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
    open MF.Utils.Logging
    open MF.HomeConsole.Console

    let arguments = []
    let options = []

    let private webServer (loggerFactory: ILoggerFactory) httpHandlers = application {
            url "http://0.0.0.0:8080/"
            use_router (choose [
                yield! httpHandlers

                routef "/%s"
                    (fun path -> json {| Error = "Path not found"; Path = path |})
                    >=> setStatusCode 404
            ])
            memory_cache
            use_gzip

            service_config (fun services ->
                services
                    .AddSingleton(loggerFactory)
                    .AddLogging()
                    .AddGiraffe()
            )
        }

    let execute (input, output) =
        output.SubTitle "Starting ..."

        let result: Result<_, CommandError> =
            result {
                (* let! config =
                    input
                    |> Input.config
                    |> Config.parse
                    |> Result.ofOption (CommandError.Message "invalid config") *)

                use loggerFactory =
                    "debug"
                    |> LogLevel.parse
                    |> LoggerFactory.create

                output.Section "Run webserver"

                [
                    GET >=>
                        choose [
                            route "/sensors"
                                >=> json {|
                                    Sensor = {|
                                        Temperature = 42
                                        Id = "sensor01"
                                        Name = "livingroom"
                                        Connected = true
                                    |}
                                    Sensors = Map.ofList [
                                        "bedroom1", {|
                                            Temperature = 15.79
                                            Humidity = 55.78
                                            Battery = 5.26
                                            Timestamp = "2019-02-27T22:21:37Z"
                                        |}
                                        "bedroom2", {|
                                            Temperature = 18.99
                                            Humidity = 49.81
                                            Battery = 5.08
                                            Timestamp = "2019-02-27T22:23:44Z"
                                        |}
                                        "bedroom3", {|
                                            Temperature = 18.58
                                            Humidity = 47.95
                                            Battery = 5.15
                                            Timestamp = "2019-02-27T22:21:22Z"
                                        |}
                                    ]
                                    Time = {|
                                        Date = "10-03-2022"
                                        Milliseconds_since_epoch = int64 "1664802991672"
                                        Time = "01:16:31 PM"
                                    |}
                                |}
                    ]
                ]
                |> webServer loggerFactory
                |> Application.run
            }

        match result with
        | Error e ->
            output.Error <| sprintf "%A" e
            ExitCode.Error
        | Ok _ ->
            output.Success "Done"
            ExitCode.Success
