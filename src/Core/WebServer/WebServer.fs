namespace MF.HomeConsole.WebServer

[<RequireQualifiedAccess>]
module WebServer =
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Cors
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Http
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.Extensions.Logging

    open Giraffe
    open Saturn

    open MF.ConsoleApplication
    open MF.Utils
    open MF.ErrorHandling
    open MF.HomeConsole
    open MF.Eaton

    type private Action<'Response> = IO -> Config -> HttpContext -> AsyncResult<'Response, ApiError>

    let private app (loggerFactory: ILoggerFactory) output port httpHandlers = application {
        url (sprintf "http://0.0.0.0:%d/" port)

        use_router (choose [
            yield! httpHandlers

            routef "/%s"
                (View.notFound output)

            routef "/%s/%s"
                (View.notFound output)

            routef "/%s/%s/%s"
                (View.notFound output)

            routef "/%s/%s/%s/%s"
                (View.notFound output)
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

    let mutable zonesCache: (Zone list) option = None
    let private loadZones (input, output) config = asyncResult {
        return!
            match zonesCache with
            | Some cache -> AsyncResult.ofSuccess cache
            | _ ->
                Api.getZoneList (input, output) config.Eaton
                |> AsyncResult.tee (function
                    | [] -> ()
                    | zones -> zonesCache <- Some zones
                )
    }

    let mutable devicesCache: (Device list) option = None
    let private loadDevices (input, output) config = asyncResult {
        return!
            match devicesCache with
            | Some cache -> AsyncResult.ofSuccess cache
            | _ -> asyncResult {
                let! zones = loadZones (input, output) config

                return!
                    zones
                    |> Api.getDeviceList (input, output) config.Eaton
                    |> AsyncResult.tee (function
                        | [] -> ()
                        | devices -> devicesCache <- Some devices
                    )
            }
    }

    let mutable scenesCache: (Scene list) option = None
    let private loadScenes (input, output) config = asyncResult {
        return!
            match scenesCache with
            | Some cache -> AsyncResult.ofSuccess cache
            | _ -> asyncResult {
                let! zones = loadZones (input, output) config

                return!
                    zones
                    |> Api.getSceneList (input, output) config.Eaton
                    |> AsyncResult.tee (function
                        | [] -> ()
                        | scenes -> scenesCache <- Some scenes
                    )
            }
    }

    let private index port: Action<_> = fun (input, output) config ctx -> asyncResult {
        // todo - allow to change this by query parameter
        let showAll = false

        let! (devices: Device list) = loadDevices (input, output) config
        let! (scenes: Scene list) = loadScenes (input, output) config
        let host = ctx.Request.Host.Host

        return View.Html.index {
            CurrentHost = $"{host}:{port}"
            Devices = devices
            Scenes = scenes
        }
    }

    let private sensorsStats: Action<_> = fun (input, output) config _ctx -> asyncResult {
        let! (devices: Device list) = loadDevices (input, output) config

        let! (devicesStats: Map<DeviceId,DeviceStat>) =
            Api.getDeviceStatuses (input, output) config.Eaton

        let sensors =
            devices
            |> List.filter Device.isSensor

        let stat deviceId = devicesStats |> Map.tryFind deviceId
        let heatingStat = DeviceId.shortId >> Api.DeviceStates.loadHeatingState

        return {|
            Sensors =
                sensors
                |> List.collect (fun device ->
                    device.Children
                    |> List.map (fun device ->
                        device.DeviceId |> DeviceId.id,
                        Map.ofList [
                            "name", device.Name

                            match heatingStat device.DeviceId with
                            | lastUpdate, Some heating ->
                                "heating", heating.ToString()

                                "temperature", string heating.CurrentTemperature
                                "power_percentage", string heating.PowerPercentage
                                "power", string heating.Power
                                "overload", heating.Overload.ToString()

                                "last_update", lastUpdate.ToString("HH:mm:ss")
                            | _ ->
                            match stat device.DeviceId with
                            | Some value ->
                                device.Type |> DeviceType.valueType, value.EventLog |> DeviceStat.value
                                "last_update", value.LastMsgTimeStamp.ToString()
                            | _ -> ()
                        ]
                    )
                )
                |> Map.ofList
        |}
    }

    let private getDeviceState (zone, device): Action<_> = fun (input, output) config ctx -> asyncResult {
        let state =
            (ZoneId zone, DeviceId device)
            |> Api.DeviceStates.loadIsOnState
            |> snd
            |> Option.defaultValue false

        return {| State = state |}
    }

    let private getAllDevicesStates: Action<_> = fun (input, output) config ctx -> asyncResult {
        let lastUpdated, states = Api.DeviceStates.allIsOnStates ()

        return {|
            Updated = lastUpdated
            States =
                states
                |> List.groupBy (fun (zone, _, _) -> zone)
                |> List.map (fun (ZoneId zone, states) -> zone, states |> List.map (fun (_, DeviceId device, state) -> device, state) |> Map.ofList)
                |> Map.ofList
        |}
    }

    let private changeDeviceState: Action<_> = fun (input, output) config ctx -> asyncResult {
        let! request =
            ctx
            |> Api.ChangeDeviceState.parse
            |> AsyncResult.mapError ApiError.Message

        do!
            request
            |> Api.changeDeviceState (input, output) config.Eaton

        return {| Status = "Ok" |}
    }

    let private triggerScene: Action<_> = fun (input, output) config ctx -> asyncResult {
        let! request =
            ctx
            |> Api.TriggerSceneOrMacro.parse
            |> AsyncResult.mapError ApiError.Message

        do!
            request
            |> Api.triggerScene (input, output) config.Eaton

        return {| Status = "Ok" |}
    }

    let private triggerMacro: Action<_> = fun (input, output) config ctx -> asyncResult {
        let! request =
            ctx
            |> Api.TriggerSceneOrMacro.parse
            |> AsyncResult.mapError ApiError.Message

        do!
            request
            |> Api.triggerMacro (input, output) config.Eaton

        return {| Status = "Ok" |}
    }

    let private handleJsonAction (input, output) config (action: Action<_>) next ctx = task {
        ctx |> Debug.logCtx HttpContext.clientIP HttpContext.isHassioIngressRequest output

        match! ctx |> action (input, output) config with
        | Ok success -> return! json success next ctx
        | Error error -> return! (json error >=> setStatusCode 400) next ctx
    }

    let private handleHtmlAction (input, output) config (action: Action<_>) next ctx = task {
        ctx |> Debug.logCtx HttpContext.clientIP HttpContext.isHassioIngressRequest output

        match! ctx |> action (input, output) config with
        | Ok success -> return! htmlString (success |> ViewEngine.RenderView.AsString.htmlDocument) next ctx
        | Error error -> return! (json error >=> setStatusCode 400) next ctx
    }

    let run loggerFactory (input, output) (config: Config) port: AsyncResult<unit, ApiError> = asyncResult {
        let handleJsonAction action = handleJsonAction (input, output) config action
        let handleHtmlAction action = handleHtmlAction (input, output) config action

        let! (zones: Zone list) = loadZones (input, output) config

        zones
        |> List.map (fun zone -> zone.Id)
        |> Api.DeviceStates.startLoadingState (input, output) config.Eaton
        |> Async.Start

        return
            [
                GET >=> choose [
                    // ingress: https://github.com/sabeechen/hassio-google-drive-backup/blob/1451592e93209c844b0e871602374a0277bf07c8/hassio-google-drive-backup/dev/apiingress.py

                    // https://developers.home-assistant.io/docs/api/supervisor/endpoints/#addons
                    route "/"
                        //>=> authorizeRequest WebServer.isHassioIngressRequest WebServer.accessDeniedJson
                        >=> handleHtmlAction (index port)

                    route "/sensors"
                        >=> handleJsonAction sensorsStats

                    routef "/state/%s/%s"
                        (getDeviceState >> handleJsonAction)

                    route "/states"
                        >=> handleJsonAction getAllDevicesStates
                ]

                POST >=> choose [
                    route "/state"
                        >=> handleJsonAction changeDeviceState

                    route "/triggerScene"
                        >=> handleJsonAction triggerScene

                    route "/triggerMacro"
                        >=> handleJsonAction triggerMacro
                ]
            ]
            |> app loggerFactory output port
            |> Application.run
    }
