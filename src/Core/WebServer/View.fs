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

                        "  - platform: template"
                        "    sensors:"

                        yield!
                            sensors
                            |> List.collect (fun sensor ->
                                sensor.Children
                                |> List.collect (fun sensor ->
                                    let name = sensor.Name |> Name.asUniqueKey // todo - use display name in the future?
                                    let deviceId = sensor.DeviceId |> DeviceId.id

                                    let valueType = sensor.Type |> DeviceType.valueType
                                    let unitOfMeassurement = sensor.Type |> DeviceType.unitOfMeassure |> Option.defaultValue "... add manually ..."

                                    [
                                        $"      {name}:"
                                        $"        unique_id: {name}"
                                        $"        friendly_name: \"{sensor.Name}\""
                                        $"        value_template: \"{{ state_attr('sensor.eaton', '{deviceId}')['{valueType}'] | float }}\""
                                        $"        unit_of_measurement: \"{unitOfMeassurement}\""
                                        $"        device_class: {valueType}"
                                        "        entity_id: sensor.eaton"
                                    ]
                                )
                            )
                    ]
                    |> htmlYaml
                ]
            ]

        type private StateChange = {
            On: string
            Off: string
        }

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
                                    let deviceId = switch.DeviceId |> DeviceId.shortId
                                    let room = switch.Zone |> Option.map ZoneId.value |> Option.defaultValue "unknown"

                                    let state =
                                        match switch.Type with
                                        | Actuator DimmerActuator -> { On = "\"density\": 50"; Off = "\"density\": 0" }
                                        | Actuator ShutterActuator -> { On = "\"state\": \"open\""; Off = "\"state\": \"close\"" }
                                        | _ -> { On = "\"state\": \"on\""; Off = "\"state\": \"off\"" }

                                    [
                                        "  - platform: rest"
                                        $"    name: Eaton - {switch.Name}"
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
                            ]
                        ]
                    ]
                ]
            ]
