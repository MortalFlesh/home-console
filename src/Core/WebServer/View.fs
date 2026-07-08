namespace MF.HomeConsole.WebServer

open MF.Eaton

[<RequireQualifiedAccess>]
module View =
    open System
    open System.Net
    open System.Xml.Serialization

    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Cors
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Http
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.Extensions.Logging

    open Giraffe

    let notFound output = fun path ->
        warbler (fun (next, ctx) ->
            ctx |> Debug.logCtx HttpContext.clientIP HttpContext.isHassioIngressRequest output

            json {| Error = "Path not found"; Path = path |}
        )
        >=> setStatusCode 404

    let accessDeniedJson: HttpHandler =
        setStatusCode 403
        >=> json {| Title = "Forbidden"; Status = 403; Detail = "Access denied." |}

    module Html =
        open Giraffe.ViewEngine
        open MF.HomeConsole
        open MF.Utils

        let private htmlYaml lines =
            pre [ _class "line-numbers language-yaml bg-gray-100 dark:bg-gray-800 p-4 rounded overflow-x-auto text-sm" ] [
                code [ _class "language-yaml" ] [
                    yield! lines |> List.map (sprintf "%s\n" >> str)
                ]
            ]

        let htmlHead =
            head [] [
                meta [ _charset "utf-8" ]
                meta [ _httpEquiv "X-UA-Compatible"; _content "IE=edge" ]
                meta [ _name "viewport"; _content "width=device-width, initial-scale=1.0" ]
                title [] [ str "EATON Addon" ]
                script [ _src "https://cdn.tailwindcss.com" ] []
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/prismjs@1/themes/prism.min.css"; _media "not all" ]
                link [ _id "prism-dark-theme"; _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/prismjs@1/themes/prism-okaidia.min.css"; _media "not all" ]
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/prismjs@1/plugins/line-numbers/prism-line-numbers.min.css" ]
                script [] [
                    rawText """
tailwind.config = { darkMode: 'class' };
(function() {
    var stored = localStorage.getItem('theme');
    var isDark = stored === 'dark' || (!stored && window.matchMedia('(prefers-color-scheme: dark)').matches);
    if (isDark) {
        document.documentElement.classList.add('dark');
    }
    function applyPrismTheme(dark) {
        var light = document.querySelector('link[href*="prism.min.css"]');
        var darkTheme = document.getElementById('prism-dark-theme');
        if (light) light.media = dark ? 'not all' : 'all';
        if (darkTheme) darkTheme.media = dark ? 'all' : 'not all';
    }
    applyPrismTheme(isDark);
    window.__applyPrismTheme = applyPrismTheme;
})();
"""
                ]
                script [ _src "https://cdn.jsdelivr.net/npm/prismjs@1/components/prism-core.min.js" ] []
                script [ _src "https://cdn.jsdelivr.net/npm/prismjs@1/plugins/autoloader/prism-autoloader.min.js" ] []
                script [ _src "https://cdn.jsdelivr.net/npm/prismjs@1/plugins/line-numbers/prism-line-numbers.min.js" ] []
            ]

        let private sensorsRow currentHost (sensors: Device list) =
            div [ _id "sensors"; _class "mb-8" ] [
                h3 [ _class "text-xl font-semibold mb-2 text-gray-800 dark:text-gray-100" ] [ str "Sensors" ]
                [
                    "sensor:"
                    "  - platform: rest"
                    $"    resource: http://{currentHost}/sensors"
                    "    scan_interval: 60"
                    "    name: eaton"
                    "    value_template: OK"
                    "    json_attributes_path: \"$.sensors\""
                    "    json_attributes:   # NOTE: Add only sensors you want to use"

                    yield!
                        sensors
                        |> List.collect (fun sensor ->
                            sensor.Children
                            |> List.map (fun sensor ->
                                sprintf "      - %s" (sensor.DeviceId |> DeviceId.id)
                            )
                        )

                    yield! HaYaml.templateSensorLines sensors
                ]
                |> htmlYaml
            ]

        type private StateChange = {
            On: string
            Off: string
        }

        let private coversRow currentHost (covers: Device list) =
            div [ _id "covers"; _class "mb-8" ] [
                h3 [ _class "text-xl font-semibold mb-2 text-gray-800 dark:text-gray-100" ] [ str "Covers (shutters, blinds, awnings)" ]
                p [ _class "mb-2 text-gray-600 dark:text-gray-400" ] [ str "Add rest_command entries to your rest_command: section. Merge the template: block below with any other template: blocks (sensors, lights) into a single top-level template: list." ]
                HaYaml.coverLines currentHost covers
                |> htmlYaml
            ]

        let private lightsRow currentHost (dimmers: Device list) =
            div [ _id "lights"; _class "mb-8" ] [
                h3 [ _class "text-xl font-semibold mb-2 text-gray-800 dark:text-gray-100" ] [ str "Lights (dimmers)" ]
                p [ _class "mb-2 text-gray-600 dark:text-gray-400" ] [ str "Add the sensor: block, then rest_command entries to your rest_command: section. Merge the template: block below with any other template: blocks (sensors, covers) into a single top-level template: list." ]
                HaYaml.lightLines currentHost dimmers
                |> htmlYaml
            ]

        let private climateRow currentHost (heatings: Device list) =
            div [ _id "climate"; _class "mb-8" ] [
                h3 [ _class "text-xl font-semibold mb-2 text-gray-800 dark:text-gray-100" ] [ str "Climate (floor heating)" ]
                p [ _class "mb-2 text-gray-600 dark:text-gray-400" ] [ str "One block per room controller. Add the sensor:, rest_command:, and climate: blocks separately." ]
                div [ _class "p-4 mb-4 rounded bg-yellow-50 border-l-4 border-yellow-400 text-yellow-800 dark:bg-yellow-900/20 dark:text-yellow-200" ] [
                    strong [] [ str "Requires HACS: " ]
                    str "the "
                    code [] [ str "climate: - platform: template" ]
                    str " block is not part of core Home Assistant. Install the "
                    a [
                        _href "https://github.com/jcwillox/hass-template-climate"
                        _target "_blank"
                        _rel "noopener noreferrer"
                        _class "underline"
                    ] [ str "Climate Template (hass-template-climate)" ]
                    str " custom integration via HACS first."
                ]
                HaYaml.climateLines currentHost heatings
                |> htmlYaml
            ]

        let private healthRow currentHost =
            div [ _id "health"; _class "mb-8" ] [
                h3 [ _class "text-xl font-semibold mb-2 text-gray-800 dark:text-gray-100" ] [ str "Bridge health sensor" ]
                p [ _class "mb-2 text-gray-600 dark:text-gray-400" ] [ str "Optional: monitors whether the Eaton bridge is reachable from Home Assistant." ]
                HaYaml.healthLines currentHost
                |> htmlYaml
            ]

        let private switchesRow currentHost (scenes: Scene list) devices =
            div [ _id "switches"; _class "mb-8" ] [
                h3 [ _class "text-xl font-semibold mb-2 text-gray-800 dark:text-gray-100" ] [ str "Switches & Scenes" ]
                [
                    "switch:"

                    yield!
                        scenes
                        |> List.collect (fun scene ->
                            let sceneId = scene.Id |> SceneId.value
                            let room = scene.Zone |> ZoneId.value

                            [
                                "  - platform: rest"
                                $"    name: Eaton - scene - {scene.Name}"
                                $"    resource: http://{currentHost}/triggerScene"
                                sprintf "    body_on: '{\"room\": \"%s\", \"scene\": \"%s\"}'" room sceneId
                                "    headers:"
                                "      Content-Type: application/json"
                            ]
                        )

                    yield!
                        devices
                        |> List.collect (fun switch ->
                            switch.Children
                            |> List.collect (fun switch ->
                                let deviceId = switch.DeviceId |> DeviceId.shortId |> ShortDeviceId.value
                                let room = switch.Zone |> Option.map ZoneId.value |> Option.defaultValue "unknown"

                                let state =
                                    match switch.Type with
                                    | Actuator DimmerActuator -> { On = "\"density\": 50"; Off = "\"density\": 0" }
                                    | Actuator ShutterActuator -> { On = "\"state\": \"open\""; Off = "\"state\": \"close\"" }
                                    | _ -> { On = "\"state\": \"on\""; Off = "\"state\": \"off\"" }

                                [
                                    "  - platform: rest"
                                    $"    name: Eaton - {Device.effectiveName switch}"
                                    $"    resource: http://{currentHost}/state"
                                    $"    state_resource: http://{currentHost}/state/{room}/{deviceId}"
                                    sprintf "    body_on: '{\"room\": \"%s\", \"device\": \"%s\", %s}'" room deviceId state.On
                                    sprintf "    body_off: '{\"room\": \"%s\", \"device\": \"%s\", %s}'" room deviceId state.Off

                                    match switch.Type with
                                    | Actuator ShutterActuator -> ()
                                    | _ -> "    is_on_template: '{{ value_json.state }}'"

                                    "    headers:"
                                    "      Content-Type: application/json"
                                ]
                            )
                        )
                ]
                |> htmlYaml
            ]

        type ConfigParameters = {
            Devices: Device list
            Settings: Settings
        }

        let private saveConfigScript = """
function configUrl() {
    var p = window.location.pathname.replace(/\/config\/?$/, '').replace(/\/$/, '');
    return p + '/config';
}
function saveConfig() {
    var nameInputs = document.querySelectorAll('input[type="text"][name^="name_"]');
    var entities = [];
    nameInputs.forEach(function(el) {
        var id = el.name.replace(/^name_/, '');
        var checkboxEl = document.querySelector('input[type="checkbox"][name="visible_' + id + '"]');
        var idOverrideEl = document.querySelector('input[type="text"][name="id_' + id + '"]');
        var visible = checkboxEl ? checkboxEl.checked : true;
        var displayName = el.value.trim();
        var idOverride = idOverrideEl ? idOverrideEl.value.trim() : '';
        entities.push({
            DeviceId: id,
            Visible: visible,
            DisplayName: displayName.length > 0 ? displayName : null,
            IdOverride: idOverride.length > 0 ? idOverride : null
        });
    });
    var zoneInputs = document.querySelectorAll('input[type="text"][name^="zone_"]');
    var zones = [];
    zoneInputs.forEach(function(el) {
        var zoneId = el.name.replace(/^zone_/, '');
        var displayName = el.value.trim();
        zones.push({ ZoneId: zoneId, DisplayName: displayName.length > 0 ? displayName : null });
    });
    fetch(configUrl(), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ Entities: entities, Zones: zones })
    }).then(function(r) {
        var msg = document.getElementById('saveMsg');
        msg.classList.remove('hidden');
        if (r.ok) {
            msg.className = 'mt-4 px-4 py-3 rounded bg-green-100 border border-green-400 text-green-800 dark:bg-green-900/20 dark:text-green-200';
            msg.textContent = 'Settings saved! Reload the index page to see updated YAML.';
        } else {
            msg.className = 'mt-4 px-4 py-3 rounded bg-red-100 border border-red-400 text-red-800 dark:bg-red-900/20 dark:text-red-200';
            msg.textContent = 'Error saving settings.';
        }
    }).catch(function(e) {
        var msg = document.getElementById('saveMsg');
        msg.classList.remove('hidden');
        msg.className = 'mt-4 px-4 py-3 rounded bg-red-100 border border-red-400 text-red-800 dark:bg-red-900/20 dark:text-red-200';
        msg.textContent = 'Network error: ' + e.message;
    });
}
"""

        let private settingsFormBody (parameters: ConfigParameters) : XmlNode list =
            let settingsMap =
                parameters.Settings.Entities
                |> List.map (fun e -> e.DeviceId, e)
                |> Map.ofList

            let getVisible id =
                settingsMap |> Map.tryFind id |> Option.map (fun e -> e.Visible) |> Option.defaultValue true

            let getDisplayName id =
                settingsMap |> Map.tryFind id |> Option.bind (fun e -> e.DisplayName) |> Option.defaultValue ""

            let getIdOverride id =
                settingsMap |> Map.tryFind id |> Option.bind (fun e -> e.IdOverride) |> Option.defaultValue ""

            let typeLabel = function
                | Actuator HeatingActuator -> "Heating Actuator"
                | Actuator ShutterActuator -> "Cover"
                | Actuator DimmerActuator -> "Light"
                | Actuator SwitchActuator -> "Switch"
                | Sensor AnalogSensor -> "Sensor"
                | Thermostat ThermostatSubType.RoomController -> "Climate"
                | Thermostat ThermostatSubType.TemperatureSensor -> "Temperature Sensor"
                | Thermostat ThermostatSubType.HumiditySensor -> "Humidity Sensor"
                | Thermostat ThermostatSubType.Adjustment -> "Temp. Adjustment"
                | PushButton -> "Push Button"
                | Other _ -> "Other"

            let entitiesToConfigure =
                parameters.Devices
                |> List.collect (fun device ->
                    if Device.isHeating device then
                        let zone = device.Zone |> Option.map ZoneId.value |> Option.defaultValue "-"
                        [ zone, device ]
                    else
                        device.Children
                        |> List.map (fun child ->
                            let zone =
                                child.Zone
                                |> Option.orElse device.Zone
                                |> Option.map ZoneId.value
                                |> Option.defaultValue "-"
                            zone, child
                        )
                )
                |> List.sortBy (fun (zone, device) -> zone, device.Name)

            let distinctZones =
                entitiesToConfigure
                |> List.map fst
                |> List.distinct
                |> List.sort

            [
                h2 [ _class "text-2xl font-semibold mb-2" ] [ str "Zone Names" ]
                p [ _class "mb-3 text-gray-600 dark:text-gray-400" ] [ str "Give each zone a human-readable label. The raw zone id is still used in generated payloads." ]
                div [ _class "overflow-x-auto mb-8" ] [
                    table [ _class "w-full text-sm border-collapse" ] [
                        thead [ _class "bg-gray-100 dark:bg-gray-700" ] [
                            tr [] [
                                th [ _class "text-left px-3 py-2 border border-gray-200 dark:border-gray-600" ] [ str "Zone Id" ]
                                th [ _class "text-left px-3 py-2 border border-gray-200 dark:border-gray-600" ] [ str "Display Name" ]
                            ]
                        ]
                        tbody [] [
                            yield!
                                distinctZones
                                |> List.map (fun zoneId ->
                                    let currentLabel =
                                        Settings.zoneDisplayName parameters.Settings zoneId
                                        |> Option.defaultValue ""
                                    tr [ _class "odd:bg-white even:bg-gray-50 dark:odd:bg-gray-900 dark:even:bg-gray-800" ] [
                                        td [ _class "px-3 py-1 border border-gray-200 dark:border-gray-600" ] [ code [ _class "text-xs" ] [ str zoneId ] ]
                                        td [ _class "px-3 py-1 border border-gray-200 dark:border-gray-600" ] [
                                            input [
                                                _type "text"
                                                _class "w-full px-2 py-1 text-sm border rounded bg-white dark:bg-gray-700 border-gray-300 dark:border-gray-600 dark:text-gray-100"
                                                _name $"zone_{zoneId}"
                                                _placeholder zoneId
                                                _value currentLabel
                                            ]
                                        ]
                                    ]
                                )
                        ]
                    ]
                ]
                h2 [ _class "text-2xl font-semibold mb-2" ] [ str "Entity Settings" ]
                p [ _class "mb-3 text-gray-600 dark:text-gray-400" ] [ str "Toggle visibility, set custom display names, and override entity ids. Invisible entities are excluded from the generated YAML." ]
                div [ _class "overflow-x-auto mb-8" ] [
                    table [ _class "w-full text-sm border-collapse" ] [
                        thead [ _class "bg-gray-100 dark:bg-gray-700" ] [
                            tr [] [
                                th [ _class "text-left px-3 py-2 border border-gray-200 dark:border-gray-600" ] [ str "Zone" ]
                                th [ _class "text-left px-3 py-2 border border-gray-200 dark:border-gray-600" ] [ str "Type" ]
                                th [ _class "text-left px-3 py-2 border border-gray-200 dark:border-gray-600" ] [ str "Eaton Name" ]
                                th [ _class "text-left px-3 py-2 border border-gray-200 dark:border-gray-600" ] [ str "Display Name Override" ]
                                th [ _class "text-left px-3 py-2 border border-gray-200 dark:border-gray-600" ] [ str "Id Override" ]
                                th [ _class "text-center px-3 py-2 border border-gray-200 dark:border-gray-600" ] [ str "Visible" ]
                            ]
                        ]
                        tbody [] [
                            yield!
                                entitiesToConfigure
                                |> List.map (fun (zone, device) ->
                                    let id = device.DeviceId |> DeviceId.id
                                    let visible = getVisible id
                                    let displayName = getDisplayName id
                                    let idOverride = getIdOverride id
                                    let zoneLabel =
                                        Settings.zoneDisplayName parameters.Settings zone
                                        |> Option.defaultValue zone

                                    tr [ _class "odd:bg-white even:bg-gray-50 dark:odd:bg-gray-900 dark:even:bg-gray-800" ] [
                                        td [ _class "px-3 py-1 border border-gray-200 dark:border-gray-600" ] [ str zoneLabel ]
                                        td [ _class "px-3 py-1 border border-gray-200 dark:border-gray-600" ] [ small [ _class "text-xs text-gray-500 dark:text-gray-400" ] [ str (typeLabel device.Type) ] ]
                                        td [ _class "px-3 py-1 border border-gray-200 dark:border-gray-600" ] [ code [ _class "text-xs" ] [ str device.Name ] ]
                                        td [ _class "px-3 py-1 border border-gray-200 dark:border-gray-600" ] [
                                            input [
                                                _type "text"
                                                _class "w-full px-2 py-1 text-sm border rounded bg-white dark:bg-gray-700 border-gray-300 dark:border-gray-600 dark:text-gray-100"
                                                _name $"name_{id}"
                                                _placeholder device.Name
                                                _value displayName
                                            ]
                                        ]
                                        td [ _class "px-3 py-1 border border-gray-200 dark:border-gray-600" ] [
                                            input [
                                                _type "text"
                                                _class "w-full px-2 py-1 text-sm border rounded bg-white dark:bg-gray-700 border-gray-300 dark:border-gray-600 dark:text-gray-100"
                                                _name $"id_{id}"
                                                _placeholder id
                                                _value idOverride
                                            ]
                                        ]
                                        td [ _class "text-center px-3 py-1 border border-gray-200 dark:border-gray-600" ] [
                                            input (
                                                [ _type "checkbox"; _name $"visible_{id}"; _class "w-4 h-4" ]
                                                @ (if visible then [ _checked ] else [])
                                            )
                                        ]
                                    ]
                                )
                        ]
                    ]
                ]
                button [
                    _class "px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600"
                    _onclick "saveConfig()"
                ] [ str "Save Settings" ]
                div [ _id "saveMsg"; _class "hidden mt-4 px-4 py-3 rounded" ] []
            ]

        type IndexParameters = {
            CurrentHost: string
            Devices: Device list
            Scenes: Scene list
            Settings: Settings
        }

        let index parameters =
            let sensors = parameters.Devices |> List.filter Device.isSensor
            let switches = parameters.Devices |> List.filter Device.isSwitch
            let covers = parameters.Devices |> List.filter Device.isCover
            let dimmers = parameters.Devices |> List.filter Device.isDimmer
            let heatings = parameters.Devices |> List.filter Device.isHeating

            let tocItems =
                [
                    if sensors |> List.isEmpty |> not then yield "sensors", "Sensors"
                    if switches |> List.isEmpty |> not || parameters.Scenes |> List.isEmpty |> not then yield "switches", "Switches & Scenes"
                    if covers |> List.isEmpty |> not then yield "covers", "Covers"
                    if dimmers |> List.isEmpty |> not then yield "lights", "Lights"
                    if heatings |> List.isEmpty |> not then yield "climate", "Climate"
                    yield "health", "Bridge Health"
                ]

            html [ _class "bg-white dark:bg-gray-900" ] [
                htmlHead
                body [ _class "bg-white dark:bg-gray-900 text-gray-900 dark:text-gray-100 min-h-screen" ] [
                    div [ _class "max-w-5xl mx-auto px-4 py-6" ] [
                        div [ _class "flex justify-between items-center mb-4" ] [
                            h1 [ _class "text-3xl font-bold" ] [ str "Eaton Addon" ]
                            button [
                                _class "px-3 py-1 rounded border border-gray-300 dark:border-gray-600 text-sm hover:bg-gray-100 dark:hover:bg-gray-700"
                                _onclick "toggleDark()"
                            ] [ str "Toggle Dark" ]
                        ]
                        p [ _class "mb-6" ] [
                            button [
                                _class "text-blue-600 dark:text-blue-400 hover:underline"
                                _onclick "openSettings()"
                            ] [ str "⚙ Entity Settings" ]
                        ]

                        h2 [ _class "text-2xl font-semibold mb-4" ] [ str "Add following to your configuration.yaml" ]

                        nav [ _class "mb-6" ] [
                            ul [ _class "flex flex-wrap gap-3 list-none" ] [
                                yield! tocItems |> List.map (fun (anchor, label) ->
                                    li [] [ a [ _href $"#{anchor}"; _class "text-blue-600 dark:text-blue-400 hover:underline" ] [ str label ] ]
                                )
                            ]
                        ]

                        if sensors |> List.isEmpty |> not then sensorsRow parameters.CurrentHost sensors
                        switchesRow parameters.CurrentHost parameters.Scenes switches
                        if covers |> List.isEmpty |> not then coversRow parameters.CurrentHost covers
                        if dimmers |> List.isEmpty |> not then lightsRow parameters.CurrentHost dimmers
                        if heatings |> List.isEmpty |> not then climateRow parameters.CurrentHost heatings
                        healthRow parameters.CurrentHost
                    ]
                    div [
                        _id "settingsModal"
                        _class "hidden fixed inset-0 z-50 flex items-start justify-center bg-black/50 p-4 overflow-y-auto"
                        _onclick "onModalBackdrop(event)"
                    ] [
                        div [ _class "bg-white dark:bg-gray-900 text-gray-900 dark:text-gray-100 rounded-lg shadow-xl w-full max-w-5xl my-8 p-6"; _id "settingsModalPanel" ] [
                            div [ _class "flex justify-between items-center mb-4" ] [
                                h2 [ _class "text-2xl font-bold" ] [ str "Entity Settings" ]
                                button [
                                    _class "px-3 py-1 rounded border border-gray-300 dark:border-gray-600 text-sm hover:bg-gray-100 dark:hover:bg-gray-700"
                                    _onclick "closeSettings()"
                                ] [ str "✕ Close" ]
                            ]
                            yield! settingsFormBody { Devices = parameters.Devices; Settings = parameters.Settings }
                        ]
                    ]
                    script [] [
                        rawText """
function toggleDark() {
    var html = document.documentElement;
    var dark = !html.classList.contains('dark');
    html.classList.toggle('dark', dark);
    localStorage.setItem('theme', dark ? 'dark' : 'light');
    if (window.__applyPrismTheme) window.__applyPrismTheme(dark);
}
function openSettings() {
    document.getElementById('settingsModal').classList.remove('hidden');
    document.body.classList.add('overflow-hidden');
}
function closeSettings() {
    document.getElementById('settingsModal').classList.add('hidden');
    document.body.classList.remove('overflow-hidden');
}
function onModalBackdrop(e) {
    if (e.target && e.target.id === 'settingsModal') closeSettings();
}
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') closeSettings();
});
"""
                    ]
                    script [] [
                        rawText saveConfigScript
                    ]
                ]
            ]

        let configPage (parameters: ConfigParameters) =
            html [ _class "bg-white dark:bg-gray-900" ] [
                htmlHead
                body [ _class "bg-white dark:bg-gray-900 text-gray-900 dark:text-gray-100 min-h-screen" ] [
                    div [ _class "max-w-5xl mx-auto px-4 py-6" ] [
                        div [ _class "flex justify-between items-center mb-4" ] [
                            h1 [ _class "text-3xl font-bold" ] [ str "Eaton Config" ]
                            button [
                                _class "px-3 py-1 rounded border border-gray-300 dark:border-gray-600 text-sm hover:bg-gray-100 dark:hover:bg-gray-700"
                                _onclick "toggleDark()"
                            ] [ str "Toggle Dark" ]
                        ]
                        p [ _class "mb-6" ] [ a [ _href "."; _class "text-blue-600 dark:text-blue-400 hover:underline" ] [ str "← Back to index" ] ]
                        yield! settingsFormBody parameters
                    ]
                    script [] [
                        rawText """
function toggleDark() {
    var html = document.documentElement;
    var dark = !html.classList.contains('dark');
    html.classList.toggle('dark', dark);
    localStorage.setItem('theme', dark ? 'dark' : 'light');
    if (window.__applyPrismTheme) window.__applyPrismTheme(dark);
}
"""
                    ]
                    script [] [
                        rawText saveConfigScript
                    ]
                ]
            ]
