namespace MF.HomeConsole

[<RequireQualifiedAccess>]
module DownloadEatonDeviceList =
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

                let! devices =
                    config.Eaton
                    |> Api.getDeviceList (input, output)
                    |> AsyncResult.mapError CommandError.ofEatonApiError

                let! stats =
                    config.Eaton
                    |> Api.getDeviceStatuses (input, output)
                    |> AsyncResult.mapError CommandError.ofEatonApiError

                output.Section "Eaton devices (with current stats)"

                let deviceRow device = [
                    let error = "<c:red>err</c>"

                    if device.Children.Length > 0
                        then yield device.Name
                        else yield $" - {device.Name}"
                    yield device.DisplayName
                    yield device.SerialNumber |> sprintf "%A"
                    yield device.Type |> sprintf "%A"
                    yield device.Rssi |> sprintf "%A"
                    yield device.Powered |> sprintf "%A"
                    yield device.Children |> List.length |> sprintf "%A"

                    match stats |> Map.tryFind device.DeviceId with
                    | Some (stats: DeviceStat) ->
                        yield try stats.EventLog |> sprintf "<c:magenta>%A</c>" with _ -> error
                        yield try stats.LastMsgTimeStamp.ToString() |> sprintf "<c:cyan>%s</c>" with _ -> error
                    | _ -> yield! ["-"; "-"]
                ]

                devices
                |> List.collect (fun device ->
                    (device |> deviceRow)
                    :: (device.Children |> List.map deviceRow)
                )
                |> output.Table [
                    "Name"
                    "DisplayName"
                    "SerialNumber"
                    "Type"
                    "Rssi"
                    "Powered"
                    "Children"
                    "Value"
                    "Last Updated"
                ]
                |> output.NewLine

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
