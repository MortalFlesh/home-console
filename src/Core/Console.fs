namespace MF.HomeConsole

module Console =
    open MF.ConsoleApplication

    let commandHelp lines = lines |> String.concat "\n\n" |> Some

    /// Concat two lines into one line for command help, so they won't be separated by other empty line
    let inline (<+>) line1 line2 = sprintf "%s\n%s" line1 line2

    (* [<RequireQualifiedAccess>]
    module Argument =
        let repositories = Argument.requiredArray "repositories" "Path to dir containing repositories." *)

    [<RequireQualifiedAccess>]
    module Option =
        let config = Option.required "config" (Some "c") "File where the configuration is." (Some ".config.json")

    (* [<RequireQualifiedAccess>]
    module Input =
        let getRepositories = Input.getArgumentValueAsList "repositories" *)
