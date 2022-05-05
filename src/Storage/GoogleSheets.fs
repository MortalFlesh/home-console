namespace MF.Storage

[<RequireQualifiedAccess>]
module GoogleSheets =
    open System
    open System.Collections.Generic
    open System.IO
    open System.Threading

    open Google.Apis.Auth.OAuth2
    open Google.Apis.Sheets.v4
    open Google.Apis.Sheets.v4.Data
    open Google.Apis.Services
    open Google.Apis.Util.Store

    open MF.ErrorHandling

    type Config = {
        ApplicationName: string
        Credentials: string
        Token: string
        SpreadsheetId: string
    }

    let private letters =
        let baseLetters = [ "A"; "B"; "C"; "D"; "E"; "F"; "G"; "H"; "I"; "J"; "K"; "L"; "M"; "N"; "O"; "P"; "Q"; "R"; "S"; "T"; "U"; "V"; "W"; "X"; "Y"; "Z" ]

        [
            yield! baseLetters
            yield! baseLetters |> List.map ((+) "A")
        ]

    let letter i =
        if i > letters.Length then failwithf "[Sheets] Letter index %A is out of bound." i
        letters.[i]

    let letterNumber letter =
        match letters |> List.tryFindIndex ((=) letter) with
        | Some i -> i
        | _ -> failwithf "[Sheets] Letter %A is out of bound." letter

    let letterMoveBy length i (cellLetter: string) =
        if i <= 0 then cellLetter
        else
            let letterNumber = cellLetter |> letterNumber
            letterNumber + i * length |> letter

    let rangeMoveBy length i (range: string) =
        if i <= 0 then range
        else
            let letter = string range.[0] |> letterMoveBy length i
            let number = int range.[1..]
            sprintf "%s%d" letter number

    let private createClient config: AsyncResult<SheetsService, exn> = asyncResult {
        let scopes = [ SheetsService.Scope.Spreadsheets ]

        use stream = File.OpenRead(config.Credentials)
        let! (secrets: GoogleClientSecrets) = GoogleClientSecrets.FromStreamAsync(stream)

        let! (credentials: UserCredential) =
            GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets.Secrets,
                scopes,
                "user",
                CancellationToken.None,
                FileDataStore(config.Token, true)
            )

        let! _ = credentials.RefreshTokenAsync(CancellationToken.None)

        return new SheetsService(
            BaseClientService.Initializer(
                HttpClientInitializer = credentials,
                ApplicationName = config.ApplicationName
            )
        )
    }

    let private range (TabName tab) fromCell toCell =
        sprintf "%s!%s:%s" tab fromCell toCell

    let private range2 tab (fromLetter, fromNumber) (toLetter, toNumber) =
        range tab (sprintf "%s%d" fromLetter fromNumber) (sprintf "%s%d" toLetter toNumber)

    /// Helper function to convert F# list to C# List
    let private data<'a> (values: 'a list): List<'a> =
        values |> ResizeArray<'a>

    /// Helper function to convert F# list to C# IList
    let private idata<'a> (values: 'a list): IList<'a> =
        values |> data :> IList<'a>

    let private valuesRangeStatic tab fromCell toCell (values: _ list list) =
        let toObj a = a :> obj

        let values =
            values
            |> List.map (List.map toObj >> idata)
            |> data

        ValueRange (
            Range = range tab fromCell toCell,
            Values = values
        )

    let private valuesRange tab (startLetter, startNumber) (values: _ list list) =
        let toLetter =
            let toLetterIndex =
                values
                |> List.map List.length
                |> List.sortDescending
                |> List.head

            letters.[toLetterIndex - 1]

        let toNumber = startNumber + values.Length - 1

        values
        |> valuesRangeStatic tab (sprintf "%s%d" startLetter startNumber) (sprintf "%s%d" toLetter toNumber)

    /// From "D2:D27" to ("D", 2)
    let private rangeStartFromString (range: string) =
        let range =
            match range.Split ":" with
            | [| range; _  |] -> range
            | _ -> failwithf "Invalid format of range %A" range

        let letter = range.[0] |> string
        let number = range.[1..] |> int

        letter, number

    let private batchUpdateSheets (client: SheetsService) spreadsheetId (valuesRange: ValueRange) = asyncResult {
        let requestBody =
            BatchUpdateValuesRequest(
                ValueInputOption = "USER_ENTERED",
                Data = data [ valuesRange ]
            )

        let request = client.Spreadsheets.Values.BatchUpdate(requestBody, spreadsheetId)
        let! _ = request.ExecuteAsync()

        return ()
    }

    let private saveItems config (serialize: _ -> string) tabName = function
        | [] -> AsyncResult.retn ()
        | items ->
            asyncResult {
                let valuesRange =
                    items
                    |> List.choose (fun item ->
                        let row = serialize item

                        match row.Split ";" |> Seq.toList with
                        | [] -> None
                        | values -> Some values
                    )
                    |> valuesRange tabName ("A", 2)

                use! service = createClient config

                do! valuesRange |> batchUpdateSheets service config.SpreadsheetId
            }

    let private loadItems config parse tab () = asyncResult {
        use! service = createClient config

        let getValues (service: SheetsService) = asyncResult {
            let request = service.Spreadsheets.Values.Get(config.SpreadsheetId, range tab "A2" "M100")
            let! (response: ValueRange) = request.ExecuteAsync()

            return response.Values
        }

        let! values = service |> getValues

        return
            if values |> Seq.isEmpty then []
            else
                values
                |> Seq.map (fun row ->
                    row
                    |> Seq.map (fun i -> i.ToString())
                    |> String.concat ";"
                )
                |> Seq.choose parse
                |> Seq.toList
    }

    let getTabs config = asyncResult {
        use! service = createClient config

        let fetchTabs (service: SheetsService) = asyncResult {
            let! (spreadsheet: Spreadsheet) = service.Spreadsheets.Get(config.SpreadsheetId).ExecuteAsync()

            return
                spreadsheet.Sheets
                |> Seq.map (fun tab -> TabName tab.Properties.Title)
                |> List.ofSeq
        }

        return! service |> fetchTabs
    }

    let createTab config (TabName tab) = asyncResult {
        use! service = createClient config

        let addTab (service: SheetsService) = asyncResult {
            printfn $"Add tab {tab} to sheet"
            let addSheet = AddSheetRequest(Properties = SheetProperties(Title = tab))

            let update = BatchUpdateSpreadsheetRequest()
            update.Requests <- data [
                Request(AddSheet = addSheet)
            ]

            printfn "Send request"
            let request = service.Spreadsheets.BatchUpdate(update, config.SpreadsheetId)
            let! _ = request.ExecuteAsync()

            return ()
        }

        do! service |> addTab
    }

    let clear config tab fromCell toCell = asyncResult {
        use! service = createClient config

        let clear (service: SheetsService) = asyncResult {
            let request = service.Spreadsheets.Values.Clear(ClearValuesRequest(), config.SpreadsheetId, range tab fromCell toCell)
            let! _ = request.ExecuteAsync()

            return ()
        }

        do! service |> clear
    }

    let updateSheets log config (updateData: UpdateData) = asyncResult {
        use! service = createClient config

        let updateSheetsByData spreadsheetId listName data =
            data
            |> List.map (fun (range: string, values) ->
                log <| sprintf "Update range %A ..." range

                let fromCell, toCell =
                    match range.Split ":" with
                    | [| cell |] -> cell, cell
                    | [| fromCell; toCell |] -> fromCell, toCell
                    | _ -> failwithf "Invalid range %A - expected \"From:To\"" range

                values
                |> valuesRangeStatic listName fromCell toCell
                |> batchUpdateSheets service spreadsheetId
            )
            |> AsyncResult.sequenceM
            |> AsyncResult.ignore

        return!
            match updateData with
            | UpdateData.String { SpreadsheetId = spreadsheetId; ListName = listName; Data = data } ->
                data
                |> updateSheetsByData spreadsheetId listName

            | UpdateData.Float { SpreadsheetId = spreadsheetId; ListName = listName; Data = data } ->
                data
                |> updateSheetsByData spreadsheetId listName
    }
