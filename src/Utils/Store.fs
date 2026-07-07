namespace MF.Utils

[<RequireQualifiedAccess>]
module Store =
    open System.IO

    let private lockObj = obj ()

    /// Read a file's text content.
    /// Returns None when the file is missing or cannot be read.
    let tryLoadText (path: string) : string option =
        lock lockObj (fun () ->
            if path |> File.Exists |> not then None
            else
                try Some (File.ReadAllText path)
                with _ -> None
        )

    /// Write text to a file atomically (via a .tmp file).
    /// Returns Error on any I/O failure.
    let saveText (path: string) (content: string) : Async<Result<unit, exn>> =
        async {
            return
                try
                    lock lockObj (fun () ->
                        let tmp = path + ".tmp"
                        path |> Path.GetDirectoryName |> Directory.ensure
                        File.WriteAllText(tmp, content)
                        File.Move(tmp, path, overwrite = true)
                    )
                    Ok ()
                with e -> Error e
        }
