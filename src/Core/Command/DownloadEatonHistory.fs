namespace MF.HomeConsole

[<RequireQualifiedAccess>]
module DownloadEatonHistory =
    open System
    open FSharp.Data
    open MF.HomeConsole.Console
    open MF.ConsoleApplication
    open MF.ErrorHandling
    open MF.Eaton

    let arguments = []
    let options = [
        Console.Option.config
    ]

    let execute = executeAsyncResult <| fun (input, output) ->
        asyncResult {
            output.SubTitle "Starting ..."

            let! config =
                input
                |> Input.config
                |> Config.parse
                |> Result.ofOption (CommandError.Message "invalid config")

            let! historyFilePath =
                config.Eaton
                |> Api.downloadHistoryFile (input, output)
                |> AsyncResult.mapError CommandError.ofEatonApiError

            output.Success "Done"

            return ExitCode.Success
        }
