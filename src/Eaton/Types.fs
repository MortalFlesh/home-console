namespace MF.Eaton

type EatonConfig = {
    Host: string
    Credentials: Credentials
    History: History
}

and Credentials = {
    Name: string
    Password: string
}

and History = {
    Download: string
}

[<RequireQualifiedAccess>]
type ApiError =
    | Exception of exn
    | Message of string
    | Errors of ApiError list
