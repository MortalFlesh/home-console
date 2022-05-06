namespace MF.Eaton

type EatonConfig = {
    Host: string
    Credentials: Credentials
}

and Credentials = {
    Name: string
    Password: string
}

[<RequireQualifiedAccess>]
type ApiError =
    | Exception of exn
    | Message of string
    | Errors of ApiError list
