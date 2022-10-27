namespace MF.HomeConsole

module Console =
    open MF.ConsoleApplication

    (* [<RequireQualifiedAccess>]
    module Argument =
        let repositories = Argument.requiredArray "repositories" "Path to dir containing repositories." *)

    [<RequireQualifiedAccess>]
    module Option =
        let config = Option.required "config" (Some "c") "File where the configuration is." ".config.json"

    [<RequireQualifiedAccess>]
    module Input =
        // let getRepositories = Input.Argument.asList "repositories"

        let config = Input.Option.value "config"
