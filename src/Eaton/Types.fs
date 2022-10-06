namespace MF.Eaton

open System

[<AutoOpen>]
module HttpTypes =
    type Url = Url of string
    type Api = Api of string
    type Path = Api -> Url

type EatonConfig = {
    Host: Api
    Credentials: Credentials
    History: HistoryConfig
}

and Credentials = {
    Name: string
    Password: string
    Path: string
}

and HistoryConfig = {
    DownloadDirectory: string
}

[<RequireQualifiedAccess>]
type ApiError =
    | Exception of exn
    | Message of string
    | Errors of ApiError list

type DeviceId = DeviceId of string

type Device = {
    Name: string
    DisplayName: string

    DeviceId: DeviceId
    Parent: DeviceId option
    SerialNumber: int
    Type: DeviceType

    Rssi: Rssi
    Powered: PowerStatus

    Children: Device list
}

and [<RequireQualifiedAccess>] Rssi =
    | Disabled
    | Enabled of status: string
    | Other of id: int option * status: string option

and [<RequireQualifiedAccess>] PowerStatus =
    | Battery of status: string
    | Always // sitove napajeni
    | Other of id: int option * status: string option

and DeviceType =
    | PushButton
    | ShutterActuator
    | HumiditySensor
    | Termostat
    | AnalogSensor
    | SwitchActuator
    | DimmerActuator
    | Other of string option

type DeviceStat = {
    MessagesPerDay: int
    LastMsgTimeStamp: TimeSpan
    EventLog: StatValue
}

and StatValue =
    | On
    | Off
    | Decimal of decimal
    | String of string

type History = {
    Device: Device
    Data: string list
}

[<RequireQualifiedAccess>]
module DeviceId =
    let contains (part: string) (DeviceId device) =
        device.Contains part

    let id (DeviceId device) =
        device
            .Replace(" ", "_")
            .Replace(":", "_")

[<RequireQualifiedAccess>]
module DeviceStat =
    let value = function
        | On -> "true"
        | Off -> "false"
        | Decimal value -> string value
        | String value -> value
