namespace ProjectBuild

module internal Utils =
    open System
    open System.IO

    open Fake.Core
    open Fake.DotNet
    open Fake.IO
    open Fake.IO.FileSystemOperators
    open Fake.IO.Globbing.Operators
    open Fake.Core.TargetOperators
    open Fake.Tools.Git

    [<RequireQualifiedAccess>]
    module Args =
        let init args =
            args
            |> Array.toList
            |> Context.FakeExecutionContext.Create false "build.fsx"
            |> Context.RuntimeContext.Fake
            |> Context.setExecutionContext

        let run args =
            match args with
            | [| "-t"; target |] -> Target.runOrDefault target
            | [| target |] -> Target.runOrDefaultWithArguments target
            | _ -> Target.runOrDefaultWithArguments "Build"

            0 // return an integer exit code

    let tee f a =
        f a
        a

    let skipOn option action p =
        if p.Context.Arguments |> Seq.contains option
        then Trace.tracefn "Skipped ..."
        else action p

    let createProcess exe arg dir =
        CreateProcess.fromRawCommandLine exe arg
        |> CreateProcess.withWorkingDirectory dir
        |> CreateProcess.ensureExitCode

    let run proc arg dir =
        proc arg dir
        |> Proc.run
        |> ignore

    let orFail = function
        | Error e -> raise e
        | Ok ok -> ok

    let stringToOption = function
        | null | "" -> None
        | string -> Some string

    let envVar name =
        if Environment.hasEnvironVar(name)
            then Environment.environVar(name) |> Some
            else None

    [<RequireQualifiedAccess>]
    module Option =
        let mapNone f = function
            | Some v -> v
            | None -> f None

        let bindNone f = function
            | Some v -> Some v
            | None -> f None

    [<RequireQualifiedAccess>]
    module Dotnet =
        let dotnet = createProcess "dotnet"

        let run command dir = try run dotnet command dir |> Ok with e -> Error e
        let runInRoot command = run command "."
        let runOrFail command dir = run command dir |> orFail
        let runInRootOrFail command = run command "." |> orFail

    [<AutoOpen>]
    module ProjectDefinition =
        type ProjectSources =
            abstract member Sources: IGlobbingPattern
            abstract member Tests: IGlobbingPattern
            abstract member All: IGlobbingPattern

        type ProjectDefinition =
            {
                Project: ProjectMeta
                Specs: ProjectSpec
            }

            with
                member this.Sources =
                    match this with
                    | { Specs = Library app } -> app :> ProjectSources
                    | { Specs = Executable app } -> app  :> ProjectSources
                    | { Specs = ConsoleApplication app } -> app :> ProjectSources
                    | { Specs = SAFEStackApplication app } -> app  :> ProjectSources

                member this.ChangeLog =
                    match this with
                    | { Specs = Library { Changelog = changeLog } } -> Some changeLog
                    | { Specs = Executable { Changelog = changeLog } }
                    | { Specs = ConsoleApplication { Changelog = changeLog } }
                    | { Specs = SAFEStackApplication { Changelog = changeLog } } -> changeLog

        and ProjectMeta = {
            Name: string
            Summary: string
            Git: Git option
        }

        and Git = {
            Commit: string
            Branch: string
            Repository: string option
        }

        and ProjectSpec =
            | Library of LibrarySpec
            | Executable of ExecutableSpec
            | ConsoleApplication of ConsoleApplicationSpec
            | SAFEStackApplication of SAFEStackApplicationSpec

            with
                member this.Type =
                    match this with
                    | Library _ -> "Library"
                    | Executable _ -> "Executable"
                    | ConsoleApplication _ -> "Console Application"
                    | SAFEStackApplication _ -> "SAFE-Stack Application"

                member this.IsSAFEStack =
                    match this with
                    | SAFEStackApplication _ -> true
                    | _ -> false

        and LibrarySpec =
            {
                Changelog: string
                ReleaseDir: string
                LibrarySources: IGlobbingPattern
                TestsSources: IGlobbingPattern
                AllSources: IGlobbingPattern
            }

            interface ProjectSources with
                member this.Sources = this.LibrarySources
                member this.Tests = this.TestsSources
                member this.All = this.AllSources

        and ExecutableSpec =
            {
                Changelog: string option
                ReleaseDir: string
                ApplicationSources: IGlobbingPattern
                TestsSources: IGlobbingPattern
                AllSources: IGlobbingPattern
            }

            interface ProjectSources with
                member this.Sources = this.ApplicationSources
                member this.Tests = this.TestsSources
                member this.All = this.AllSources

        and ConsoleApplicationSpec =
            {
                Changelog: string option
                ReleaseDir: string
                RuntimeIds: RuntimeId list
                ReleaseSource: string
                ApplicationSources: IGlobbingPattern
                TestsSources: IGlobbingPattern
                AllSources: IGlobbingPattern
            }

            interface ProjectSources with
                member this.Sources = this.ApplicationSources
                member this.Tests = this.TestsSources
                member this.All = this.AllSources

        and SAFEStackApplicationSpec =
            {
                Changelog: string option
                TemplateVersion: string

                SharedPath: string
                ServerPath: string
                ClientPath: string

                DeployPath: string

                SharedTestsPath: string
                ServerTestsPath: string
                ClientTestsPath: string

                ReleaseSources: IGlobbingPattern
                TestsSources: IGlobbingPattern
                AllSources: IGlobbingPattern
            }

            interface ProjectSources with
                member this.Sources = this.ReleaseSources
                member this.Tests = this.TestsSources
                member this.All = this.AllSources

        and RuntimeId =
            | OSX
            | Windows
            | Linux
            | ArmLinux
            | AlpineLinux
            | RaspberryPiHassioAddon
            | Other of string

        [<RequireQualifiedAccess>]
        module Git =
            let init () =
                Some {
                    Commit = Information.getCurrentSHA1(".")
                    Branch = Information.getBranchName(".")
                    Repository = None
                }

        [<RequireQualifiedAccess>]
        module Spec =
            let defaultLibrary: ProjectSpec =
                let sources =
                    !! "./*.fsproj"
                    ++ "src/*.fsproj"
                    ++ "src/**/*.fsproj"

                Library {
                    Changelog = "CHANGELOG.md"
                    ReleaseDir = "release"
                    LibrarySources = sources
                    TestsSources = !! "tests/*.fsproj"
                    AllSources =
                        sources
                        ++ "tests/*.fsproj"
                        ++ "build/*.fsproj"
                }

            let defaultExecutable: ProjectSpec =
                let release =
                    !! "./*.fsproj"
                    ++ "src/**/*.fsproj"

                Executable {
                    Changelog = if File.Exists "CHANGELOG.md" then Some "CHANGELOG.md" else None
                    ReleaseDir = "/app"

                    ApplicationSources = release
                    TestsSources = !! "tests/**/*.fsproj"
                    AllSources =
                        release
                        ++ "tests/**/*.fsproj"
                        ++ "build/*.fsproj"
                }

            let defaultConsoleApplication runtimeIds: ProjectSpec =
                let sources =
                    !! "./*.fsproj"
                    ++ "src/*.fsproj"
                    ++ "src/**/*.fsproj"

                ConsoleApplication {
                    Changelog = if File.Exists "CHANGELOG.md" then Some "CHANGELOG.md" else None
                    ReleaseDir = "./dist"
                    RuntimeIds = runtimeIds

                    ApplicationSources = sources
                    ReleaseSource = sources |> Seq.head
                    TestsSources = !! "tests/*.fsproj"
                    AllSources =
                        sources
                        ++ "tests/*.fsproj"
                        ++ "build/*.fsproj"
                }

            let defaultSAFEStackApplication templateVersion: ProjectSpec =
                let release = !! "src/**/*.fsproj"

                SAFEStackApplication {
                    Changelog = if File.Exists "CHANGELOG.md" then Some "CHANGELOG.md" else None
                    TemplateVersion = templateVersion

                    SharedPath = Path.getFullName ("src" </> "Shared")
                    ServerPath = Path.getFullName ("src" </> "Server")
                    ClientPath = Path.getFullName ("src" </> "Client")

                    DeployPath = Path.getFullName "deploy"

                    SharedTestsPath = Path.getFullName ("tests" </> "Shared")
                    ServerTestsPath = Path.getFullName ("tests" </> "Server")
                    ClientTestsPath = Path.getFullName ("tests" </> "Client")

                    ReleaseSources = release
                    TestsSources = !! "tests/**/*.fsproj"
                    AllSources = release ++ "tests/**/*.fsproj"
                }

        [<RequireQualifiedAccess>]
        module RuntimeId =
            /// Runtime IDs: https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#macos-rids
            let value = function
                | OSX -> "osx-x64"
                | Windows -> "win-x64"
                | Linux -> "linux-x64"
                | ArmLinux -> "linux-arm64"
                | AlpineLinux -> "linux-musl-x64"
                | RaspberryPiHassioAddon -> "alpine.3.16-arm64"
                | Other other -> other
