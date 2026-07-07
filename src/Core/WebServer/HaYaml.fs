namespace MF.HomeConsole.WebServer

open MF.Eaton

[<RequireQualifiedAccess>]
module HaYaml =
    open MF.HomeConsole
    open MF.Utils

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
                                    $"        name: \"{sensor.Name} ({metric})\""
                                    $"        state: \"{valueTemplate}\""
                                    $"        unit_of_measurement: \"{heating.Unit}\""
                                    $"        device_class: {heating.DeviceClass}"
                                ]
                            )

                        | _ ->
                            let valueType = sensor.Type |> DeviceType.valueType
                            let unitOfMeasurement = sensor.Type |> DeviceType.unitOfMeasure |> Option.defaultValue "... add manually ..."
                            let valueTemplate = sprintf "{{ state_attr('sensor.eaton', '%s')['%s'] | float }}" deviceId valueType

                            [
                                $"      - unique_id: {name}"
                                $"        name: \"{sensor.Name}\""
                                $"        state: \"{valueTemplate}\""
                                $"        unit_of_measurement: \"{unitOfMeasurement}\""
                                $"        device_class: {valueType}"
                            ]
                    )
                )
        ]
