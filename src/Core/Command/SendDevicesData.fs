namespace MF.HomeConsole

[<RequireQualifiedAccess>]
module SendDevicesDataCommand =
    open System
    open System.Collections.Concurrent
    open System.IO
    open FSharp.Data
    open MF.ConsoleApplication
    open MF.ErrorHandling
    open MF.HomeConsole.Console
    open MF.Storage

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

                let! (tabs: TabName list) =
                    GoogleSheets.getTabs config.GoogleSheets
                    |> AsyncResult.mapError CommandError.Exception

                let existingTabs = tabs |> Set.ofList

                do!
                    [
                        TabName "Test"
                        TabName "Test2"
                    ]
                    |> List.filter (existingTabs.Contains >> not)
                    |> List.map (fun tabName ->
                        tabName
                        |> GoogleSheets.createTab config.GoogleSheets
                        |> AsyncResult.mapError CommandError.Exception
                    )
                    |> AsyncResult.ofSequentialAsyncResults CommandError.Exception
                    |> AsyncResult.ignore
                    |> AsyncResult.mapError CommandError.Errors
            }
            |> Async.RunSynchronously

        match result with
        | Error e ->
            output.Error <| sprintf "%A" e
            ExitCode.Error
        | Ok _ ->
            output.Success "Done"
            ExitCode.Success
