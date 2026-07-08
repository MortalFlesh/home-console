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

    open System

    open Feather.ConsoleApplication
    open MF.Utils
    open Feather.ErrorHandling
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

    let mutable settingsCache: Settings = Settings.empty

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

        let effectiveDevices = Settings.applyToDevices settingsCache devices

        return View.Html.index {
            CurrentHost = $"{host}:{port}"
            Devices = effectiveDevices
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

                                "temperature", heating.CurrentTemperature |> sprintf "%.1f"
                                "power_percentage", heating.PowerPercentage |> float |> sprintf "%.1f"
                                "power", heating.Power |> sprintf "%.1f"
                                "overload", heating.Overload.ToString()

                                "last_update", lastUpdate.ToString("o")
                            | _ ->
                            match stat device.DeviceId with
                            | Some value ->
                                device.Type |> DeviceType.valueType, value.EventLog |> DeviceStat.value
                                "last_update", (DateTimeOffset.Now - value.LastMsgTimeStamp).ToString("o")
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

    let private brightnessStats: Action<_> = fun (input, output) config _ -> asyncResult {
        let! (devices: Device list) = loadDevices (input, output) config
        let lastUpdated, values = Api.DeviceStates.allValueStates ()

        // the value cache is keyed by the state-response id (xCo:... form); map it back to the
        // device-list id (hdm:... form) so /brightness keys match /sensors and the light template
        let idMap =
            devices
            |> List.collect (fun device -> device.Children)
            |> List.map (fun device -> DeviceId (device.DeviceId |> DeviceId.shortId |> ShortDeviceId.value), device.DeviceId |> DeviceId.id)
            |> Map.ofList

        return {|
            Updated = lastUpdated
            Brightness =
                values
                |> List.choose (fun (_, device, v) -> idMap |> Map.tryFind device |> Option.map (fun haId -> haId, v))
                |> Map.ofList
        |}
    }

    let private getDeviceBrightness (zone, device): Action<_> = fun _ _ _ -> asyncResult {
        let brightness =
            (ZoneId zone, DeviceId device)
            |> Api.DeviceStates.loadValueState
            |> snd
            |> Option.defaultValue 0

        return {| Brightness = brightness |}
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

    let private getClimateDashboard (zone: string) : Action<_> = fun (input, output) config _ -> asyncResult {
        let! (dashboard: Api.ClimateFunction.ClimateDashboard) = Api.ClimateFunction.getDashboard (input, output) config.Eaton (ZoneId zone)

        return {|
            Temperature = dashboard.Current
            Setpoint = dashboard.Target
            Mode = dashboard.Mode
            TypeId = dashboard.TypeId
        |}
    }

    let private setClimateSetpoint: Action<_> = fun (input, output) config ctx -> asyncResult {
        let! (request: Api.ClimateSetpoint) =
            ctx
            |> Api.ClimateSetpoint.parse
            |> AsyncResult.mapError ApiError.Message

        do! Api.ClimateFunction.setSetpoint (input, output) config.Eaton request.Room request.Temperature

        return {| Status = "Ok" |}
    }

    let private version = Version.value

    let private health: Action<_> = fun _ _ _ -> asyncResult {
        let status = if zonesCache.IsSome then "ok" else "degraded"
        return {|
            Status = status
            LastUpdated = Api.DeviceStates.lastUpdated
            Version = version
        |}
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

    let private getConfig: Action<_> = fun (input, output) config _ctx -> asyncResult {
        let! (devices: Device list) = loadDevices (input, output) config
        return View.Html.configPage {
            Devices = devices
            Settings = settingsCache
        }
    }

    let private postConfig: Action<_> = fun _ config ctx -> asyncResult {
        use reader = new System.IO.StreamReader(ctx.Request.Body)
        let! body =
            reader.ReadToEndAsync()
            |> AsyncResult.ofTaskCatch ApiError.Exception

        let! settings =
            body
            |> Settings.tryParse
            |> Result.mapError ApiError.Message

        settingsCache <- settings
        let settingsPath = System.IO.Path.Combine(config.Data.Directory, "settings.json")
        do!
            Settings.serialize settings
            |> Store.saveText settingsPath
            |> Async.map (Result.mapError ApiError.Exception)

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

    let private startWithRetry (input, output) config = async {
        let backoffMs = [| 5_000; 10_000; 20_000; 40_000; 60_000 |]
        let mutable attempt = 0
        let mutable loaded = false
        while not loaded do
            match! loadZones (input, output) config with
            | Ok zones ->
                let existingZoneIds =
                    settingsCache.Zones
                    |> List.choose (fun z -> if z.DisplayName.IsSome then Some z.ZoneId else None)
                    |> Set.ofList

                let newZones =
                    zones
                    |> List.choose (fun zone ->
                        let zoneId = zone.Id |> ZoneId.value
                        if existingZoneIds |> Set.contains zoneId then None
                        else Some { ZoneId = zoneId; DisplayName = Some zone.Name }
                    )

                if newZones |> List.isEmpty |> not then
                    let merged =
                        let toAdd = newZones |> List.map (fun z -> z.ZoneId, z) |> Map.ofList
                        let updated = settingsCache.Zones |> List.map (fun z -> { z with DisplayName = z.DisplayName |> Option.orElse (toAdd |> Map.tryFind z.ZoneId |> Option.bind (fun s -> s.DisplayName)) })
                        let existingIds = settingsCache.Zones |> List.map (fun z -> z.ZoneId) |> Set.ofList
                        let added = newZones |> List.filter (fun z -> existingIds |> Set.contains z.ZoneId |> not)
                        updated @ added

                    settingsCache <- { settingsCache with Zones = merged }

                    let settingsPath = System.IO.Path.Combine(config.Data.Directory, "settings.json")
                    match! Settings.serialize settingsCache |> Store.saveText settingsPath with
                    | Ok () -> output.Message "[Settings] Pre-seeded zone names from Eaton"
                    | Error _ -> ()

                zones
                |> List.map (fun zone -> zone.Id)
                |> Api.DeviceStates.startLoadingState (input, output) config.Eaton
                |> Async.Start
                loaded <- true
            | Error err ->
                let delay = backoffMs[min attempt (backoffMs.Length - 1)]
                output.Warning(sprintf "Eaton not reachable (attempt %d), retrying in %d s: %A" (attempt + 1) (delay / 1000) err)
                do! Async.Sleep delay
                attempt <- attempt + 1
    }

    let run loggerFactory (input, output) (config: Config) port: AsyncResult<unit, ApiError> = asyncResult {
        let handleJsonAction action = handleJsonAction (input, output) config action
        let handleHtmlAction action = handleHtmlAction (input, output) config action

        let settingsPath = System.IO.Path.Combine(config.Data.Directory, "settings.json")
        settingsCache <-
            Store.tryLoadText settingsPath
            |> Option.map Settings.parse
            |> Option.defaultValue Settings.empty

        startWithRetry (input, output) config |> Async.Start

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

                    route "/brightness"
                        >=> handleJsonAction brightnessStats

                    routef "/brightness/%s/%s"
                        (getDeviceBrightness >> handleJsonAction)

                    routef "/climate/%s"
                        (getClimateDashboard >> handleJsonAction)

                    route "/health"
                        >=> handleJsonAction health

                    route "/config"
                        >=> handleHtmlAction getConfig
                ]

                POST >=> choose [
                    route "/state"
                        >=> handleJsonAction changeDeviceState

                    route "/triggerScene"
                        >=> handleJsonAction triggerScene

                    route "/triggerMacro"
                        >=> handleJsonAction triggerMacro

                    route "/climate"
                        >=> handleJsonAction setClimateSetpoint

                    route "/config"
                        >=> handleJsonAction postConfig
                ]
            ]
            |> app loggerFactory output port
            |> Application.run
    }
