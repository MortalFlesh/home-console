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

    let execute (input, output) =
        output.SubTitle "Starting ..."

        let result: Result<_, CommandError> =
            asyncResult {
                let! config =
                    input
                    |> Input.config
                    |> Config.parse
                    |> Result.ofOption (CommandError.Message "invalid config")

                let! historyFilePath =
                    config.Eaton
                    |> Api.downloadHistoryFile (input, output)
                    |> AsyncResult.mapError CommandError.ofEatonApiError

                return "Done"
            }
            |> Async.RunSynchronously

        match result with
        | Error e ->
            output.Error <| sprintf "%A" e
            ExitCode.Error
        | Ok _ ->
            output.Success "Done"
            ExitCode.Success
