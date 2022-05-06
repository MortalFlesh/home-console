namespace MF.HomeConsole

[<RequireQualifiedAccess>]
module DownloadEatonConfiguration =
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

                return!
                    config.Eaton
                    |> Api.run
                    |> AsyncResult.mapError CommandError.ofEatonApiError
            }
            |> Async.RunSynchronously

        match result with
        | Error e ->
            output.Error <| sprintf "%A" e
            ExitCode.Error
        | Ok _ ->
            output.Success "Done"
            ExitCode.Success
