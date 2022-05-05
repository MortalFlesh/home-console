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

    type SendDevicesDataError =
        | Message of string
        | Exception of exn
        | Errors of SendDevicesDataError list

    let execute (input, output) =
        output.SubTitle "Starting ..."

        let result =
            asyncResult {
                let! config =
                    input
                    |> Input.config
                    |> Config.parse
                    |> Result.ofOption (Message "invalid config")

                let! (tabs: TabName list) =
                    GoogleSheets.getTabs config.GoogleSheets
                    |> AsyncResult.mapError Exception

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
                        |> AsyncResult.mapError Exception
                    )
                    |> AsyncResult.ofSequentialAsyncResults Exception
                    |> AsyncResult.ignore
                    |> AsyncResult.mapError Errors
            }
            |> Async.RunSynchronously

        match result with
        | Error e ->
            output.Error <| sprintf "%A" e
            ExitCode.Error
        | Ok _ ->
            output.Success "Done"
            ExitCode.Success
