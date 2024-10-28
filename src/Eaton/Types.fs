namespace MF.Eaton

open System
open MF.Utils

[<AutoOpen>]
module HttpTypes =
    type Url = Url of string
    type Api = Api of string
    type Path = Api -> Url

    [<RequireQualifiedAccess>]
    module Api =
        let create (api: string) =
            if api.StartsWith "http" then api
            else $"http://{api}"
            |> Api

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

type Sensor =
    | AnalogSensor

type Actuator =
    | ShutterActuator
    | SwitchActuator
    | DimmerActuator
    | HeatingActuator

[<RequireQualifiedAccess>]
type ThermostatSubType =
    | RoomController
    | HumiditySensor
    | TemperatureSensor
    | Adjustment

type Zone = {
    Id: ZoneId
    Name: string
    Devices: DeviceInZone list
}

and ZoneId = ZoneId of string
and DeviceInZone = {
    DeviceId: DeviceId
    Name: string
}

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

    Zone: ZoneId option
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
    | Sensor of Sensor
    | Actuator of Actuator
    | Thermostat of ThermostatSubType
    | PushButton
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

type Scene = {
    Id: SceneId
    Zone: ZoneId
    Name: string
}

and SceneId = SceneId of string

[<RequireQualifiedAccess>]
module SceneId =
    let value (SceneId value) = value

[<RequireQualifiedAccess>]
module ZoneId =
    let value (ZoneId value) = value

[<RequireQualifiedAccess>]
module Rssi =
    let tryParse = function
        | Some 1, Some status
        | Some 2, Some status
        | Some 3, Some status
            -> Rssi.Enabled status |> Some
        | Some 0, _ -> Rssi.Disabled |> Some
        | _ -> None

    let parse rssi =
        rssi
        |> tryParse
        |> Option.defaultValue (Rssi.Other rssi)

[<RequireQualifiedAccess>]
module Powered =
    let tryParse = function
        | Some 6, String.OptionContains "Sítově Napájené" -> PowerStatus.Always |> Some
        | Some 1, Some status -> PowerStatus.Battery status |> Some
        | _ -> None

    let parse powered =
        powered
        |> tryParse
        |> Option.defaultValue (PowerStatus.Other powered)

[<RequireQualifiedAccess>]
module Name =
    let asUniqueKey name =
        name
        |> String.remove [ "["; "]"; "("; ")" ]
        |> String.replaceAll ["ě"; "é"] "e"
        |> String.replace "š" "s"
        |> String.replace "č" "c"
        |> String.replace "ř" "r"
        |> String.replace "ž" "z"
        |> String.replace "ý" "y"
        |> String.replace "á" "a"
        |> String.replace "í" "i"
        |> String.replace " " "_"
        |> String.lower
        |> sprintf "eaton_%s"

[<AutoOpen>]
module ShortId =
    // there could be 2 deviceIds, so we need to normalize it (at least now)
    // xCo:9214125_u0 –> storing device state (shortId)
    // hdm:xComfort Adapter:9214125_u0 -> loading device state
    type ShortDeviceId = private ShortDeviceId of string

    [<RequireQualifiedAccess>]
    module ShortDeviceId =
        let fromDeviceId (DeviceId deviceId) =
            deviceId
            |> String.split ":"
            |> List.last
            |> sprintf "xCo:%s"
            |> ShortDeviceId

        let value (ShortDeviceId value) = value

[<RequireQualifiedAccess>]
module DeviceId =
    let contains (part: string) (DeviceId device) =
        device.Contains part

    let value (DeviceId device) = device

    let id (DeviceId device) =
        device
        |> String.replaceAll [ " "; ":" ] "_"

    let shortId = ShortDeviceId.fromDeviceId

[<RequireQualifiedAccess>]
module DeviceType =
    let valueType = function
        | Actuator HeatingActuator
        | Thermostat ThermostatSubType.Adjustment
        | Thermostat ThermostatSubType.TemperatureSensor -> "temperature"
        | Thermostat ThermostatSubType.HumiditySensor -> "humidity"
        | _ -> "value"

    let unitOfMeasure = function
        | Actuator HeatingActuator
        | Thermostat ThermostatSubType.TemperatureSensor
        | Thermostat ThermostatSubType.Adjustment -> Some "°C"

        | Thermostat ThermostatSubType.HumiditySensor -> Some "%"

        | _ -> None

    let parseMainType = function
        | String.OptionContains "Room Controller" ->
            Thermostat ThermostatSubType.RoomController

        | Some (String.Contains "Actuator" as deviceType) ->
            match deviceType with
            | String.Contains "Dimmer" -> Actuator DimmerActuator
            | String.Contains "Shutter" -> Actuator ShutterActuator
            | String.Contains "Switch" -> Actuator SwitchActuator
            | String.Contains "Heating" -> Actuator HeatingActuator
            | other -> Other (Some other)

        | Some (String.Contains "Sensor" as deviceType) ->
            match deviceType with
            | String.Contains "Analog" -> Sensor AnalogSensor
            | other -> Other (Some other)

        | Some (String.Contains "Push-Button") -> PushButton


        | other -> Other other

    let parseChildType (DeviceId deviceId) = function
        | Thermostat ThermostatSubType.RoomController, _ ->
            match deviceId with
            | String.EndsWith "u0" -> Thermostat ThermostatSubType.TemperatureSensor
            | String.EndsWith "u1" -> Thermostat ThermostatSubType.HumiditySensor
            | String.EndsWith "w" -> Thermostat ThermostatSubType.Adjustment
            | _ -> Thermostat ThermostatSubType.Adjustment

        | parentType, _ -> parentType

[<RequireQualifiedAccess>]
module Device =
    let isSensor = function
        | { Type = Actuator HeatingActuator }
        | { Type = Thermostat _ }
        | { Type = Sensor _ } -> true
        | _ -> false

    let isSwitch = function
        | { Type = Actuator HeatingActuator } -> false
        | { Type = Actuator _ }
        | { Type = PushButton }  -> true
        | _ -> false

[<RequireQualifiedAccess>]
module DeviceStat =
    let value = function
        | On -> "true"
        | Off -> "false"
        | Decimal value -> string value
        | String value -> value
