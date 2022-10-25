open System
open MF.ConsoleApplication
open MF.HomeConsole

[<EntryPoint>]
let main argv =
    consoleApplication {
        title AssemblyVersionInformation.AssemblyProduct
        info ApplicationInfo.MainTitle
        version AssemblyVersionInformation.AssemblyVersion
        description AssemblyVersionInformation.AssemblyDescription
        meta ("BuildAt", AssemblyVersionInformation.AssemblyMetadata_createdAt)

        gitBranch AssemblyVersionInformation.AssemblyMetadata_gitbranch
        gitCommit AssemblyVersionInformation.AssemblyMetadata_gitcommit

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

        command "home:web:run" {
            Description = "Run a webserver which expose sensor data on <c:yellow>http://0.0.0.0:8080/sensors</c> as a json."
            Help = None
            Arguments = RunWebServerCommand.arguments
            Options = RunWebServerCommand.options
            Initialize = None
            Interact = None
            Execute = RunWebServerCommand.execute
        }
    }
    |> run argv
