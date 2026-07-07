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
            lines
            |> List.map (sprintf "%s\n" >> str)
            |> pre [ _style "border: 1px dotted gray; padding: 10px" ]  // todo - format yaml properly

        let htmlHead =
            head [] [
                meta [ _charset "utf-8" ]
                meta [ _httpEquiv "X-UA-Compatible"; _content "IE=edge" ]
                meta [ _name "viewport"; _content "width=device-width, initial-scale=1.0" ]
                title [] [ str "EATON Addon" ]
                link [
                    _rel "stylesheet"
                    _href "https://cdn.jsdelivr.net/npm/bootstrap@4.3.1/dist/css/bootstrap.min.css"
                    _integrity "sha384-ggOyR0iXCbMQv3Xipma34MD+dH/1fQ784/j6cY/iJTQUOhcWr7x9JvoRxT2MZw1T"
                    _crossorigin "anonymous"
                ]
            ]

        let private sensorsRow currentHost (sensors: Device list) =
            div [ _class "row" ] [
                div [ _class "col-md-12" ] [
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
            ]

        type private StateChange = {
            On: string
            Off: string
        }

        let private coversRow currentHost (covers: Device list) =
            div [ _class "row" ] [
                div [ _class "col-md-12" ] [
                    h3 [] [ str "Covers (shutters, blinds, awnings)" ]
                    p [] [ str "Add rest_command entries to your rest_command: section, and the cover: block separately." ]
                    HaYaml.coverLines currentHost covers
                    |> htmlYaml
                ]
            ]

        let private lightsRow currentHost (dimmers: Device list) =
            div [ _class "row" ] [
                div [ _class "col-md-12" ] [
                    h3 [] [ str "Lights (dimmers)" ]
                    p [] [ str "Add the sensor: block, then rest_command entries to your rest_command: section, and the light: block separately." ]
                    HaYaml.lightLines currentHost dimmers
                    |> htmlYaml
                ]
            ]

        let private climateRow currentHost (heatings: Device list) =
            div [ _class "row" ] [
                div [ _class "col-md-12" ] [
                    h3 [] [ str "Climate (floor heating)" ]
                    p [] [ str "One block per room controller. Add the sensor:, rest_command:, and climate: blocks separately." ]
                    div [ _class "alert alert-warning" ] [
                        strong [] [ str "Requires HACS: " ]
                        str "the "
                        code [] [ str "climate: - platform: template" ]
                        str " block is not part of core Home Assistant. Install the "
                        a [
                            _href "https://github.com/jcwillox/hass-template-climate"
                            _target "_blank"
                            _rel "noopener noreferrer"
                        ] [ str "Climate Template (hass-template-climate)" ]
                        str " custom integration via HACS first."
                    ]
                    HaYaml.climateLines currentHost heatings
                    |> htmlYaml
                ]
            ]

        let private healthRow currentHost =
            div [ _class "row" ] [
                div [ _class "col-md-12" ] [
                    h3 [] [ str "Bridge health sensor" ]
                    p [] [ str "Optional: monitors whether the Eaton bridge is reachable from Home Assistant." ]
                    HaYaml.healthLines currentHost
                    |> htmlYaml
                ]
            ]

        let private switchesRow currentHost (scenes: Scene list) devices =
            div [ _class "row" ] [
                div [ _class "col-md-12" ] [
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
            ]

        type IndexParameters = {
            CurrentHost: string
            Devices: Device list
            Scenes: Scene list
        }

        let index parameters =
            html [] [
                htmlHead

                body [] [
                    div [ _class "container" ] [
                        div [ _class "row" ] [
                            div [ _class "col-md-12" ] [
                                h1 [] [ str "Eaton Addon" ]
                                p [] [ a [ _href "/config" ] [ str "⚙ Entity Settings" ] ]
                            ]
                        ]
                        div [ _class "row" ] [
                            div [ _class "col-md-12" ] [
                                h2 [] [ str "Add following to your configuration.yaml" ]

                                parameters.Devices
                                |> List.filter Device.isSensor
                                |> sensorsRow parameters.CurrentHost

                                parameters.Devices
                                |> List.filter Device.isSwitch
                                |> switchesRow parameters.CurrentHost parameters.Scenes

                                parameters.Devices
                                |> List.filter Device.isCover
                                |> coversRow parameters.CurrentHost

                                parameters.Devices
                                |> List.filter Device.isDimmer
                                |> lightsRow parameters.CurrentHost

                                parameters.Devices
                                |> List.filter Device.isHeating
                                |> climateRow parameters.CurrentHost

                                healthRow parameters.CurrentHost
                            ]
                        ]
                    ]
                ]
            ]

        type ConfigParameters = {
            Devices: Device list
            Settings: Settings
        }

        let configPage (parameters: ConfigParameters) =
            let settingsMap =
                parameters.Settings.Entities
                |> List.map (fun e -> e.DeviceId, e)
                |> Map.ofList

            let getVisible id =
                settingsMap |> Map.tryFind id |> Option.map (fun e -> e.Visible) |> Option.defaultValue true

            let getDisplayName id =
                settingsMap |> Map.tryFind id |> Option.bind (fun e -> e.DisplayName) |> Option.defaultValue ""

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

            // Entities that appear in the generated YAML:
            // - for heating: the parent RoomController is the climate entity
            // - for all others: the children are the entities
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

            html [] [
                htmlHead
                body [] [
                    div [ _class "container"; _style "margin-top: 20px" ] [
                        h1 [] [ str "Eaton Config" ]
                        p [] [ a [ _href "/" ] [ str "← Back to index" ] ]
                        h2 [] [ str "Entity Settings" ]
                        p [] [ str "Toggle visibility and set custom display names. Invisible entities are excluded from the generated YAML." ]
                        div [ _class "table-responsive" ] [
                            table [ _class "table table-sm table-striped" ] [
                                thead [] [
                                    tr [] [
                                        th [] [ str "Zone" ]
                                        th [] [ str "Type" ]
                                        th [] [ str "Eaton Name" ]
                                        th [] [ str "Display Name Override" ]
                                        th [] [ str "Visible" ]
                                    ]
                                ]
                                tbody [] [
                                    yield!
                                        entitiesToConfigure
                                        |> List.map (fun (zone, device) ->
                                            let id = device.DeviceId |> DeviceId.id
                                            let visible = getVisible id
                                            let displayName = getDisplayName id

                                            tr [] [
                                                td [] [ str zone ]
                                                td [] [ small [] [ str (typeLabel device.Type) ] ]
                                                td [] [ code [] [ str device.Name ] ]
                                                td [] [
                                                    input [
                                                        _type "text"
                                                        _class "form-control form-control-sm"
                                                        _name $"name_{id}"
                                                        _placeholder device.Name
                                                        _value displayName
                                                    ]
                                                ]
                                                td [ _class "text-center" ] [
                                                    input (
                                                        [ _type "checkbox"; _name $"visible_{id}" ]
                                                        @ (if visible then [ _checked ] else [])
                                                    )
                                                ]
                                            ]
                                        )
                                ]
                            ]
                        ]
                        button [ _class "btn btn-primary"; _onclick "saveConfig()" ] [ str "Save Settings" ]
                        div [ _id "saveMsg"; _style "display:none; margin-top: 10px" ] []
                        script [] [
                            rawText """
function saveConfig() {
    var nameInputs = document.querySelectorAll('input[type="text"][name^="name_"]');
    var entities = [];
    nameInputs.forEach(function(el) {
        var id = el.name.replace(/^name_/, '');
        var checkboxEl = document.querySelector('input[type="checkbox"][name="visible_' + id + '"]');
        var visible = checkboxEl ? checkboxEl.checked : true;
        var displayName = el.value.trim();
        entities.push({ DeviceId: id, Visible: visible, DisplayName: displayName.length > 0 ? displayName : null });
    });
    fetch('/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ Entities: entities })
    }).then(function(r) {
        var msg = document.getElementById('saveMsg');
        msg.style.display = 'block';
        if (r.ok) {
            msg.className = 'alert alert-success';
            msg.textContent = 'Settings saved! Reload the index page to see updated YAML.';
        } else {
            msg.className = 'alert alert-danger';
            msg.textContent = 'Error saving settings.';
        }
    }).catch(function(e) {
        var msg = document.getElementById('saveMsg');
        msg.style.display = 'block';
        msg.className = 'alert alert-danger';
        msg.textContent = 'Network error: ' + e.message;
    });
}
"""
                        ]
                    ]
                ]
            ]
