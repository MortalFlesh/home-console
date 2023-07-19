namespace MF.Eaton

open System

[<AutoOpen>]
module internal Utils =
    let parseInt (string: string) =
        match string |> Int32.TryParse with
        | true, value -> Some value
        | _ -> None

    [<RequireQualifiedAccess>]
    module String =
        let (|EndsWith|_|): string -> string -> _ = fun part -> function
            | s when s.EndsWith part -> Some ()
            | _ -> None

        let (|Contains|_|): string -> string -> _ = fun part -> function
            | s when s.Contains part -> Some ()
            | _ -> None

        let (|OptionContains|_|): string -> string option -> _ = fun part -> function
            | Some (Contains part) -> Some ()
            | _ -> None
