module HaYamlTests

open Expecto
open MF.Eaton
open MF.HomeConsole.WebServer

let private makeDevice name deviceId deviceType children = {
    Name = name
    DisplayName = name
    DeviceId = DeviceId deviceId
    Parent = None
    SerialNumber = 0
    Type = deviceType
    Rssi = Rssi.Disabled
    Powered = PowerStatus.Always
    Children = children
    Zone = None
}

let private child name deviceId deviceType =
    makeDevice name deviceId deviceType []

[<Tests>]
let haYamlTests =
    testList "HaYaml.templateSensorLines" [

        test "empty sensors list returns only the header" {
            let lines = HaYaml.templateSensorLines []
            Expect.equal lines [ ""; "template:"; "  - sensor:" ] "header only"
        }

        test "device with no children contributes no sensor lines" {
            let device = makeDevice "Zone" "parent_id" (Other None) []
            let lines = HaYaml.templateSensorLines [ device ]
            Expect.equal lines [ ""; "template:"; "  - sensor:" ] "header only when no children"
        }

        test "non-heating sensor generates unique_id, name, state, unit and device_class" {
            let sensor = child "Living Room" "hdm:xComfort Adapter:1234_u0" (Sensor AnalogSensor)
            let device = makeDevice "Zone" "parent_id" (Other None) [ sensor ]
            let lines = HaYaml.templateSensorLines [ device ]
            Expect.equal lines [
                ""
                "template:"
                "  - sensor:"
                "      - unique_id: eaton_living_room"
                "        name: \"Living Room\""
                "        state: \"{{ state_attr('sensor.eaton', 'hdm_xComfort_Adapter_1234_u0')['value'] | float }}\""
                "        unit_of_measurement: \"... add manually ...\""
                "        device_class: value"
                "      - unique_id: eaton_living_room_last_update"
                "        name: \"Living Room (last update)\""
                "        state: \"{{ state_attr('sensor.eaton', 'hdm_xComfort_Adapter_1234_u0')['last_update'] }}\""
                "        device_class: timestamp"
            ] "analog sensor lines"
        }

        test "heating actuator generates entries for temperature, power_percentage, power and overload" {
            let sensor = child "Room Heating" "hdm:xComfort Adapter:5678_u0" (Actuator HeatingActuator)
            let device = makeDevice "Zone" "parent_id" (Other None) [ sensor ]
            let lines = HaYaml.templateSensorLines [ device ]
            let id = "hdm_xComfort_Adapter_5678_u0"
            Expect.equal lines [
                ""
                "template:"
                "  - sensor:"
                "      - unique_id: eaton_room_heating_temperature"
                "        name: \"Room Heating (temperature)\""
                $"        state: \"{{{{ state_attr('sensor.eaton', '{id}')['temperature'] | float }}}}\""
                "        unit_of_measurement: \"°C\""
                "        device_class: temperature"
                "      - unique_id: eaton_room_heating_power_percentage"
                "        name: \"Room Heating (power_percentage)\""
                $"        state: \"{{{{ state_attr('sensor.eaton', '{id}')['power_percentage'] | float }}}}\""
                "        unit_of_measurement: \"%\""
                "        device_class: power_factor"
                "      - unique_id: eaton_room_heating_power"
                "        name: \"Room Heating (power)\""
                $"        state: \"{{{{ state_attr('sensor.eaton', '{id}')['power'] | float }}}}\""
                "        unit_of_measurement: \"W\""
                "        device_class: power"
                "      - unique_id: eaton_room_heating_overload"
                "        name: \"Room Heating (overload)\""
                $"        state: \"{{{{ state_attr('sensor.eaton', '{id}')['overload'] | float }}}}\""
                "        unit_of_measurement: \"\""
                "        device_class: value"
                "      - unique_id: eaton_room_heating_last_update"
                "        name: \"Room Heating (last update)\""
                $"        state: \"{{{{ state_attr('sensor.eaton', '{id}')['last_update'] }}}}\""
                "        device_class: timestamp"
            ] "heating actuator all metric lines"
        }

        test "Czech characters in name are transliterated in unique_id but kept in display name" {
            let sensor = child "Obývací pokoj" "hdm:xComfort Adapter:9999_u0" (Sensor AnalogSensor)
            let device = makeDevice "Zone" "parent_id" (Other None) [ sensor ]
            let lines = HaYaml.templateSensorLines [ device ]
            Expect.exists lines (fun l -> l.Contains "unique_id: eaton_obyvaci_pokoj") "unique_id transliterated"
            Expect.exists lines (fun l -> l.Contains "name: \"Obývací pokoj\"") "display name unchanged"
        }

        test "multiple children each produce their own entry" {
            let s1 = child "Sensor A" "device_A" (Sensor AnalogSensor)
            let s2 = child "Sensor B" "device_B" (Sensor AnalogSensor)
            let device = makeDevice "Zone" "parent_id" (Other None) [ s1; s2 ]
            let lines = HaYaml.templateSensorLines [ device ]
            Expect.exists lines (fun l -> l.Contains "unique_id: eaton_sensor_a") "sensor A present"
            Expect.exists lines (fun l -> l.Contains "unique_id: eaton_sensor_b") "sensor B present"
        }
    ]

let private inZone zone (device: Device) =
    { device with Zone = Some (ZoneId zone) }

[<Tests>]
let climateLinesTests =
    testList "HaYaml.climateLines" [

        test "empty heatings list returns no lines" {
            let lines = HaYaml.climateLines "host" []
            Expect.equal lines [] "no lines"
        }

        test "device without a zone is skipped" {
            let device = makeDevice "Living Room" "hdm:xComfort Adapter:9214125" (Thermostat ThermostatSubType.RoomController) []
            let lines = HaYaml.climateLines "host" [ device ]
            Expect.equal lines [] "device without zone produces nothing"
        }

        test "room controller with zone generates sensor, rest_command and climate blocks" {
            let device =
                makeDevice "Living Room" "hdm:xComfort Adapter:9214125" (Thermostat ThermostatSubType.RoomController) []
                |> inZone "hz_5"

            let lines = HaYaml.climateLines "192.168.1.10:28080" [ device ]

            Expect.equal lines [
                "sensor:"
                "  - platform: rest"
                "    resource: http://192.168.1.10:28080/climate/hz_5"
                "    scan_interval: 60"
                "    name: eaton_climate_hdm_xComfort_Adapter_9214125"
                "    value_template: \"{{ value_json.Temperature }}\""
                "    json_attributes:"
                "      - Temperature"
                "      - Setpoint"
                ""
                "rest_command:"
                "  eaton_climate_hdm_xComfort_Adapter_9214125_set_temp:"
                "    url: \"http://192.168.1.10:28080/climate\""
                "    method: POST"
                "    headers:"
                "      Content-Type: application/json"
                "    payload: '{\"room\": \"hz_5\", \"temperature\": {{ temperature }}}'"
                ""
                "climate:"
                "  - platform: template"
                "    climates:"
                "      eaton_climate_hdm_xComfort_Adapter_9214125:"
                "        friendly_name: \"Living Room\""
                "        current_temperature_template: \"{{ state_attr('sensor.eaton_climate_hdm_xComfort_Adapter_9214125', 'Temperature') | float(0) }}\""
                "        target_temperature_template: \"{{ state_attr('sensor.eaton_climate_hdm_xComfort_Adapter_9214125', 'Setpoint') | float(0) }}\""
                "        hvac_modes:"
                "          - heat"
                "        hvac_mode_template: \"heat\""
                "        set_temperature:"
                "          action: rest_command.eaton_climate_hdm_xComfort_Adapter_9214125_set_temp"
                "          data:"
                "            temperature: \"{{ temperature }}\""
                "        min_temp: 5"
                "        max_temp: 30"
                "        target_temp_step: 0.5"
            ] "full climate block"
        }

        test "multiple room controllers each produce their own climate id" {
            let a = makeDevice "Room A" "device_a" (Thermostat ThermostatSubType.RoomController) [] |> inZone "hz_1"
            let b = makeDevice "Room B" "device_b" (Thermostat ThermostatSubType.RoomController) [] |> inZone "hz_2"
            let lines = HaYaml.climateLines "host" [ a; b ]
            Expect.exists lines (fun l -> l.Contains "eaton_climate_device_a:") "room A climate present"
            Expect.exists lines (fun l -> l.Contains "eaton_climate_device_b:") "room B climate present"
            Expect.exists lines (fun l -> l.Contains "resource: http://host/climate/hz_1") "room A uses its zone"
            Expect.exists lines (fun l -> l.Contains "resource: http://host/climate/hz_2") "room B uses its zone"
        }
    ]

[<Tests>]
let lightLinesTests =
    testList "HaYaml.lightLines" [

        test "empty dimmers list still emits the aggregated brightness sensor header" {
            let lines = HaYaml.lightLines "host" []
            Expect.equal (lines |> List.head) "sensor:" "starts with sensor block"
            Expect.exists lines (fun l -> l.Contains "name: eaton_brightness") "brightness sensor present"
        }

        test "dimmer child generates rest_command, light template and brightness attribute" {
            let dimmerChild = child "Kitchen Light" "hdm:xComfort Adapter:4110125_u0" (Actuator DimmerActuator) |> inZone "hz_3"
            let dimmer = makeDevice "Kitchen" "parent" (Actuator DimmerActuator) [ dimmerChild ]
            let lines = HaYaml.lightLines "host" [ dimmer ]
            let id = "hdm_xComfort_Adapter_4110125_u0"

            Expect.exists lines (fun l -> l.Contains (sprintf "      - %s" id)) "brightness attribute present"
            Expect.exists lines (fun l -> l.Contains (sprintf "  eaton_%s_set_level:" id)) "rest_command present"
            Expect.exists lines (fun l -> l.Contains "payload: '{\"room\": \"hz_3\", \"device\": \"xCo:4110125_u0\", \"density\": {{ brightness_pct }}}'") "payload uses short device id and room"
            Expect.exists lines (fun l -> l.Contains (sprintf "      eaton_light_%s:" id)) "light template present"
            Expect.exists lines (fun l -> l.Contains "friendly_name: \"Kitchen Light\"") "friendly name present"
        }
    ]

[<Tests>]
let coverLinesTests =
    testList "HaYaml.coverLines" [

        test "empty covers list emits rest_command and cover headers only" {
            let lines = HaYaml.coverLines "host" []
            Expect.equal lines [ "rest_command:"; ""; "cover:"; "  - platform: template"; "    covers:" ] "headers only"
        }

        test "shutter child generates open, close and stop rest_commands and a cover template" {
            let shutterChild = child "Kitchen Blind" "hdm:xComfort Adapter:111_u0" (Actuator ShutterActuator) |> inZone "hz_1"
            let shutter = makeDevice "Kitchen" "parent" (Actuator ShutterActuator) [ shutterChild ]
            let lines = HaYaml.coverLines "host" [ shutter ]
            let id = "hdm_xComfort_Adapter_111_u0"

            Expect.exists lines (fun l -> l.Contains "payload: '{\"room\": \"hz_1\", \"device\": \"xCo:111_u0\", \"state\": \"open\"}'") "open payload present"
            Expect.exists lines (fun l -> l.Contains "payload: '{\"room\": \"hz_1\", \"device\": \"xCo:111_u0\", \"state\": \"close\"}'") "close payload present"
            Expect.exists lines (fun l -> l.Contains "payload: '{\"room\": \"hz_1\", \"device\": \"xCo:111_u0\", \"state\": \"stop\"}'") "stop payload present"
            Expect.exists lines (fun l -> l.Contains (sprintf "      eaton_cover_%s:" id)) "cover template present"
            Expect.exists lines (fun l -> l.Contains (sprintf "          action: rest_command.eaton_%s_open" id)) "open action wired"
        }
    ]

