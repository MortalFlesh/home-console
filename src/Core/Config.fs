namespace MF.HomeConsole

open MF.Eaton

type DataConfig = {
    Directory: string
}

type Config = {
    Eaton: EatonConfig
    Data: DataConfig
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
                Eaton = {
                    Host = Api.create parsed.Eaton.Host
                    Credentials = {
                        Name = parsed.Eaton.Credentials.Username
                        Password = parsed.Eaton.Credentials.Password
                        Path = parsed.Eaton.Credentials.Path
                    }
                    History = {
                        DownloadDirectory = parsed.Eaton.History.Download
                    }
                }
                Data = {
                    Directory =
                        parsed.JsonValue.TryGetProperty("data")
                        |> Option.bind (fun d -> d.TryGetProperty("directory"))
                        |> Option.map (fun v -> v.AsString())
                        |> Option.defaultValue ""
                }
            }
