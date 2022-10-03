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

    let arguments = []
    let options = []

    [<RequireQualifiedAccess>]
    module WebServer =
        open System
        open System.Net
        open System.Xml.Serialization

        open Microsoft.AspNetCore.Builder
        open Microsoft.AspNetCore.Cors
        open Microsoft.AspNetCore.Hosting
        open Microsoft.AspNetCore.Http
        open Microsoft.Extensions.DependencyInjection
        open Microsoft.Extensions.Logging

        let app (loggerFactory: ILoggerFactory) httpHandlers = application {
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

        [<RequireQualifiedAccess>]
        module Header =
            let (|RequestHeader|_|) header (httpContext: HttpContext) =
                match httpContext.Request.Headers.TryGetValue(header) with
                | true, headerValues -> Some (headerValues |> Seq.toList)
                | _ -> None

        type ClientIpAddress = ClientIpAddress of IPAddress

        [<RequireQualifiedAccess>]
        module ClientIpAddress =
            let parse = function
                | null | "" -> None
                | ip ->
                    match ip |> IPAddress.TryParse with
                    | true, ip -> Some (ClientIpAddress ip)
                    | _ -> None

            /// Parse IP of the original client from the value of X-Forwarded-For header.
            ///
            /// The value could consist of more comma-separated IPs, the left-most being the original client,
            /// and each successive proxy that passed the request adding the IP address where it received the request from.
            ///
            /// @see https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/X-Forwarded-For#syntax
            let parseClientIpFromXForwardedFor = function
                | null | "" -> None
                | xForwardedFor -> xForwardedFor.Split ',' |> Array.map String.trim |> Array.tryHead |> Option.bind parse

            /// Parse IP of the original client from the value of X-Forwarded-For header.
            ///
            /// The value could consist of more comma-separated IPs, the left-most being the original client,
            /// and each successive proxy that passed the request adding the IP address where it received the request from.
            ///
            /// @see https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/X-Forwarded-For#syntax
            let (|HttpXForwardedFor|_|) = function
                | Header.RequestHeader "X-Forwarded-For" xForwardedFor -> xForwardedFor |> List.tryPick parseClientIpFromXForwardedFor
                | _ -> None

            let (|RemoteIpAddress|_|) (httpContext: HttpContext) =
                match httpContext.Request.HttpContext.Connection.RemoteIpAddress with
                | null -> None
                | remoteIp -> Some (ClientIpAddress remoteIp)

        [<RequireQualifiedAccess>]
        module IPAddress =
            /// https://developers.home-assistant.io/docs/add-ons/presentation/#ingress
            let HassioIngressIp = IPAddress.Parse "172.30.32.2"

            let isInternal (ip: IPAddress) =
                match ip.GetAddressBytes() |> Array.map int with
                | [| 127; _; _; _ |]

                // RFC1918
                | [| 10; _; _; _ |]
                | [| 192; 168; _; _ |] -> true
                | [| 172; i; _; _ |] when i >= 16 && i <= 31 -> true

                // RFC3927
                | [| 169; 254; _; _ |] -> true

                | _ -> false

        let clientIP = function
            // When request goes via loadbalancer, original client IP is stored in HTTP_X_FORWARDED_FOR
            | ClientIpAddress.HttpXForwardedFor xForwardedFor -> Some xForwardedFor

            // Docker / vagrant requests goes directly, ie. HTTP_X_FORWARDED_FOR is not set and client IP is in REMOTE_ADDR
            | ClientIpAddress.RemoteIpAddress remoteIp -> Some remoteIp

            // This is just a fallback, one of above methods should always return an IP
            | _ -> None

        /// Check if request comes from internal "safe" network
        let isHassioIngressRequest = function
            // When request goes via loadbalancer, original client IP is stored in HTTP_X_FORWARDED_FOR
            | ClientIpAddress.HttpXForwardedFor (ClientIpAddress xForwardedFor) ->
                (xForwardedFor = IPAddress.HassioIngressIp)

            // Docker / vagrant requests goes directly, ie. HTTP_X_FORWARDED_FOR is not set and client IP is in REMOTE_ADDR
            | ClientIpAddress.RemoteIpAddress (ClientIpAddress remoteIp) ->
                (remoteIp = IPAddress.HassioIngressIp)

            | _ -> false

        let accessDeniedJson: HttpHandler =
            setStatusCode 403
            >=> json {| Title = "Forbidden"; Status = 403; Detail = "Access denied." |}

        let index =
            """<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="X-UA-Compatible" content="IE=edge">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>EATON Addon</title>
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@4.3.1/dist/css/bootstrap.min.css" integrity="sha384-ggOyR0iXCbMQv3Xipma34MD+dH/1fQ784/j6cY/iJTQUOhcWr7x9JvoRxT2MZw1T" crossorigin="anonymous">

</head>
<body>
    <div class="container">
        <div class="row">
            <div class="col-md-12">
                <h1>Eaton Addon</h1>
            </div>
        </div>
        <div class="row">
            <div class="col-md-12">
                <h2>Add following to your configuration.yaml</h2>
                <pre>
rest:
  - scan_interval: 60
    resource: http://192.168.1.5:28080/sensors
    sensor:
      - name: "Eaton"
        json_attributes_path: "$.response.system"
        value_template: "OK"
        json_attributes:
          - "runstate"
          - "model"
          - "opmode"
          - "freeze"
          - "time"
          - "sensor1"
          - "sensor2"
          - "sensor3"
          - "sensor4"
          - "sensor5"
          - "version"
</pre>
            </div>
        </div>
    </div>
</body>
</html>"""

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
                            route "/"
                                >=> authorizeRequest WebServer.isHassioIngressRequest WebServer.accessDeniedJson
                                >=> htmlString WebServer.index

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
                |> WebServer.app loggerFactory
                |> Application.run
            }

        match result with
        | Error e ->
            output.Error <| sprintf "%A" e
            ExitCode.Error
        | Ok _ ->
            output.Success "Done"
            ExitCode.Success
