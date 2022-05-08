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

    let private cookies = CookieContainer()

    /// see https://stackoverflow.com/questions/1777203/c-writing-a-cookiecontainer-to-disk-and-loading-back-in-for-use
    let private loadCookies ((_, output): IO) (config: EatonConfig) =
        asyncResult {
            // todo - if verbose
            output.SubTitle "[Eaton][Cookies] Loading cookies ..."

            if config.Credentials.Path |> File.Exists then
                use stream = File.OpenRead(config.Credentials.Path)

                let! (cc: CookieCollection) =
                    try JsonSerializer.Deserialize<CookieCollection>(stream) |> AsyncResult.ofSuccess with
                    | e -> AsyncResult.ofError (ApiError.Exception e)

                cookies.Add cc
                output.Success "✅ Cookies loaded"
            else
                output.Message "⚠️  Stored cookies were not found"
        }

    let private persistCookies ((_, output): IO) (config: EatonConfig) =
        asyncResult {
            output.SubTitle "[Eaton][Cookies] Saving cookies ..."

            config.Credentials.Path |> Path.GetDirectoryName |> Directory.ensure
            use stream = File.Create(config.Credentials.Path)

            do! JsonSerializer.SerializeAsync(stream, cookies.GetAllCookies())
            output.Success "✅ Cookies saved"
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

    [<AutoOpen>]
    module private Http =
        open FSharp.Data.HttpRequestHeaders

        type Url = Url of string
        type Api = Api of string
        type Path = Api -> Url

        let path (path: string): Path = fun (Api api) -> Url <| sprintf "%s/%s" (api.TrimEnd '/') (path.TrimStart '/')

        [<RequireQualifiedAccess>]
        module Url =
            let asUri (Url url) = Uri url

        [<RequireQualifiedAccess>]
        module Path =
            let asUri (api: Api) (path: Path) =
                api |> path |> Url.asUri

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

    let inline private (/) a b = Path.Combine(a, b)

    let private login ((_, output) as io: IO) config api = asyncResult {
        output.Section "[Eaton] Logging in ..."

        if cookies.Count = 0 then
            do! loadCookies io config

        let loginPath = path "/system/http/login"

        match cookies.GetCookies(loginPath |> Path.asUri api) with
        | eatonCookies when eatonCookies.Count > 0 ->
            output.Success "✅ Done (already logged in)"
            return ()

        | _ ->
            // todo - log "logging in"
            let! (response: HttpResponse) =
                [
                    "u" => config.Credentials.Name
                    "p" => config.Credentials.Password
                ]
                |> Http.postForm api loginPath

            let! _ =
                response.Cookies
                |> Map.tryFind "JSESSIONID"
                |> Result.ofOption (ApiError.Message "Missing session id cookie")

            do! persistCookies io config
            output.Success "✅ Done"

            return ()
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

            output.Success "✅ Done"

        let createDownloadedFile file (contentStream: Stream) =
            asyncResult {
                use (outputFile: FileStream) = new FileStream(file, FileMode.Create)

                return! contentStream.CopyToAsync outputFile
            }
            |> AsyncResult.mapError ApiError.Exception

        asyncResult {
            let api = Api $"http://{config.Host}"

            let targetDir =
                config.History.DownloadDirectory
                |> tee Directory.ensure

            let configFile = targetDir / "config.zip"
            let tmpHistoryFilePath = targetDir / "configuration" / "xComfortPortocolAdapter.xml"

            deleteTemporaryFiles targetDir configFile tmpHistoryFilePath

            do! api |> login io config

            output.Section "[Eaton] Downloading ..."
            let! (response: HttpResponseWithStream) = Http.getStream api (path "/BackupRestore/History?filename=history")
            output.Success "✅ Done"

            output.Section "[Eaton] Create a file ..."
            do! createDownloadedFile configFile response.ResponseStream
            output.Success "✅ Done"

            output.Section "[Eaton] Extracting a file ..."
            try ZipFile.ExtractToDirectory(configFile, targetDir) with
            | e ->
                output.Error (sprintf "Error: %A" e)
                return! Error (ApiError.Exception e)

            if tmpHistoryFilePath |> File.Exists |> not then
                return! Error (ApiError.Message $"History file ({tmpHistoryFilePath}) does not exists after extraction.")
            output.Success "✅ Done"

            output.Section "[Eaton] Move history file to its directory ..."
            let now = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")
            let historyFilePath = targetDir / $"history_{now}.xml"
            File.Move (tmpHistoryFilePath, historyFilePath)
            output.Success "✅ Done"

            deleteTemporaryFiles targetDir configFile tmpHistoryFilePath

            return tmpHistoryFilePath
        }

    type private DevicesSchema = JsonProvider<"schema/diagnosticsPhysicalDevicesResponse.json", SampleIsList = true>

    let getDeviceList ((_, output) as io: IO) (config: EatonConfig): AsyncResult<Device list, ApiError> =
        asyncResult {
            let api = Api $"http://{config.Host}"

            do! api |> login io config

            output.Section "[Eaton] Downloading ..."
            let rpc =
                [
                    "id" => (42 :> obj) // todo ??
                    "jsonrpc" => ("2.0" :> obj)
                    "method" => ("Diagnostics/getPhysicalDevices" :> obj)
                    "params" => ([] :> obj)
                ]
                |> Map.ofList
                :> obj

            let! (response: string) = Http.post api (path "/remote/json-rpc") rpc
            output.Success "✅ Done"

            output.Section "[Eaton] Parsing devices ..."
            let! (devices: DevicesSchema.Root) =
                try response |> DevicesSchema.Parse |> AsyncResult.ofSuccess with
                | e -> AsyncResult.ofError (ApiError.Exception e)

            devices.Result
            |> Seq.map (fun item ->
                [
                    item.Name
                    item.Type |> Option.defaultValue "-"
                ]
            )
            |> Seq.toList
            |> output.Table ["Device"; "Type"]

            // todo - parse response
            output.NewLine()
            output.Message "⚠️  TODO"

            return []
        }
