namespace MF.HomeConsole.WebServer

open MF.Eaton

[<AutoOpen>]
module Utils =
    open System
    open System.Net
    open System.Xml.Serialization

    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Cors
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Http
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.Extensions.Logging

    open MF.Utils

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

    [<RequireQualifiedAccess>]
    module HttpContext =
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

    [<RequireQualifiedAccess>]
    module Debug =
        let logCtx clientIp isHassioIngressRequest (output: MF.ConsoleApplication.Output) (ctx: HttpContext) =
            if output.IsDebug() then
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
