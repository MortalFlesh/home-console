namespace MF.Eaton

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

type Device = {
    Name: string
    DisplayName: string
    SerialNumber: int
    Type: DeviceType
}

and DeviceType =
    | PushButton
    | ShutterActuator
    | HumiditySensor
    | Termostat
    | AnalogSensor
    | SwitchActuator
    | DimmerActuator

type History = {
    Device: Device
    Data: string list
}
