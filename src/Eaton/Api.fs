namespace MF.Eaton

[<RequireQualifiedAccess>]
module Api =
    open System
    open System.IO
    open System.IO.Compression
    open System.Net
    open System.Text.Json

    open FSharp.Data

    open MF.ConsoleApplication
    open MF.Utils
    open MF.Utils.Option.Operators
    open MF.ErrorHandling

    type IO = MF.ConsoleApplication.IO

    let mutable private cookies = CookieContainer()

    /// see https://stackoverflow.com/questions/1777203/c-writing-a-cookiecontainer-to-disk-and-loading-back-in-for-use
    let private loadCookies ((_, output): IO) (config: EatonConfig) =
        asyncResult {
            if output.IsVerbose() then output.SubTitle "[Eaton][Cookies] Loading cookies ..."

            if config.Credentials.Path |> File.Exists then
                use stream = File.OpenRead(config.Credentials.Path)

                let! (cc: CookieCollection) =
                    try JsonSerializer.Deserialize<CookieCollection>(stream) |> AsyncResult.ofSuccess with
                    | e -> AsyncResult.ofError (ApiError.Exception e)

                cookies.Add cc
                if output.IsVerbose() then output.Success "✅ Cookies loaded"
            else
                if output.IsVerbose() then output.Message "⚠️  Stored cookies were not found"
        }

    let private persistCookies ((_, output): IO) (config: EatonConfig) =
        asyncResult {
            if output.IsVerbose() then output.SubTitle "[Eaton][Cookies] Saving cookies ..."

            config.Credentials.Path |> Path.GetDirectoryName |> Directory.ensure
            use stream = File.Create(config.Credentials.Path)

            do! JsonSerializer.SerializeAsync(stream, cookies.GetAllCookies())
            if output.IsVerbose() then output.Success "✅ Cookies saved"
        }
        |> AsyncResult.mapError ApiError.Exception

    [<RequireQualifiedAccess>]
    module private Serialize =
        module private Json =
            open Newtonsoft.Json
            open Newtonsoft.Json.Serialization

            let private options () =
                JsonSerializerSettings (
                    ContractResolver =
                        DefaultContractResolver (
                            NamingStrategy = SnakeCaseNamingStrategy()
                        )
                )

            let serialize obj =
                JsonConvert.SerializeObject (obj, options())

            let serializePretty obj =
                let options = options()
                options.Formatting <- Formatting.Indented

                JsonConvert.SerializeObject (obj, options)

        let toJsonPretty: obj -> string = Json.serializePretty
        let toJson: obj -> string = Json.serialize

    [<RequireQualifiedAccess>]
    module private Http =
        open FSharp.Data.HttpRequestHeaders

        type private Path = HttpTypes.Path

        let path (path: string): Path = fun (Api api) ->
            sprintf "%s/%s" (api.TrimEnd '/') (path.TrimStart '/') |> Url

        [<RequireQualifiedAccess>]
        module Url =
            let asUri (Api api) (Url url) =
                try
                    /// see https://stackoverflow.com/questions/2887924/invalid-uri-the-format-of-the-uri-could-not-be-determined
                    let host = Uri api
                    let path = Uri(url.Replace(api, ""), UriKind.Relative)

                    Uri (host, path)
                with
                | e -> failwithf "Url %A is invalid, due to %A" url e

        [<RequireQualifiedAccess>]
        module Path =
            let asUri (api: Api) (path: Path) =
                api |> path |> Url.asUri api

        let getStream api (path: Path): AsyncResult<HttpResponseWithStream, ApiError> =
            asyncResult {
                let (Url url) = api |> path

                return!
                    Http.AsyncRequestStream (
                        url,
                        httpMethod = "GET",
                        cookieContainer = cookies
                    )
            }
            |> AsyncResult.mapError ApiError.Exception

        let get (api: Api) (path: Path): AsyncResult<string, ApiError> =
            asyncResult {
                let (Url url) = api |> path

                return!
                    Http.AsyncRequestString (
                        url,
                        httpMethod = "GET",
                        cookieContainer = cookies
                    )
            }
            |> AsyncResult.mapError ApiError.Exception

        let post (api: Api) (path: Path) (body: obj): AsyncResult<string, ApiError> =
            asyncResult {
                let (Url url) = api |> path

                return!
                    Http.AsyncRequestString (
                        url,
                        headers = (
                            [
                                Accept "application/json"
                                ContentType "application/json"
                            ]
                        ),
                        body = TextRequest (body |> Serialize.toJson),
                        cookieContainer = cookies
                    )
            }
            |> AsyncResult.mapError ApiError.Exception

        let postForm (api: Api) (path: Path) (formValues: (string * string) list): AsyncResult<HttpResponse, ApiError> =
            asyncResult {
                let (Url url) = api |> path

                return!
                    Http.AsyncRequest (
                        url,
                        headers = (
                            [
                                Accept "text/html"
                                ContentType "application/x-www-form-urlencoded"
                            ]
                        ),
                        body = FormValues formValues,
                        cookieContainer = cookies
                    )
            }
            |> AsyncResult.mapError ApiError.Exception

    module RPC =
        let mutable private id = 1000

        type Request<'Params> = {
            Id: int
            Method: string
            Parameters: 'Params
        }

        [<RequireQualifiedAccess>]
        module Request =
            let create method parameters =
                id <- id + 1
                {
                    Id = id
                    Method = method
                    Parameters = parameters
                }

        let call config request = asyncResult {
            let rpc =
                [
                    "id" => (request.Id :> obj)
                    "jsonrpc" => ("2.0" :> obj)
                    "method" => (request.Method :> obj)
                    "params" => (request.Parameters :> obj)
                ]
                |> Map.ofList
                :> obj

            let! (response: string) =
                rpc
                |> Http.post config.Host (Http.path "/remote/json-rpc")

            return response
        }

    let inline private (/) a b = Path.Combine(a, b)

    let private retryOnUnathorized ((_, output): IO) (config: EatonConfig) action =
        action
        |> AsyncResult.bindError (function
            | (ApiError.Exception Http.Unauthorized) ->
                if output.IsVerbose() then output.Message "[Retry] On Unauthorized action ..."

                if output.IsVeryVerbose() then output.Message "[Retry] Clear cookies ..."
                cookies <- CookieContainer()

                if config.Credentials.Path |> File.Exists then
                    if output.IsVeryVerbose() then output.Message "[Retry] Remove credentials file to force login by form ..."
                    cookies.GetAllCookies().Clear()
                    config.Credentials.Path |> File.Delete
                elif output.IsVeryVerbose() then output.Message "[Retry] Credentials file is not presented ..."

                if output.IsVeryVerbose() then output.Message "[Retry] Run action again ..."
                action
            | e -> AsyncResult.ofError e
        )

    let private login ((_, output) as io: IO) config = asyncResult {
        output.Section "[Eaton] Logging in ..."

        if cookies.Count = 0 then
            do! loadCookies io config

        let loginPath = Http.path "/system/http/login"

        match cookies.GetCookies(loginPath |> Http.Path.asUri config.Host) with
        | eatonCookies when eatonCookies.Count > 0 ->
            output.Success "Done (already logged in)"
            return ()

        | _ ->
            output.Message "Logging in by a form ..."

            let! (response: HttpResponse) =
                [
                    "u" => config.Credentials.Name
                    "p" => config.Credentials.Password
                ]
                |> Http.postForm config.Host loginPath

            let! _ =
                response.Cookies
                |> Map.tryFind "JSESSIONID"
                |> Result.ofOption (ApiError.Message "Missing session id cookie")

            do! persistCookies io config
            output.Success "Done"
    }

    let downloadHistoryFile ((_, output) as io: IO) (config: EatonConfig) =
        let deleteTemporaryFiles targetDir configFile tmpHistoryFilePath =
            output.Section "[Eaton] Clear temporary files ..."
            [
                configFile
                tmpHistoryFilePath
            ]
            |> List.filter File.Exists
            |> List.iter File.Delete

            if Directory.Exists (targetDir / "configuration") then
                Directory.Delete (targetDir / "configuration")

            output.Success "Done"

        let createDownloadedFile file (contentStream: Stream) =
            asyncResult {
                use (outputFile: FileStream) = new FileStream(file, FileMode.Create)

                return! contentStream.CopyToAsync outputFile
            }
            |> AsyncResult.mapError ApiError.Exception

        asyncResult {
            let targetDir =
                config.History.DownloadDirectory
                |> tee Directory.ensure

            let configFile = targetDir / "config.zip"
            let tmpHistoryFilePath = targetDir / "configuration" / "xComfortPortocolAdapter.xml"

            deleteTemporaryFiles targetDir configFile tmpHistoryFilePath

            do! login io config

            output.Section "[Eaton] Downloading ..."
            let! (response: HttpResponseWithStream) = Http.getStream config.Host (Http.path "/BackupRestore/History?filename=history")
            output.Success "Done"

            output.Section "[Eaton] Create a file ..."
            do! createDownloadedFile configFile response.ResponseStream
            output.Success "Done"

            output.Section "[Eaton] Extracting a file ..."
            try ZipFile.ExtractToDirectory(configFile, targetDir) with
            | e ->
                output.Error (sprintf "Error: %A" e)
                return! Error (ApiError.Exception e)

            if tmpHistoryFilePath |> File.Exists |> not then
                return! Error (ApiError.Message $"History file ({tmpHistoryFilePath}) does not exists after extraction.")
            output.Success "Done"

            output.Section "[Eaton] Move history file to its directory ..."
            let now = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")
            let historyFilePath = targetDir / $"history_{now}.xml"
            File.Move (tmpHistoryFilePath, historyFilePath)
            output.Success "Done"

            deleteTemporaryFiles targetDir configFile tmpHistoryFilePath

            return tmpHistoryFilePath
        }
        |> retryOnUnathorized io config

    type private DevicesSchema = JsonProvider<"schema/diagnosticsPhysicalDevicesResponse.json", SampleIsList = true>
    type private DevicesStatusSchema = JsonProvider<"schema/diagnosticsPhysicalDevicesWithLogStatsResponse.json", SampleIsList = true>
    type private StatItemSchema = JsonProvider<"schema/statsItem.json", SampleIsList = true>

    open MF.ErrorHandling.Option.Operators

    let getDeviceList ((_, output) as io: IO) (config: EatonConfig): AsyncResult<Device list, ApiError> =
        asyncResult {
            do! login io config

            output.Section "[Eaton] Downloading ..."
            let! response =
                RPC.Request.create "Diagnostics/getPhysicalDevices" []
                |> RPC.call config

            output.Success "Done"

            output.Section "[Eaton] Parsing devices ..."
            let! (devices: DevicesSchema.Root) =
                try response |> DevicesSchema.Parse |> AsyncResult.ofSuccess with
                | e -> AsyncResult.ofError (ApiError.Exception e)

            let deviceGroups =
                devices.Result
                |> Seq.map (fun item ->
                    {
                        Name = item.Name.Trim()
                        DisplayName = ""    // todo - parse from user device list - there is a custom name

                        DeviceId = DeviceId item.DeviceUid
                        Parent = item.ParentId <!> DeviceId

                        SerialNumber = item.SerialNr >>= parseInt <?=> 0

                        Type =
                            match item.Type with
                            | Contains "Room Controller" -> Termostat
                            | other -> Other other

                        Rssi =
                            match item.RssiId, item.Rssi with
                            | Some 1, Some status -> Rssi.Enabled status
                            | Some 0, _ -> Rssi.Disabled
                            | other -> Rssi.Other other

                        Powered =
                            match item.PoweredId, item.Powered with
                            | Some 6, Contains "Sítově Napájené" -> PowerStatus.Always
                            | Some 1, Some status -> PowerStatus.Battery status
                            | other -> PowerStatus.Other other

                        Children = []
                    }
                )
                // todo - remove folowing filter
                |> Seq.filter (fun device ->
                    let contains part = (device.Parent <?=> device.DeviceId) |> DeviceId.contains part
                    [ "4131920"; "8649687" ] |> List.exists contains
                )
                |> Seq.groupBy (fun device -> device.Parent <?=> device.DeviceId)
                |> Seq.toList

            if output.IsVeryVerbose() then
                deviceGroups
                |> Seq.iter (fun (device, devices) ->
                    output.Section <| sprintf "%A" device

                    devices
                    |> Seq.toList
                    |> List.sortBy (fun device -> device.Parent <?=> device.DeviceId)
                    |> List.map (fun device -> [
                        device.Name
                        device.DisplayName
                        device.SerialNumber |> sprintf "%A"
                        device.Type |> sprintf "%A"
                        device.Rssi |> sprintf "%A"
                        device.Powered |> sprintf "%A"
                    ])
                    |> output.Table [
                        "Name"
                        "DisplayName"
                        "SerialNumber"
                        "Type"
                        "Rssi"
                        "Powered"
                    ]
                    |> output.NewLine
                )

            let devices =
                deviceGroups
                |> List.fold
                    (fun (acc: Device list) (_, devices) ->
                        let devices = devices |> List.ofSeq

                        let device =
                            devices
                            |> List.pick (function
                                | { Parent = None } as main -> Some main
                                | _ -> None
                            )

                        let device =
                            { device with
                                Children =
                                    devices
                                    |> List.choose (function
                                        | { Parent = Some _ } as child -> Some child
                                        | _ -> None
                                    )
                            }

                        device :: acc
                    )
                    []

            return devices
        }
        |> retryOnUnathorized io config

    let getDeviceStatuses ((_, output) as io: IO) (config: EatonConfig): AsyncResult<Map<DeviceId, DeviceStat>, ApiError> =
        asyncResult {
            do! login io config

            output.Section "[Eaton] Get Device Statuses"
            let! response =
                RPC.Request.create "Diagnostics/getPhysicalDevicesWithLogStats" []
                |> RPC.call config

            output.Success "Done"

            output.Section "[Eaton] Parsing statuses ..."
            let! (stats: DevicesStatusSchema.Root) =
                try response |> DevicesStatusSchema.Parse |> AsyncResult.ofSuccess with
                | e -> AsyncResult.ofError (ApiError.Exception e)

            if output.IsVerbose() then
                output.Message " -> <c:green>parsed</c>"
                output.SubTitle "Parsing stats ..."

            let! deviceStats =
                stats.Result.JsonValue.Properties()
                |> Seq.map (fun (device, value) -> result {
                    let! parsed =
                        try value.ToString() |> StatItemSchema.Parse |> Ok
                        with e -> Error (ApiError.Message <| sprintf "There is an error while parsing device %A value %A:\n%A" device value e)

                    return DeviceId device, {
                        MessagesPerDay = parsed.MsgsPerDay
                        LastMsgTimeStamp = parsed.LastMsgTimeStamp
                        EventLog =
                            match parsed.EventLog.Number, parsed.EventLog.String with
                            | Some number, _ -> StatValue.Decimal number
                            | _, Contains "ON"
                            | _, Contains "OPEN" -> StatValue.On

                            | _, Contains "OFF"
                            | _, Contains "CLOSE" -> StatValue.Off

                            | _, Some string -> StatValue.String string
                            | _ -> parsed.EventLog.ToString() |> StatValue.String
                    }
                })
                |> List.ofSeq
                |> Validation.ofResults
                |> Result.mapError ApiError.Errors

            if output.IsVerbose() then output.Message " -> <c:green>done</c>"

            if output.IsVeryVerbose() then
                deviceStats
                |> List.map (fun (DeviceId device, stats) -> [
                    let error = "<c:red>err</c>"

                    yield device
                    yield try stats.EventLog |> sprintf "<c:magenta>%A</c>" with _ -> error
                    yield try stats.LastMsgTimeStamp.ToString() |> sprintf "<c:cyan>%s</c>" with _ -> error
                ])
                |> output.Table [ "Device"; "Value"; "Last Updated" ]

            return deviceStats |> Map.ofList
        }
        |> retryOnUnathorized io config
