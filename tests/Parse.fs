module ParseTests

open System
open Expecto
open MF.Eaton
open MF.HomeConsole.WebServer
open MF.Utils
open Microsoft.AspNetCore.Http

let httpContext (data: obj): HttpContext =
    let jsonString = Text.Json.JsonSerializer.Serialize(data)

    let context = DefaultHttpContext()
    context.Request.Body <- new IO.MemoryStream(Text.Encoding.UTF8.GetBytes(jsonString))
    context

[<Tests>]
let parseTests =
    testList "Parse" [

        test "ClimateSetpoint.parse float" {
            let expected: Api.ClimateSetpoint = {
                Room = ZoneId "my_living_room"
                Temperature = decimal 23.5
            }
            let result =
                {|
                    room = "my_living_room"
                    temperature = 23.5
                |}
                |> httpContext
                |> Api.ClimateSetpoint.parse
                |> Async.RunSynchronously

            Expect.equal result (Ok expected) "parses float correctly"
        }

        test "ClimateSetpoint.parse float with zero" {
            let expected: Api.ClimateSetpoint = {
                Room = ZoneId "my_living_room"
                Temperature = decimal 23.0
            }
            let result =
                {|
                    room = "my_living_room"
                    temperature = 23.0
                |}
                |> httpContext
                |> Api.ClimateSetpoint.parse
                |> Async.RunSynchronously

            Expect.equal result (Ok expected) "parses float correctly"
        }
    ]
