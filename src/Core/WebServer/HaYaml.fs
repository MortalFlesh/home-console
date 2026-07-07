namespace MF.HomeConsole.WebServer

open MF.Eaton

[<RequireQualifiedAccess>]
module HaYaml =
    open MF.HomeConsole
    open MF.Utils

    let coverLines currentHost (covers: Device list) : string list =
        let coverId (device: Device) =
            device.DeviceId |> DeviceId.id

        let restCommandEntries (device: Device) =
            let room = device.Zone |> Option.map ZoneId.value |> Option.defaultValue "unknown"
            let deviceId = device.DeviceId |> DeviceId.shortId |> ShortDeviceId.value
            let id = coverId device
            [ "open"; "close"; "stop" ]
            |> List.collect (fun action -> [
                $"  eaton_{id}_{action}:"
                $"    url: \"http://{currentHost}/state\""
                "    method: POST"
                "    headers:"
                "      Content-Type: application/json"
                sprintf "    payload: '{\"room\": \"%s\", \"device\": \"%s\", \"state\": \"%s\"}'" room deviceId action
            ])

        let coverChildren =
            covers
            |> List.collect (fun device -> device.Children)

        [
            "rest_command:"
            yield! coverChildren |> List.collect restCommandEntries

            ""
            "cover:"
            "  - platform: template"
            "    covers:"
            yield!
                coverChildren
                |> List.collect (fun device ->
                    let id = coverId device
                    [
                        $"      eaton_cover_{id}:"
                        $"        friendly_name: \"{Device.effectiveName device}\""
                        "        open_cover:"
                        $"          action: rest_command.eaton_{id}_open"
                        "        close_cover:"
                        $"          action: rest_command.eaton_{id}_close"
                        "        stop_cover:"
                        $"          action: rest_command.eaton_{id}_stop"
                    ]
                )
        ]


    let templateSensorLines (sensors: Device list) : string list =
        [
            ""
            "template:"
            "  - sensor:"

            yield!
                sensors
                |> List.collect (fun sensor ->
                    sensor.Children
                    |> List.collect (fun sensor ->
                        let name = sensor.Name |> Name.asUniqueKey
                        let deviceId = sensor.DeviceId |> DeviceId.id

                        match sensor.Type with
                        | Actuator HeatingActuator ->
                            (
                                [
                                    {| Metric = "temperature"; Unit = "°C"; DeviceClass = "temperature" |}
                                    {| Metric = "power_percentage"; Unit = "%"; DeviceClass = "power_factor" |}
                                    {| Metric = "power"; Unit = "W"; DeviceClass = "power" |}
                                    {| Metric = "overload"; Unit = ""; DeviceClass = "value" |}
                                ]
                                |> List.collect (fun heating ->
                                    let metric = heating.Metric
                                    let valueTemplate = sprintf "{{ state_attr('sensor.eaton', '%s')['%s'] | float }}" deviceId metric

                                    [
                                        $"      - unique_id: {name}_{metric}"
                                        $"        name: \"{Device.effectiveName sensor} ({metric})\""
                                        $"        state: \"{valueTemplate}\""
                                        $"        unit_of_measurement: \"{heating.Unit}\""
                                        $"        device_class: {heating.DeviceClass}"
                                    ]
                                )
                            )
                            @ [
                                $"      - unique_id: {name}_last_update"
                                $"        name: \"{Device.effectiveName sensor} (last update)\""
                                $"        state: \"{{{{ state_attr('sensor.eaton', '{deviceId}')['last_update'] }}}}\""
                                "        device_class: timestamp"
                            ]

                        | _ ->
                            let valueType = sensor.Type |> DeviceType.valueType
                            let unitOfMeasurement = sensor.Type |> DeviceType.unitOfMeasure |> Option.defaultValue "... add manually ..."
                            let valueTemplate = sprintf "{{ state_attr('sensor.eaton', '%s')['%s'] | float }}" deviceId valueType

                            [
                                $"      - unique_id: {name}"
                                $"        name: \"{Device.effectiveName sensor}\""
                                $"        state: \"{valueTemplate}\""
                                $"        unit_of_measurement: \"{unitOfMeasurement}\""
                                $"        device_class: {valueType}"
                                $"      - unique_id: {name}_last_update"
                                $"        name: \"{Device.effectiveName sensor} (last update)\""
                                $"        state: \"{{{{ state_attr('sensor.eaton', '{deviceId}')['last_update'] }}}}\""
                                "        device_class: timestamp"
                            ]
                    )
                )
        ]

    let lightLines currentHost (dimmers: Device list) : string list =
        let dimmerId (device: Device) =
            device.DeviceId |> DeviceId.id

        let dimmerChildren =
            dimmers
            |> List.collect (fun device -> device.Children)

        let dimmerIds =
            dimmerChildren
            |> List.map dimmerId

        [
            "sensor:"
            "  - platform: rest"
            $"    resource: http://{currentHost}/brightness"
            "    scan_interval: 60"
            "    name: eaton_brightness"
            "    value_template: OK"
            "    json_attributes_path: \"$.Brightness\""
            "    json_attributes:"
            yield! dimmerIds |> List.map (fun id -> $"      - {id}")

            ""
            "rest_command:"
            yield!
                dimmerChildren
                |> List.collect (fun device ->
                    let room = device.Zone |> Option.map ZoneId.value |> Option.defaultValue "unknown"
                    let deviceId = device.DeviceId |> DeviceId.shortId |> ShortDeviceId.value
                    let id = dimmerId device
                    [
                        $"  eaton_{id}_set_level:"
                        $"    url: \"http://{currentHost}/state\""
                        "    method: POST"
                        "    headers:"
                        "      Content-Type: application/json"
                        sprintf "    payload: '{\"room\": \"%s\", \"device\": \"%s\", \"density\": {{ brightness_pct }}}'" room deviceId
                    ]
                )

            ""
            "light:"
            "  - platform: template"
            "    lights:"
            yield!
                dimmerChildren
                |> List.collect (fun device ->
                    let id = dimmerId device
                    [
                        $"      eaton_light_{id}:"
                        $"        friendly_name: \"{Device.effectiveName device}\""
                        $"        level_template: >-"
                        $"          {{{{ (state_attr('sensor.eaton_brightness', '{id}') | int(0)) * 255 / 100 }}}}"
                        $"        value_template: \"{{{{ (state_attr('sensor.eaton_brightness', '{id}') | int(0)) > 0 }}}}\""
                        "        turn_on:"
                        $"          action: rest_command.eaton_{id}_set_level"
                        "          data:"
                        "            brightness_pct: 100"
                        "        turn_off:"
                        $"          action: rest_command.eaton_{id}_set_level"
                        "          data:"
                        "            brightness_pct: 0"
                        "        set_level:"
                        $"          action: rest_command.eaton_{id}_set_level"
                        "          data:"
                        "            brightness_pct: \"{{ (brightness / 255 * 100) | round(0) }}\""
                    ]
                )
        ]

    let climateLines currentHost (heatings: Device list) : string list =
        let withZone =
            heatings
            |> List.choose (fun device ->
                device.Zone |> Option.map (fun zone -> device, zone |> ZoneId.value)
            )

        match withZone with
        | [] -> []
        | _ ->
            [
                "sensor:"
                yield!
                    withZone
                    |> List.collect (fun (device, zoneId) ->
                        let id = device.DeviceId |> DeviceId.id
                        let sensorName = $"eaton_climate_{id}"
                        [
                            "  - platform: rest"
                            $"    resource: http://{currentHost}/climate/{zoneId}"
                            "    scan_interval: 60"
                            $"    name: {sensorName}"
                            $"    value_template: \"{{{{ value_json.Temperature }}}}\""
                            "    json_attributes:"
                            "      - Temperature"
                            "      - Setpoint"
                        ]
                    )

                ""
                "rest_command:"
                yield!
                    withZone
                    |> List.collect (fun (device, zoneId) ->
                        let id = device.DeviceId |> DeviceId.id
                        let restCommandName = $"eaton_climate_{id}_set_temp"
                        [
                            $"  {restCommandName}:"
                            $"    url: \"http://{currentHost}/climate\""
                            "    method: POST"
                            "    headers:"
                            "      Content-Type: application/json"
                            sprintf "    payload: '{\"room\": \"%s\", \"temperature\": {{ temperature }}}'" zoneId
                        ]
                    )

                ""
                "climate:"
                "  - platform: template"
                "    climates:"
                yield!
                    withZone
                    |> List.collect (fun (device, _zoneId) ->
                        let id = device.DeviceId |> DeviceId.id
                        let sensorName = $"eaton_climate_{id}"
                        let restCommandName = $"eaton_climate_{id}_set_temp"
                        let climateId = $"eaton_climate_{id}"
                        let name = Device.effectiveName device
                        [
                            $"      {climateId}:"
                            $"        friendly_name: \"{name}\""
                            $"        current_temperature_template: \"{{{{ state_attr('sensor.{sensorName}', 'Temperature') | float(0) }}}}\""
                            $"        target_temperature_template: \"{{{{ state_attr('sensor.{sensorName}', 'Setpoint') | float(0) }}}}\""
                            "        hvac_modes:"
                            "          - heat"
                            "        hvac_mode_template: \"heat\""
                            "        set_temperature:"
                            $"          action: rest_command.{restCommandName}"
                            "          data:"
                            "            temperature: \"{{ temperature }}\""
                            "        min_temp: 5"
                            "        max_temp: 30"
                            "        target_temp_step: 0.5"
                        ]
                    )
            ]

    let healthLines currentHost : string list =
        [
            "binary_sensor:"
            "  - platform: rest"
            $"    resource: http://{currentHost}/health"
            "    name: eaton_bridge"
            "    value_template: \"{{ value_json.Status == 'ok' }}\""
            "    device_class: connectivity"
            "    scan_interval: 60"
        ]
