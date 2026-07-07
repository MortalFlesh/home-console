namespace MF.HomeConsole

[<RequireQualifiedAccess>]
module RunWebServerCommand =
    open System
    open System.Collections.Concurrent
    open System.IO
    open FSharp.Data
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.Extensions.Logging
    open Giraffe
    open Saturn
    open Feather.ConsoleApplication
    open Feather.ErrorHandling
    open MF.Utils
    open MF.Utils.Logging
    open MF.HomeConsole.Console
    open MF.HomeConsole.WebServer
    open MF.Eaton

    let arguments = []
    let options = [
        Console.Option.config

        Option.optional "data-path" None "Persistent data directory (default: /data falling back to ./data)." None
        Option.optional "host" None "Host IP of the eaton controller." None
        Option.optional "name" None "Name for eaton controller." (Some "admin")
        Option.optional "password" None "Password for eaton controller." None
        Option.optional "cookies-path" None "Path for a credentials file (default: <data-path>/eaton-cookies.json)." None
        Option.optional "history-path" None "Path for a downloaded history directory (default: <data-path>/eaton-history)." None
    ]

    let execute port = executeAsyncResult <| fun (input, output) -> asyncResult {
        output.SubTitle "Starting ..."

        let optionValue option = input |> Input.Option.value option

        let resolveDataDir (rawPath: string) =
            if rawPath <> "" then rawPath
            else
                let isWritable (dir: string) =
                    try
                        if not (System.IO.Directory.Exists(dir)) then false
                        else
                            let testFile = System.IO.Path.Combine(dir, ".write-test")
                            System.IO.File.WriteAllText(testFile, "")
                            System.IO.File.Delete(testFile)
                            true
                    with _ -> false
                if isWritable "/data" then "/data"
                else "./data"

        let dataDir = optionValue "data-path" |> resolveDataDir

        let deriveOrUse option derived =
            let v = optionValue option
            if v <> "" then v else derived

        let directConfig =
            try Some {
                Eaton = {
                    Host = optionValue "host" |> Api.create
                    Credentials = {
                        Name = optionValue "name"
                        Password = optionValue "password"
                        Path = deriveOrUse "cookies-path" (System.IO.Path.Combine(dataDir, "eaton-cookies.json"))
                    }
                    History = {
                        DownloadDirectory = deriveOrUse "history-path" (System.IO.Path.Combine(dataDir, "eaton-history"))
                    }
                }
                Data = { Directory = dataDir }
            }
            with e ->
                if output.IsVeryVerbose() then output.Warning("Config from option values is not valid: %s", e.Message)
                None

        let! config =
            directConfig
            |> Option.orElseWith (fun _ ->
                input
                |> Input.config
                |> Config.parse
            )
            |> Result.ofOption (CommandError.Message "Missing configuration")

        // When using a file-based config that omits data.directory, fill in the resolved default
        let config =
            if config.Data.Directory <> "" then config
            else { config with Data = { Directory = dataDir } }

        if output.IsDebug() then
            output.Table [ "Config"; "Value" ] [
                [ "Eaton.Host"; config.Eaton.Host |> sprintf "%A" ]
                [ "Eaton.Name"; config.Eaton.Credentials.Name |> sprintf "%A" ]
                [ "Eaton.Path"; config.Eaton.Credentials.Path |> sprintf "%A" ]
                [ "Eaton.History"; config.Eaton.History.DownloadDirectory |> sprintf "%A" ]
                [ "Data.Directory"; config.Data.Directory |> sprintf "%A" ]
            ]

        use loggerFactory =
            "normal"
            |> LogLevel.parse
            |> LoggerFactory.create

        output.Section "Run webserver"
        let rec mapApiError = function
            | ApiError.Exception e -> CommandError.Exception e
            | ApiError.Message m -> CommandError.Message m
            | ApiError.Errors errors -> errors |> List.map mapApiError |> CommandError.Errors

        do!
            port
            |> WebServer.run loggerFactory (input, output) config
            |> AsyncResult.mapError mapApiError

        output.Success "Done"

        return ExitCode.Success
    }
