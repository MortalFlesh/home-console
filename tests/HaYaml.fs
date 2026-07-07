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

