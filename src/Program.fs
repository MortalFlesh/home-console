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

        command "home:send" {
            Description = "Send data to storage."
            Help = None
            Arguments = SendDevicesDataCommand.arguments
            Options = SendDevicesDataCommand.options
            Initialize = None
            Interact = None
            Execute = SendDevicesDataCommand.execute
        }

        command "home:download:history" {
            Description = "Download current data from EATON app."
            Help = None
            Arguments = DownloadEatonHistory.arguments
            Options = DownloadEatonHistory.options
            Initialize = None
            Interact = None
            Execute = DownloadEatonHistory.execute
        }

        command "home:download:devices" {
            Description = "Download current devices list from EATON app."
            Help = None
            Arguments = DownloadEatonDeviceList.arguments
            Options = DownloadEatonDeviceList.options
            Initialize = None
            Interact = None
            Execute = DownloadEatonDeviceList.execute
        }

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
