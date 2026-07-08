module SettingsTests

open Expecto
open MF.HomeConsole

[<Tests>]
let settingsRoundTripTests =
    testList "Settings serialize/parse (FSharp.Data)" [

        test "round-trips entities with and without a display name" {
            let settings : Settings = {
                Entities = [
                    { DeviceId = "hdm_x_1"; Visible = true; DisplayName = Some "Kitchen"; IdOverride = None }
                    { DeviceId = "hdm_x_2"; Visible = false; DisplayName = None; IdOverride = None }
                ]
                Zones = []
            }

            let back = settings |> Settings.serialize |> Settings.parse

            Expect.equal back settings "round-trip preserves entities, visibility and optional display name"
        }

        test "parses frontend JSON (PascalCase, null display name)" {
            let json = """{"Entities":[{"DeviceId":"hdm_x_1","Visible":true,"DisplayName":"Kitchen"},{"DeviceId":"hdm_x_2","Visible":false,"DisplayName":null}],"Zones":[]}"""

            let parsed = Settings.parse json

            Expect.equal parsed.Entities [
                { DeviceId = "hdm_x_1"; Visible = true; DisplayName = Some "Kitchen"; IdOverride = None }
                { DeviceId = "hdm_x_2"; Visible = false; DisplayName = None; IdOverride = None }
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

        test "round-trips zones with and without a display name" {
            let settings : Settings = {
                Entities = []
                Zones = [
                    { ZoneId = "hz_1"; DisplayName = Some "Living Room" }
                    { ZoneId = "hz_2"; DisplayName = None }
                ]
            }

            let back = settings |> Settings.serialize |> Settings.parse

            Expect.equal back settings "round-trip preserves zones and optional display name"
        }

        test "zoneDisplayName returns override when set" {
            let settings : Settings = {
                Entities = []
                Zones = [ { ZoneId = "hz_1"; DisplayName = Some "Living Room" } ]
            }

            Expect.equal (Settings.zoneDisplayName settings "hz_1") (Some "Living Room") "returns display name"
            Expect.equal (Settings.zoneDisplayName settings "hz_99") None "unknown zone returns None"
        }

        test "round-trips entity id override" {
            let settings : Settings = {
                Entities = [
                    { DeviceId = "hdm_x_1"; Visible = true; DisplayName = None; IdOverride = Some "my_light" }
                    { DeviceId = "hdm_x_2"; Visible = true; DisplayName = None; IdOverride = None }
                ]
                Zones = []
            }

            let back = settings |> Settings.serialize |> Settings.parse

            Expect.equal back settings "id override survives round-trip"
        }

        test "parses old settings without Zones property" {
            let json = """{"Entities":[{"DeviceId":"hdm_x_1","Visible":true}]}"""
            match Settings.tryParse json with
            | Ok parsed ->
                Expect.equal parsed.Zones [] "missing Zones defaults to empty"
                Expect.equal (parsed.Entities |> List.length) 1 "entity still parsed"
            | Error e -> failtest $"unexpected parse error: {e}"
        }
    ]
