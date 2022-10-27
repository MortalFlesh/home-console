namespace MF.Utils

[<RequireQualifiedAccess>]
module FileSystem =
    open System.IO

    let private writeContent (writer: StreamWriter) content =
        writer.WriteLine(sprintf "%s" content)

    let writeSeqToFile (filePath: string) (data: string seq) =
        File.WriteAllLines(filePath, data)

    let writeToFile (filePath: string) data =
        File.WriteAllText(filePath, data)

    let appendToFile (filePath: string) data =
        File.AppendAllText(filePath, data)

    let readLines (filePath: string) =
        File.ReadAllLines(filePath)
        |> Seq.toList

    let readContent (filePath: string) =
        File.ReadAllText(filePath)

    let tryReadContent (filePath: string) =
        if File.Exists filePath then File.ReadAllText(filePath) |> Some
        else None

    let getAllDirs = function
        | [] -> []
        | directories -> directories |> List.collect (Directory.EnumerateDirectories >> List.ofSeq)

    let rec getAllFiles = function
        | [] -> []
        | directories -> [
            yield! directories |> Seq.collect Directory.EnumerateFiles
            yield! directories |> Seq.collect Directory.EnumerateDirectories |> List.ofSeq |> getAllFiles
        ]

[<RequireQualifiedAccess>]
module Option =
    module Operators =
        let (=>) key value = (key, value)

[<RequireQualifiedAccess>]
module String =
    let toLower (value: string) =
        value.ToLower()

    let trim (string: string) =
        string.Trim(' ')

    let ucFirst (value: string) =
        match value |> Seq.toList with
        | [] -> ""
        | first :: rest -> (string first).ToUpper() :: (rest |> List.map string) |> String.concat ""

    let split (separator: string) (value: string) =
        value.Split(separator) |> Seq.toList

    let replaceAll (replace: string list) replacement (value: string) =
        replace
        |> List.fold (fun (value: string) toRemove ->
            value.Replace(toRemove, replacement)
        ) value

    let remove toRemove = replaceAll toRemove ""

    let append suffix string =
        sprintf "%s%s" string suffix

    let trimEnd (char: char) (string: string) =
        string.TrimEnd char

    let contains (subString: string) (string: string) =
        string.Contains(subString)

    let startsWith (prefix: string) (string: string) =
        string.StartsWith(prefix)

[<RequireQualifiedAccess>]
module Directory =
    open System.IO

    let ensure (path: string) =
        if path |> Directory.Exists |> not then Directory.CreateDirectory(path) |> ignore

[<RequireQualifiedAccess>]
module Path =
    open System.IO

    let fileName = String.split "/" >> List.rev >> List.head

    let dirName path =
        let file = path |> fileName
        path.Substring(0, path.Length - file.Length)

    module Operators =
        let (/) a b = Path.Combine(a, b)

[<AutoOpen>]
module Regexp =
    open System.Text.RegularExpressions

    // http://www.fssnip.net/29/title/Regular-expression-active-pattern
    let (|Regex|_|) pattern input =
        let m = Regex.Match(input, pattern)
        if m.Success then Some (List.tail [ for g in m.Groups -> g.Value ])
        else None

[<RequireQualifiedAccess>]
module List =
    /// see https://stackoverflow.com/questions/32363848/fastest-way-to-reduce-a-list-based-on-another-list-using-f
    let filterNotIn excluding list =
        let toExclude = set excluding
        list |> List.filter (toExclude.Contains >> not)

    let filterNotInBy f excluding list =
        let toExclude = set excluding
        list |> List.filter (f >> toExclude.Contains >> not)

    let filterInBy f including list =
        let toInclude = set including
        list |> List.filter (f >> toInclude.Contains)

[<AutoOpen>]
module Utils =
    let tee f a =
        f a
        a

[<RequireQualifiedAccess>]
module Http =
    let (|BadRequest|Unauthorized|NotFound|Unknown|) (e: exn) =
        match e with
        | :? System.Net.WebException as webException when webException.Message.Contains "(400) Bad Request" -> BadRequest
        | :? System.Net.WebException as webException when webException.Message.Contains "(401) Unauthorized" -> Unauthorized
        | :? System.Net.WebException as webException when webException.Message.Contains "(404) Not Found" -> NotFound
        | e -> Unknown e

module Logging =
    open System
    open Microsoft.Extensions.Logging

    let private normalizeString (string: string) =
        string.Replace(" ", "").ToLowerInvariant()

    [<RequireQualifiedAccess>]
    module LogLevel =
        (* let fromVerbosity (verbosity: MF.ConsoleStyle.Verbosity) =
            LogLevel.Trace *)

        let parse = normalizeString >> function
            | "trace" | "vvv" -> LogLevel.Trace
            | "debug" | "vv" -> LogLevel.Debug
            | "information" | "v" | "normal" -> LogLevel.Information
            | "warning" -> LogLevel.Warning
            | "error" -> LogLevel.Error
            | "critical" -> LogLevel.Critical
            | "quiet" | "q" | _ -> LogLevel.None

    [<RequireQualifiedAccess>]
    module LoggerFactory =
        open NReco.Logging.File

        let createWithFileLogging (path: string) level =
            LoggerFactory.Create(fun builder ->
                builder
                    .SetMinimumLevel(level)
                    .AddFile(
                        path,
                        fun c ->
                            c.FormatLogFileName <- fun name -> String.Format(name, DateTime.UtcNow)
                            c.Append <- true
                            c.MinLevel <- LogLevel.Trace
                    )
                |> ignore
            )

        let createForCommand command =
            createWithFileLogging (command |> normalizeString |> sprintf "logs/log_%s_{0:yyyy}-{0:MM}-{0:dd}.log")

        let create level =
            LoggerFactory.Create(fun builder ->
                builder
                    .SetMinimumLevel(level)
                    .AddConsole(fun c -> c.LogToStandardErrorThreshold <- LogLevel.Error)
                |> ignore
            )

[<RequireQualifiedAccess>]
module Serialize =
    module private Json =
        open Newtonsoft.Json
        open Newtonsoft.Json.Serialization

        let private options () =
            JsonSerializerSettings (
                ContractResolver =
                    DefaultContractResolver (
                        NamingStrategy = SnakeCaseNamingStrategy()
                    )
            )

        let serialize obj =
            JsonConvert.SerializeObject (obj, options())

        let serializePretty obj =
            let options = options()
            options.Formatting <- Formatting.Indented

            JsonConvert.SerializeObject (obj, options)

    let toJsonPretty: obj -> string = Json.serializePretty
    let toJson: obj -> string = Json.serialize
