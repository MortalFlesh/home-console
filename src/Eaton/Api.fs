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
    open MF.JsonRpc

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
        let mutable private id = 0

        [<RequireQualifiedAccess>]
        module Request =
            let createWithoutParameters method =
                id <- id + 1
                {
                    Id = id
                    Method = Method method
                    Parameters = RawJson (RawJsonData (JsonValue.Array [||]))
                }

            let create method parameters =
                id <- id + 1
                {
                    Id = id
                    Method = Method method
                    Parameters = parameters
                }

        let call config (request: Request) = asyncResult {
            let post = Http.post config.Host (Http.path "/remote/json-rpc")

            let! (response: Response) =
                request
                |> JsonRpcCall.send post
                |> AsyncResult.mapError (function
                    | JsonRpcCallError.RequestError e -> e
                    | JsonRpcCallError.ResponseError e -> e |> sprintf "%A" |> ApiError.Message
                )

            return response
        }

    let inline private (/) a b = Path.Combine(a, b)

    let private retryOnUnauthorized ((_, output): IO) (config: EatonConfig) action =
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

            output.Section "[Eaton] Downloading history ..."
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
            let now = DateTimeOffset.Now.ToString("yyyy-MM-dd-HH-mm-ss")
            let historyFilePath = targetDir / $"history_{now}.xml"
            File.Move (tmpHistoryFilePath, historyFilePath)
            output.Success "Done"

            deleteTemporaryFiles targetDir configFile tmpHistoryFilePath

            return tmpHistoryFilePath
        }
        |> retryOnUnauthorized io config

    type private DeviceItemSchema = JsonProvider<"schema/diagnosticsPhysicalDevicesResponse.json", SampleIsList = true>
    type private DevicesStatusSchema = JsonProvider<"schema/diagnosticsPhysicalDevicesWithLogStatsResponse.json">
    type private StatItemSchema = JsonProvider<"schema/statsItem.json", SampleIsList = true>
    type private ZoneItemSchema = JsonProvider<"schema/zoneItem.json", SampleIsList = true>
    type private DeviceInZoneItemSchema = JsonProvider<"schema/deviceInZoneItem.json", SampleIsList = true>
    type private SceneItemSchema = JsonProvider<"schema/sceneItem.json", SampleIsList = true>

    open MF.ErrorHandling.Option.Operators

    let getZoneList ((_, output) as io: IO) (config: EatonConfig): AsyncResult<Zone list, ApiError> =
        asyncResult {
            do! login io config

            output.Section "[Eaton] Downloading zones ..."
            let! (response: Response) =
                RPC.Request.createWithoutParameters "HFM/getZones"
                |> RPC.call config

            output.Success "Done"

            let devicesInZone (zone: Zone) = asyncResult {
                output.Message <| sprintf "<c:dark-yellow>[Eaton] Downloading devices in zone</c> %A [<c:cyan>%s</c>] ..." zone.Name (zone.Id |> ZoneId.value)

                let! (response: Response) =
                    [ zone.Id |> ZoneId.value ]
                    :> obj
                    |> Dto
                    |> RPC.Request.create "XDC/getDevices"
                    |> RPC.call config

                let! devicesInZone =
                    response
                    |> Response.tryParseResultAsJsonList
                    |> List.map (fun item ->
                        try
                            let parsed = item |> DeviceInZoneItemSchema.Parse
                            Ok {
                                DeviceId = DeviceId parsed.DeviceId
                                Name = parsed.DeviceName
                            }
                        with e -> Error (ApiError.Exception e)
                    )
                    |> Validation.ofResults
                    |> Result.mapError ApiError.Errors

                output.Message "<c:green> -> done</c>"
                return devicesInZone
            }

            output.Section "[Eaton] Parsing zones ..."
            let! zones =
                response
                |> Response.tryParseResultAsJsonList
                |> List.map (fun item -> asyncResult {
                    let! zone =
                        try
                            let parsed = item |> ZoneItemSchema.Parse
                            Ok {
                                Id = ZoneId parsed.ZoneId
                                Name = parsed.ZoneName
                                Devices = []
                            }
                        with e -> Error (ApiError.Exception e)

                    let! devicesInZone = devicesInZone zone

                    return { zone with Devices = devicesInZone }
                })
                |> AsyncResult.ofSequentialAsyncResults ApiError.Exception
                |> AsyncResult.mapError ApiError.Errors

            if output.IsVeryVerbose() then
                zones
                |> List.collect (fun zone ->
                    zone.Devices
                    |> List.map (fun device -> [
                        zone.Id |> ZoneId.value
                        zone.Name
                        device.DeviceId |> DeviceId.value
                        device.Name
                    ])
                )
                |> output.Table [ "Zone ID"; "Zone"; "Device ID" ;"Device" ]

            return zones
        }
        |> retryOnUnauthorized io config

    let getDeviceList ((_, output) as io: IO) (config: EatonConfig) (zones: Zone list): AsyncResult<Device list, ApiError> =
        asyncResult {
            do! login io config

            output.Section "[Eaton] Downloading devices ..."
            let! (response: Response) =
                RPC.Request.createWithoutParameters "Diagnostics/getPhysicalDevices"
                |> RPC.call config

            output.Success "Done"

            output.Section "[Eaton] Parsing devices ..."
            let! (devices: DeviceItemSchema.Root list) =
                try
                    response
                    |> Response.tryParseResultAsJsonList
                    |> tee (fun response -> if output.IsDebug() then output.Message <| sprintf "<c:dark-yellow>[Eaton] Response</c>\n - %s\n" (response |> String.concat "\n - "))
                    |> List.map (fun item ->
                        try DeviceItemSchema.Parse item |> Ok
                        with e -> Error (ApiError.Exception e)
                    )
                    |> Validation.ofResults
                    |> Result.mapError ApiError.Errors
                with
                | e -> Error (ApiError.Exception e)

            let zoneMap = zones |> List.collect (fun zone -> zone.Devices |> List.map (fun device -> device.DeviceId, zone.Id)) |> Map.ofList

            let mainDevices =
                devices
                |> List.filter (fun item -> item.ParentId |> Option.isNone)
                |> List.map (fun item ->
                    let deviceId = DeviceId item.DeviceUid
                    if output.IsDebug() then output.Message <| sprintf "<c:dark-yellow>[Eaton] Raw device (main)</c>\n%A\n - Rssi: %A\n - Powerd: %A\n - SR: %A" item (item.RssiId, item.Rssi) (item.PoweredId, item.Powered) item.SerialNr

                    {
                        Name = item.Name.Trim()
                        DisplayName = ""    // todo - parse from user device list - there is a custom name

                        DeviceId = deviceId
                        Parent = item.ParentId <!> DeviceId

                        SerialNumber = item.SerialNr >>= parseInt <?=> 0

                        Type = item.Type |> DeviceType.parseMainType
                        Rssi = (item.RssiId, item.Rssi) |> Rssi.parse
                        Powered = (item.PoweredId, item.Powered) |> Powered.parse

                        Children = []

                        Zone = zoneMap |> Map.tryFind deviceId
                    }
                )

            let children =
                let mainDeviceMap =
                    mainDevices
                    |> List.map (fun device -> device.DeviceId, device)
                    |> Map.ofList

                devices
                |> List.choose (fun item ->
                    match item.ParentId with
                    | Some parentId ->
                        match mainDeviceMap |> Map.tryFind (DeviceId parentId) with
                        | Some parent -> Some (parent, item)
                        | None -> None
                    | None -> None
                )
                |> List.map (fun (parent, item) ->
                    let deviceId = DeviceId item.DeviceUid

                    {
                        Name = item.Name.Trim()
                        DisplayName = ""    // todo - parse from user device list - there is a custom name

                        DeviceId = deviceId
                        Parent = item.ParentId <!> DeviceId
                        SerialNumber = item.SerialNr >>= parseInt <?=> 0
                        Type = (parent.Type, item.Type) |> DeviceType.parseChildType deviceId
                        Rssi = (item.RssiId, item.Rssi) |> Rssi.tryParse <?=> parent.Rssi
                        Powered = (item.PoweredId, item.Powered) |> Powered.tryParse <?=> parent.Powered

                        Children = []

                        Zone = zoneMap |> Map.tryFind deviceId
                    }
                )
                |> List.groupBy (fun device -> device.Parent <?=> device.DeviceId)
                |> Map.ofList

            let devices =
                mainDevices
                |> List.fold (fun acc device ->
                    let children =
                        children
                        |> Map.tryFind device.DeviceId
                        |> Option.defaultValue []

                    let zone =
                        match device.Zone with
                        | Some zone -> Some zone
                        | _ ->
                            let candidate = children |> List.tryPick (fun device -> device.Zone)

                            if children |> List.forall (fun device -> device.Zone = candidate) then candidate
                            else None

                    { device with Children = children; Zone = zone } :: acc
                ) []

            if output.IsVeryVerbose() then
                let zoneNameMap = zones |> List.map (fun zone -> Some zone.Id, zone.Name) |> Map.ofList

                let fields = [
                    "Role"
                    "Name"
                    "DisplayName"
                    "SerialNumber"
                    "Type"
                    "Rssi"
                    "Powered"
                    "Zone ID"
                    "Zone"
                ]

                let values (device: Device) = [
                    if device.Parent |> Option.isNone then "<c:green>Main</c>" else "<c:cyan>Child</c>"
                    device.Name
                    device.DisplayName
                    device.SerialNumber |> sprintf "%A"
                    device.Type |> sprintf "%A"
                    device.Rssi |> sprintf "%A"
                    device.Powered |> sprintf "%A"
                    device.Zone |> Option.map ZoneId.value |> Option.defaultValue "N/A"
                    zoneNameMap |> Map.tryFind device.Zone |> Option.defaultValue "N/A"
                ]

                devices
                |> List.iter (fun device ->
                    output.Section <| sprintf "%A" device.DeviceId

                    device :: device.Children
                    |> Seq.toList
                    |> List.sortBy (fun device -> device.Parent <?=> device.DeviceId)
                    |> List.map values
                    |> output.Table fields
                    |> output.NewLine
                )

            return devices
        }
        |> retryOnUnauthorized io config

    let getDeviceStatuses ((_, output) as io: IO) (config: EatonConfig): AsyncResult<Map<DeviceId, DeviceStat>, ApiError> =
        asyncResult {
            do! login io config

            output.Section "[Eaton] Get Device Statuses"
            let! response =
                RPC.Request.createWithoutParameters "Diagnostics/getPhysicalDevicesWithLogStats"
                |> RPC.call config

            output.Success "Done"

            output.Section "[Eaton] Parsing statuses ..."
            let! (stats: DevicesStatusSchema.Root) =
                try
                    response
                    |> Response.tryParseResultAsJsonString
                    |> Result.ofOption (ApiError.Message "Invalid response")
                    |> Result.map DevicesStatusSchema.Parse
                    |> AsyncResult.ofResult
                with
                | e -> AsyncResult.ofError (ApiError.Exception e)

            if output.IsVerbose() then
                output.Message " -> <c:green>parsed</c>"
                output.SubTitle "Parsing stats ..."

            let! deviceStats =
                stats.JsonValue.Properties()
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
                            | _, String.OptionContains "ON"
                            | _, String.OptionContains "OPEN" -> StatValue.On

                            | _, String.OptionContains "OFF"
                            | _, String.OptionContains "CLOSE" -> StatValue.Off

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
        |> retryOnUnauthorized io config

    let getSceneList ((_, output) as io: IO) (config: EatonConfig) (zones: Zone list): AsyncResult<Scene list, ApiError> =
        asyncResult {
            do! login io config

            output.Section "[Eaton] Downloading scenes ..."
            let! (response: Response) =
                [ true ]    // not sure what it is
                :> obj
                |> Dto
                |> RPC.Request.create "SceneFunction/getDashboardTiles"
                |> RPC.call config

            output.Success "Done"

            let zoneMap = zones |> List.map (fun zone -> zone.Id, zone) |> Map.ofList

            output.Section "[Eaton] Parsing scenes ..."
            let! scenes =
                response
                |> Response.tryParseResultAsJsonList
                |> List.map (fun item -> asyncResult {
                    let! parsed =
                        try item |> SceneItemSchema.Parse |> Ok
                        with e -> Error (ApiError.Exception e)

                    let! (zoneId, sceneId) =
                        parsed.Actions
                        |> Seq.tryFind (fun action -> action.ActionMethod = "SceneFunction/triggerScene")
                        |> Option.bind (fun action ->
                            match action.ActionParams with
                            | [| zoneId; sceneId |] ->
                                let zoneId = ZoneId zoneId

                                if zoneId |> zoneMap.ContainsKey
                                then Some (zoneId, SceneId sceneId)
                                else None
                            | _ -> None
                        )
                        |> Result.ofOption (ApiError.Message "Invalid action parameters")

                    return {
                        Id = sceneId
                        Name = parsed.Data.Texts |> Seq.head
                        Zone = zoneId
                    }
                })
                |> AsyncResult.ofSequentialAsyncResults ApiError.Exception
                |> AsyncResult.mapError ApiError.Errors

            if output.IsVeryVerbose() then
                let zoneNameMap = zones |> List.map (fun zone -> zone.Id, zone.Name) |> Map.ofList

                scenes
                |> List.map (fun scene -> [
                    scene.Zone |> ZoneId.value
                    zoneNameMap[scene.Zone]
                    scene.Id |> SceneId.value
                    scene.Name
                ])
                |> output.Table [ "Zone ID"; "Zone"; "Scene ID"; "Scene" ]

            return scenes
        }
        |> retryOnUnauthorized io config

    type ChangeDeviceState = {
        Room: ZoneId
        Device: DeviceId
        State: DeviceState
    }

    and DeviceState =
        | Density of int
        | On | Off
        | Toggle
        | Open | Close | Stop | StepOpen | StepClose    // ShutterActuator

    [<RequireQualifiedAccess>]
    module DeviceState =
        let value = function
            | Density density -> string density

            | On -> "on"
            | Off -> "off"
            | Toggle -> "toggle"

            | Open -> "open"
            | Close -> "close"
            | Stop -> "stop"
            | StepOpen -> "stepOpen"
            | StepClose -> "stepClose"

    type private GetDeviceStateSchema = JsonProvider<"schema/getDeviceStateResponse.json", SampleIsList=true>

    [<RequireQualifiedAccess>]
    module DeviceStates =
        open MF.Utils
        open MF.Utils.ConcurrentCache

        type HeatingStats =
            {
                Power: float
                CurrentTemperature: float
                PowerPercentage: int
                Overload: bool
            }

            with
                override this.ToString() =
                    sprintf "%.1f °C | %d %% (%.1f W)%s" this.CurrentTemperature this.PowerPercentage this.Power (if this.Overload then " - overload!" else "")

        let mutable lastUpdated = DateTimeOffset.Now
        let private isOnCache: Cache<ZoneId * DeviceId, bool> = create()
        let private heatingCache: Cache<ShortDeviceId, HeatingStats> = create()

        type CurrentState = {
            DeviceId: DeviceId
            IsOn: bool
            HeatingStats: HeatingStats option
        }

        let private getDeviceStates (_, output as io) config zone = asyncResult {
            do! login io config

            output.Section ("[Eaton] Get Devices States in the Zone <c:cyan>%s</c>", zone |> ZoneId.value)
            let! response =
                [
                    zone |> ZoneId.value
                    ""
                ]
                :> obj
                |> Dto
                |> RPC.Request.create "StatusControlFunction/getDevices"
                |> RPC.call config

            let stateValues =
                response
                |> Response.tryParseResultAsJsonList
                |> List.map (fun state ->
                    try
                        let parsed = state |> GetDeviceStateSchema.Parse

                        let decimalValue =
                            match parsed.Value.Number, parsed.Value.String with
                            | Some number, _ -> Some number
                            | _ -> None

                        let isOn =
                            match parsed.Value.Number, parsed.Value.String with
                            | Some number, _ -> number > (decimal 0)
                            | _, Some string ->
                                let lower = string.ToLower()
                                lower = "on" || lower = "open"
                            | _ -> false

                        let heating =
                            match parsed.Status with
                            | Some status when status.PowerDisplay ->
                                Some {
                                    Power = float status.Power
                                    CurrentTemperature = status.Temperature
                                    PowerPercentage = decimalValue |> Option.map int |> Option.defaultValue 0
                                    Overload = status.Overload
                                }

                            | _ -> None

                        Ok {
                            DeviceId = DeviceId parsed.Id
                            IsOn = isOn
                            HeatingStats = heating
                        }
                    with e -> Error (ApiError.Exception e)
                )
                |> List.choose Result.toOption

            if output.IsVeryVerbose() then
                output.Message(
                    "<c:gray>[Debug]</c> Response for %A stateValue:\n - %s",
                    zone |> ZoneId.value,
                    stateValues
                    |> List.map (function
                        | { DeviceId = DeviceId deviceId; IsOn = isOn; HeatingStats = Some heating } ->
                            sprintf "%A: <c:magenta>%A</c> | <c:red>Heating on %s</c>" deviceId isOn (heating.ToString())

                        | { DeviceId = DeviceId deviceId; IsOn = isOn } ->
                            sprintf "%A: <c:magenta>%A</c>" deviceId isOn
                    )
                    |> String.concat "\n - "
            )

            output.Success "Done"

            return stateValues
        }

        let storeState zone (deviceState: CurrentState) =
            isOnCache |> Cache.set (Key (zone, deviceState.DeviceId)) deviceState.IsOn

            deviceState.HeatingStats
            |> Option.iter (fun heating -> heatingCache |> Cache.set (deviceState.DeviceId |> DeviceId.shortId |> Key) heating)

            lastUpdated <- DateTimeOffset.Now

        let rec startLoadingState ((_, output) as io: IO) config zones: Async<unit> = async {
            output.Section "[Eaton] Loading Device States ..."

            do!
                zones
                |> List.map (fun zone -> asyncResult {
                    let! deviceStates = zone |> getDeviceStates io config

                    deviceStates
                    |> List.iter (storeState zone)
                })
                |> AsyncResult.ofSequentialAsyncResults ApiError.Exception
                |> Async.Ignore

            do! Async.Sleep (TimeSpan.FromSeconds 10)

            return! zones |> startLoadingState io config
        }

        let loadIsOnState key =
            lastUpdated, isOnCache |> Cache.tryFind (Key key)

        let allIsOnStates () =
            lastUpdated,
            isOnCache
            |> Cache.items
            |> List.map (fun (Key (zone, device), isOn) -> zone, device, isOn)

        let loadHeatingState key =
            printfn "[Heating] Loading state for %A" key
            lastUpdated, heatingCache |> Cache.tryFind (Key key)

    [<RequireQualifiedAccess>]
    module ChangeDeviceState =
        open Microsoft.AspNetCore.Http

        type private ChangeDeviceStateSchema = JsonProvider<"schema/changeStateRequest.json", SampleIsList=true>

        // todo - handle errors better
        let parse (ctx: HttpContext) = asyncResult {
            use reader = new StreamReader(ctx.Request.Body)

            let! body =
                reader.ReadToEndAsync()
                |> AsyncResult.ofTaskCatch string

            let! parsed =
                try body |> ChangeDeviceStateSchema.Parse |> Ok
                with e -> Error e.Message

            let! state =
                match parsed.Density, parsed.State with
                | Some density, _ -> Density density |> Ok
                | _, Some "on" -> On |> Ok
                | _, Some "off" -> Off |> Ok
                | _, Some "open" -> Open |> Ok
                | _, Some "close" -> Close |> Ok
                // other states are not supported yet
                | invalidState -> Error $"Invalid state: {invalidState}"

            return {
                Room = ZoneId parsed.Room
                Device = DeviceId parsed.Device
                State = state
            }
        }

    let changeDeviceState (_, output as io) config deviceState = asyncResult {
        do! login io config

        output.Section "[Eaton] Change Device State"
        let! response =
            [
                deviceState.Room |> ZoneId.value
                deviceState.Device |> DeviceId.value
                deviceState.State |> DeviceState.value
            ]
            :> obj
            |> Dto
            |> RPC.Request.create "StatusControlFunction/controlDevice"
            |> RPC.call config

        let isOn =
            match deviceState.State with
            | Density density -> density > 0
            | On | Open -> true
            | _ -> false

        DeviceStates.storeState deviceState.Room {
            DeviceId = deviceState.Device
            IsOn = isOn
            HeatingStats = None
        }

        output.Success "Done"

        return ()
    }

    type TriggerSceneOrMacro = {
        Room: ZoneId
        Scene: SceneId
    }

    [<RequireQualifiedAccess>]
    module TriggerSceneOrMacro =
        open Microsoft.AspNetCore.Http

        type private TriggerSceneOrMacroSchema = JsonProvider<"schema/triggerSceneOrMacroRequest.json", SampleIsList=true>

        // todo - handle errors better
        let parse (ctx: HttpContext) = asyncResult {
            use reader = new StreamReader(ctx.Request.Body)

            let! body =
                reader.ReadToEndAsync()
                |> AsyncResult.ofTaskCatch string

            let! parsed =
                try body |> TriggerSceneOrMacroSchema.Parse |> Ok
                with e -> Error e.Message

            return {
                Room = ZoneId parsed.Room
                Scene = SceneId parsed.Scene
            }
        }

    let triggerScene (_, output as io) config triggerScene =
        asyncResult {
            do! login io config

            output.Section "[Eaton] Trigger Scene"
            let! response =
                [
                    triggerScene.Room |> ZoneId.value
                    triggerScene.Scene |> SceneId.value
                ]
                :> obj
                |> Dto
                |> RPC.Request.create "SceneFunction/triggerScene"
                |> RPC.call config

            output.Success "Done"

            return ()
        }

    let triggerMacro (_, output as io) config triggerMacro =
        asyncResult {
            do! login io config

            output.Section "[Eaton] Trigger Macro"
            let! response =
                [
                    triggerMacro.Room |> ZoneId.value
                    triggerMacro.Scene |> SceneId.value
                ]
                :> obj
                |> Dto
                |> RPC.Request.create "MacroFunction/triggerMacro"
                |> RPC.call config

            output.Success "Done"

            return ()
        }
