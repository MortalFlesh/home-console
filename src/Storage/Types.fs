namespace MF.Storage

type Key = Key of string

type TabName = TabName of string

type UpdateFloatData = {
    SpreadsheetId: string
    ListName: TabName
    Data: (string * float list list) list //Map<string, float list list>
}

type UpdateStringData = {
    SpreadsheetId: string
    ListName: TabName
    Data: (string * string list list) list //Map<string, string list list>
}

[<RequireQualifiedAccess>]
type UpdateData =
    | Float of UpdateFloatData
    | String of UpdateStringData
