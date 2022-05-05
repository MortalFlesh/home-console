open System
open System.IO
open MF.ConsoleApplication
open MF.HomeConsole
open MF.HomeConsole.Console

[<EntryPoint>]
let main argv =
    consoleApplication {
        title AssemblyVersionInformation.AssemblyProduct
        info ApplicationInfo.MainTitle
        version AssemblyVersionInformation.AssemblyVersion

        (* command "calendar:show:events" {
            Description = "Show events from a office365 calendar."
            Help = None
            Arguments = []
            Options = []
            Initialize = None
            Interact = None
            Execute = fun (input, output) ->
                Calendar.show output

                output.Success "Done"
                ExitCode.Success
        } *)

        command "about" {
            Description = "Displays information about the current project."
            Help = None
            Arguments = []
            Options = []
            Initialize = None
            Interact = None
            Execute = fun (_input, output) ->
                let ``---`` = [ "------------------"; "----------------------------------------------------------------------------------------------" ]

                output.Table [ AssemblyVersionInformation.AssemblyProduct ] [
                    [ "Description"; AssemblyVersionInformation.AssemblyDescription ]
                    [ "Version"; AssemblyVersionInformation.AssemblyVersion ]

                    ``---``
                    [ "Environment" ]
                    ``---``
                    [ ".NET Core"; Environment.Version |> sprintf "%A" ]
                    [ "Command Line"; Environment.CommandLine ]
                    [ "Current Directory"; Environment.CurrentDirectory ]
                    [ "Machine Name"; Environment.MachineName ]
                    [ "OS Version"; Environment.OSVersion |> sprintf "%A" ]
                    [ "Processor Count"; Environment.ProcessorCount |> sprintf "%A" ]

                    ``---``
                    [ "Git" ]
                    ``---``
                    [ "Branch"; AssemblyVersionInformation.AssemblyMetadata_gitbranch ]
                    [ "Commit"; AssemblyVersionInformation.AssemblyMetadata_gitcommit ]
                ]

                ExitCode.Success
        }
    }
    |> run argv
