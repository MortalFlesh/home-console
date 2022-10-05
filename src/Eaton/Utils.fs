namespace MF.Eaton

open System

[<AutoOpen>]
module internal Utils =
    let parseInt (string: string) =
        match string |> Int32.TryParse with
        | true, value -> Some value
        | _ -> None

    let (|Contains|_|): string -> string option -> _ = fun part -> function
        | Some s when s.Contains part -> Some Contains
        | _ -> None
