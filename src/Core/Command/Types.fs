namespace MF.HomeConsole

open Feather.ConsoleApplication

[<RequireQualifiedAccess>]
module CommandError =
    let rec ofEatonApiError: MF.Eaton.ApiError -> CommandError = function
        | MF.Eaton.ApiError.Exception e -> CommandError.Exception e
        | MF.Eaton.ApiError.Message e -> CommandError.Message e
        | MF.Eaton.ApiError.Errors e -> CommandError.Errors (e |> List.map ofEatonApiError)

[<AutoOpen>]
module Execute =
    open Feather.ErrorHandling.AsyncResult.Operators

    let executeAsyncResult execute =
        ExecuteAsyncResult (execute >@> ConsoleApplicationError.CommandError)

    open Feather.ErrorHandling.Result.Operators

    let executeResult execute =
        ExecuteResult (execute >@> ConsoleApplicationError.CommandError)
