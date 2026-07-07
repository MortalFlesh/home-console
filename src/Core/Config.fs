namespace MF.HomeConsole

open MF.Eaton

type DataConfig = {
    Directory: string
}

type Config = {
    Eaton: EatonConfig
    Data: DataConfig
}

type EntitySetting = {
    DeviceId: string
    Visible: bool
    DisplayName: string option
}

type Settings = {
    Entities: EntitySetting list
}

[<RequireQualifiedAccess>]
module Settings =
    open FSharp.Data

    type private SettingsSchema = JsonProvider<"schema/settings.json">

    let empty : Settings = { Entities = [] }

    let tryParse (json: string) : Result<Settings, string> =
        try
            let parsed = SettingsSchema.Parse json
            Ok {
                Entities =
                    parsed.Entities
                    |> Array.map (fun e -> {
                        DeviceId = e.DeviceId
                        Visible = e.Visible
                        DisplayName = e.DisplayName
                    })
                    |> List.ofArray
            }
        with e -> Error e.Message

    let parse (json: string) : Settings =
        match tryParse json with
        | Ok settings -> settings
        | Error _ -> empty

    let serialize (settings: Settings) : string =
        let entity (e: EntitySetting) =
            [
                yield "DeviceId", JsonValue.String e.DeviceId
                yield "Visible", JsonValue.Boolean e.Visible
                match e.DisplayName with
                | Some name -> yield "DisplayName", JsonValue.String name
                | None -> ()
            ]
            |> Array.ofList
            |> JsonValue.Record

        JsonValue.Record [| "Entities", settings.Entities |> List.map entity |> Array.ofList |> JsonValue.Array |]
        |> fun json -> json.ToString()

    let applyToDevices (settings: Settings) (devices: Device list) : Device list =
        let settingsMap =
            settings.Entities
            |> List.map (fun e -> e.DeviceId, e)
            |> Map.ofList

        let applyDisplayName (device: Device) =
            let id = device.DeviceId |> DeviceId.id
            let displayName =
                settingsMap
                |> Map.tryFind id
                |> Option.bind (fun e -> e.DisplayName)
                |> Option.defaultValue device.DisplayName
            { device with DisplayName = displayName }

        let isEntityVisible id =
            settingsMap
            |> Map.tryFind id
            |> Option.map (fun e -> e.Visible)
            |> Option.defaultValue true

        devices
        |> List.choose (fun device ->
            let parentId = device.DeviceId |> DeviceId.id
            if not (isEntityVisible parentId) then None
            else
                let children =
                    device.Children
                    |> List.filter (fun child -> child.DeviceId |> DeviceId.id |> isEntityVisible)
                    |> List.map applyDisplayName

                if device.Children |> List.isEmpty |> not && children |> List.isEmpty then None
                else Some { (applyDisplayName device) with Children = children }
        )

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
