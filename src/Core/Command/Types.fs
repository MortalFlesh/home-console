namespace MF.HomeConsole

[<RequireQualifiedAccess>]
type CommandError =
    | Exception of exn
    | Message of string
    | Errors of CommandError list

[<RequireQualifiedAccess>]
module CommandError =
    let rec ofEatonApiError: MF.Eaton.ApiError -> CommandError = function
        | MF.Eaton.ApiError.Exception e -> CommandError.Exception e
        | MF.Eaton.ApiError.Message e -> CommandError.Message e
        | MF.Eaton.ApiError.Errors e -> CommandError.Errors (e |> List.map ofEatonApiError)
