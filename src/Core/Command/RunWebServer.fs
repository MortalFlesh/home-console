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
    open MF.ConsoleApplication
    open MF.ErrorHandling
    open MF.Utils
    open MF.Utils.Logging
    open MF.HomeConsole.Console
    open MF.HomeConsole.WebServer
    open MF.Eaton

    let arguments = []
    let options = [
        Console.Option.config

        Option.optional "host" None "Host IP of the eaton controller." None
        Option.optional "name" None "Name for eaton controller." (Some "admin")
        Option.optional "password" None "Password for eaton controller." None
        Option.optional "cookies-path" None "Path for a credentials file." (Some "./eaton-cookies.json")
        Option.optional "history-path" None "Path for a downloaded history directory." (Some "./eaton-history")
    ]

    let execute port = executeAsyncResult <| fun (input, output) -> asyncResult {
        output.SubTitle "Starting ..."

        let optionValue option = input |> Input.Option.value option

        let directConfig =
            try Some {
                Eaton = {
                    Host = optionValue "host" |> Api.create
                    Credentials = {
                        Name = optionValue "name"
                        Password = optionValue "password"
                        Path = optionValue "cookies-path"
                    }
                    History = {
                        DownloadDirectory = optionValue "history-path"
                    }
                }
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

        if output.IsDebug() then
            output.Table [ "Config"; "Value" ] [
                [ "Eaton.Host"; config.Eaton.Host |> sprintf "%A" ]
                [ "Eaton.Name"; config.Eaton.Credentials.Name |> sprintf "%A" ]
                [ "Eaton.Path"; config.Eaton.Credentials.Path |> sprintf "%A" ]
                [ "Eaton.History"; config.Eaton.History.DownloadDirectory |> sprintf "%A" ]
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
