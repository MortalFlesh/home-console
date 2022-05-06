namespace MF.Eaton

[<RequireQualifiedAccess>]
module Api =
    open System
    open System.IO
    open System.IO.Compression
    open FSharp.Data
    open MF.Utils
    open MF.Utils.Option.Operators
    open MF.ErrorHandling

    module Http =
        open FSharp.Data.HttpRequestHeaders

        type Url = Url of string
        type Api = Api of string
        type Path = Api -> Url

        let get cookies (path: Path) (api: Api): AsyncResult<HttpResponseWithStream, ApiError> =
            asyncResult {
                let (Url url) = api |> path

                return!
                    Http.AsyncRequestStream (
                        url,
                        httpMethod = "GET",
                        cookies = cookies
                    )
            }
            |> AsyncResult.mapError ApiError.Exception

        let post (path: Path) (api: Api) (request: (string * string) list): AsyncResult<HttpResponse, ApiError> =
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
                        body = FormValues request
                    )
            }
            |> AsyncResult.mapError ApiError.Exception

    let inline private (/) a b = Path.Combine(a, b)

    let run (config: EatonConfig) =
        // todo - funguje to, ale chce to doladit (vytvorit si slozku, smazat predchozi soubor, ... a pak mozna i rovnou rozbalit ten zip)
        // todo - taky tahat veci z configu

        asyncResult {
            let api = Http.Api $"http://{config.Host}"
            let path: Http.Path = fun (Http.Api api) -> Http.Url ($"{api}/system/http/login")
            let body = [
                "u" => config.Credentials.Name
                "p" => config.Credentials.Password
            ]

            let targetDir =
                config.History.Download
                |> tee Directory.ensure

            let configFile = targetDir / "config.zip"

            // todo - kouknout na cookie container
            // open System.Net
            // let cc = CookieContainer()

            printfn "Loging ..."
            let! (response: HttpResponse) = Http.post path api body
            //printfn "Body: %A" response.Body
            printfn "StatusCode: %A" response.StatusCode
            printfn "Cookies: %A" response.Cookies

            let! _ =
                response.Cookies
                |> Map.tryFind "JSESSIONID"
                |> Result.ofOption (ApiError.Message "Missing session id cookie")

            printfn "Downloading ..."
            let path: Http.Path = fun (Http.Api api) -> Http.Url ($"{api}/BackupRestore/History?filename=history")
            let! (response: HttpResponseWithStream) = Http.get (response.Cookies |> Map.toSeq) path api

            //printfn "Body: %A" response.Body
            printfn "StatusCode: %A" response.StatusCode
            printfn "Cookies: %A" response.Cookies

            printfn "Create a file ..."
            use (outputFile: FileStream) = new FileStream(configFile, FileMode.Create)
            do! (asyncResult {
                return! response.ResponseStream.CopyToAsync outputFile
            } |> AsyncResult.mapError ApiError.Exception)

            printfn "Extracting a file ..."
            ZipFile.ExtractToDirectory(configFile, targetDir)

            return ()
        }
