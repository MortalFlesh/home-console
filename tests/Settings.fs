module SettingsTests

open Expecto
open MF.HomeConsole

[<Tests>]
let settingsRoundTripTests =
    testList "Settings serialize/parse (FSharp.Data)" [

        test "round-trips entities with and without a display name" {
            let settings : Settings = {
                Entities = [
                    { DeviceId = "hdm_x_1"; Visible = true; DisplayName = Some "Kitchen" }
                    { DeviceId = "hdm_x_2"; Visible = false; DisplayName = None }
                ]
            }

            let back = settings |> Settings.serialize |> Settings.parse

            Expect.equal back settings "round-trip preserves entities, visibility and optional display name"
        }

        test "parses frontend JSON (PascalCase, null display name)" {
            let json = """{"Entities":[{"DeviceId":"hdm_x_1","Visible":true,"DisplayName":"Kitchen"},{"DeviceId":"hdm_x_2","Visible":false,"DisplayName":null}]}"""

            let parsed = Settings.parse json

            Expect.equal parsed.Entities [
                { DeviceId = "hdm_x_1"; Visible = true; DisplayName = Some "Kitchen" }
                { DeviceId = "hdm_x_2"; Visible = false; DisplayName = None }
            ] "null display name becomes None"
        }

        test "empty settings round-trip" {
            let settings = Settings.empty
            let back = settings |> Settings.serialize |> Settings.parse
            Expect.equal back settings "empty settings survive round-trip"
        }

        test "invalid JSON returns an error from tryParse" {
            match Settings.tryParse "not json" with
            | Error _ -> ()
            | Ok _ -> failtest "expected an error for invalid JSON"
        }
    ]
