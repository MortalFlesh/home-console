namespace MF.Utils

[<RequireQualifiedAccess>]
module Store =
    open System.IO
    open System.Text.Json

    let private lockObj = obj ()

    /// Load a JSON file and deserialise it to 'T.
    /// Returns None when the file is missing or the content is corrupt.
    let tryLoad<'T> (path: string) : 'T option =
        lock lockObj (fun () ->
            if path |> File.Exists |> not then None
            else
                try
                    use stream = File.OpenRead(path)
                    JsonSerializer.Deserialize<'T>(stream) |> Some
                with _ -> None
        )

    /// Serialise 'T to JSON and write it atomically (via a .tmp file).
    /// Returns Error on any I/O or serialisation failure.
    let save<'T> (path: string) (value: 'T) : Async<Result<unit, exn>> =
        async {
            return
                try
                    lock lockObj (fun () ->
                        let tmp = path + ".tmp"
                        path |> Path.GetDirectoryName |> Directory.ensure
                        use stream = File.Create(tmp)
                        JsonSerializer.Serialize(stream, value)
                        stream.Flush()
                        stream.Dispose()
                        File.Move(tmp, path, overwrite = true)
                    )
                    Ok ()
                with e -> Error e
        }
