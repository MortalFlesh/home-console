namespace MF.HomeConsole

open MF.Storage

type Config = {
    GoogleSheets: GoogleSheets.Config
}

[<RequireQualifiedAccess>]
module Config =
    open System.IO
    open FSharp.Data

    type private ConfigSchema = JsonProvider<"schema/config.json", SampleIsList = true>

    let parse = function
        | notFound when notFound |> File.Exists |> not -> None
        | file ->
            let parsed =
                file
                |> File.ReadAllText
                |> ConfigSchema.Parse

            Some {
                GoogleSheets = {
                    ApplicationName = parsed.GoogleSheets.ApplicationName
                    Credentials = parsed.GoogleSheets.Credentials
                    Token = parsed.GoogleSheets.Token
                    SpreadsheetId = parsed.GoogleSheets.SpreadsheetId
                }
            }
