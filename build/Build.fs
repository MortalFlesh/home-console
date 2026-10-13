open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "Home Console"
            Summary = "Console application to help with home automations."
            Git = Git.init ()
        }
        Specs = Spec.defaultConsoleApplication [
            RaspberryPiHassioAddon
            OSX
            // ArmLinux
            // AlpineLinux
            Windows
        ]
    }

    args |> Args.run
