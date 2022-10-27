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

    [<RequireQualifiedAccess>]
    module private Debug =
        open Microsoft.AspNetCore.Http

        let logCtx clientIp isHassioIngressRequest (output: MF.ConsoleApplication.Output) (ctx: HttpContext) =
            let path = (try ctx.Request.Path.Value |> string with _ -> "-")

            if path = "/favicon.ico" then ()
            else
            let tupleRows values =
                values
                |> Seq.map (fun (key, value) -> [ ""; key; value ])

            let separator = [ "<c:gray>---</c>"; "<c:gray>---</c>" ]

            let clientIp =
                try
                    ctx
                    |> clientIp
                    |> Option.map (sprintf "<c:cyan>%A</c>")
                    |> Option.defaultValue "-"
                with e ->
                    e.Message
                    |> sprintf "<c:red>Err: %s</c>"

            let th = sprintf "<c:dark-yellow>%s</c>"

            output.Table [ "Http Context"; "Value" ] [
                [ th "ContentType"; (try ctx.Request.ContentType |> string with _ -> "-") ]
                [ th "Host"; (try ctx.Request.Host.Value |> string with _ -> "-") ]
                [ th "Method"; (try ctx.Request.Method |> string with _ -> "-") ]
                [ th "Path"; path ]
                [ th "PathBase"; (try ctx.Request.PathBase.Value |> string with _ -> "-") ]
                [ th "QueryString"; (try ctx.Request.QueryString.Value |> string with _ -> "-") ]

                separator

                [ th "ClientIP"; clientIp ]
                [ th "Is Hassio"; (if ctx |> isHassioIngressRequest then "<c:green>yes</c>" else "<c:red>no</c>") ]
            ]

            tupleRows (ctx.Request.Headers |> Seq.map (fun h -> h.Key, h.Value |> String.concat ", "))
            |> List.ofSeq
            |> output.GroupedOptions "-" "Headers"

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

        let private notFound output =
            (fun path ->
                warbler (fun (next, ctx) ->
                    ctx |> Debug.logCtx clientIP isHassioIngressRequest output

                    json {| Error = "Path not found"; Path = path |}
                )
            )

        let app (loggerFactory: ILoggerFactory) output httpHandlers = application {
            url "http://0.0.0.0:8080/"
            use_router (choose [
                yield! httpHandlers

                routef "/%s"
                    (notFound output)
                    >=> setStatusCode 404

                routef "/%s/%s"
                    (notFound output)
                    >=> setStatusCode 404

                routef "/%s/%s/%s"
                    (notFound output)
                    >=> setStatusCode 404

                routef "/%s/%s/%s/%s"
                    (notFound output)
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



    let execute = executeResult <| fun (input, output) ->
        result {
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

            let mutable devicesCache: (Device list) option = None
            let debuCtx ctx =
                ctx |> Debug.logCtx WebServer.clientIP WebServer.isHassioIngressRequest output

            [
                GET >=>
                    choose [
                        // https://developers.home-assistant.io/docs/api/supervisor/endpoints/#addons
                        route "/"
                            //>=> authorizeRequest WebServer.isHassioIngressRequest WebServer.accessDeniedJson
                            //>=> htmlString WebServer.index
                            >=> warbler (fun (next, ctx) ->
                                ctx |> debuCtx

                                text "OK"
                            )

                        route "/sensors"
                            >=> warbler (fun (next, ctx) ->
                                ctx |> debuCtx

                                let data =
                                    asyncResult {
                                        let! (devices: Device list) =
                                            match devicesCache with
                                            | Some cache -> AsyncResult.ofSuccess cache
                                            | _ ->
                                                Api.getDeviceList (input, output) config.Eaton
                                                |> AsyncResult.tee (function
                                                    | [] -> ()
                                                    | devices -> devicesCache <- Some devices
                                                )

                                        let! (devicesStats: Map<DeviceId,DeviceStat>) =
                                            Api.getDeviceStatuses (input, output) config.Eaton

                                        let devicesToShow =
                                            devices
                                            |> List.filter (fun d -> d.SerialNumber = 4131920 || d.SerialNumber = 8649687)

                                        let stat deviceId = devicesStats |> Map.tryFind deviceId

                                        return {|
                                            Sensors =
                                                devicesToShow
                                                |> List.collect (fun device ->
                                                    device.Children
                                                    |> List.map (fun device ->
                                                        device.DeviceId |> DeviceId.id,
                                                        Map.ofList [
                                                            "name", device.Name

                                                            match stat device.DeviceId with
                                                            | Some value ->
                                                                if device.DeviceId |> DeviceId.contains "_u1"
                                                                    then "humidity", value.EventLog |> DeviceStat.value
                                                                elif device.DeviceId |> DeviceId.contains "_u0"
                                                                    then "temperature", value.EventLog |> DeviceStat.value
                                                                elif device.DeviceId |> DeviceId.contains "_w"
                                                                    then "adjustment", value.EventLog |> DeviceStat.value
                                                                else
                                                                    "value", value.EventLog |> DeviceStat.value

                                                                "last_update", value.LastMsgTimeStamp.ToString()
                                                            | _ -> ()
                                                        ]
                                                    )
                                                )
                                                |> Map.ofList
                                        |}
                                    }
                                    |> Async.RunSynchronously

                                match data with
                                | Ok success -> json success
                                | Error error -> json error
                            )
                ]
            ]
            |> WebServer.app loggerFactory output
            |> Application.run

            output.Success "Done"

            return ExitCode.Success
        }
