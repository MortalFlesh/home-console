namespace MF.JsonRpc

open System
open System.IO
open System.Net
open System.Xml.Serialization

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

open MF.ErrorHandling

open FSharp.Data
open Giraffe

[<RequireQualifiedAccess>]
module JsonRpc =
    let [<Literal>] Version = "2.0"

type RawJsonData = RawJsonData of JsonValue

[<RequireQualifiedAccess>]
module RawJsonData =
    let internal parameters (RawJsonData data) = data :> obj

type Method = Method of string

[<RequireQualifiedAccess>]
module Method =
    let value (Method method) = method

/// https://www.jsonrpc.org/specification#error_object
type JsonRpcErrorDto = {
    /// A Number that indicates the error type that occurred.
    Code: int

    /// A String providing a short description of the error.
    /// The message SHOULD be limited to a concise single sentence.
    Message: string

    /// A Primitive or Structured value that contains additional information about the error.
    /// This may be omitted.
    /// The value of this member is defined by the Server (e.g. detailed error information, nested errors etc.).
    Data: obj
}

type JsonRpcErrorResponseDto = {
    Error: JsonRpcErrorDto
}

[<RequireQualifiedAccess>]
module JsonRpcErrorDto =
    let notFound request = {
        Code = 404
        Message = "Not found"
        Data = request
    }

    let private jsonRpcError (code, message) request = {
        Code = code
        Message = message
        Data = request
    }

    let parseError request = jsonRpcError (-32700, "Parse Error") request
    let invalidRequest request = jsonRpcError (-32600, "Invalid Request") request
    let methodNotFound request = jsonRpcError (-32601, "Method not found") request
    let invalidParams request = jsonRpcError (-32602, "Invalid params") request
    let internalError request = jsonRpcError (-32603, "Internal error") request

[<RequireQualifiedAccess>]
module JsonRpcErrorResponseDto =
    let ofError error = {
        Error = error
    }

//
// Request
//

/// https://www.jsonrpc.org/specification#request_object
type Request = {
    /// An identifier established by the Client that MUST contain a String, Number, or NULL value if included.
    /// If it is not included it is assumed to be a notification.
    /// The value SHOULD normally not be Null [1] and Numbers SHOULD NOT contain fractional parts
    Id: int

    /// A String containing the name of the method to be invoked.
    /// Method names that begin with the word rpc followed by a period character (U+002E or ASCII 46)
    /// are reserved for rpc-internal methods and extensions and MUST NOT be used for anything else.
    Method: Method

    /// A Structured value that holds the parameter values to be used during the invocation of the method.
    /// This member MAY be omitted.
    Parameters: RequestParameters
}

and RequestParameters =
    | RawJson of RawJsonData
    | Dto of obj

/// https://www.jsonrpc.org/specification#request_object
type RequestDto = {
    /// A String specifying the version of the JSON-RPC protocol. MUST be exactly "2.0".
    Jsonrpc: string

    /// An identifier established by the Client that MUST contain a String, Number, or NULL value if included.
    /// If it is not included it is assumed to be a notification.
    /// The value SHOULD normally not be Null [1] and Numbers SHOULD NOT contain fractional parts
    Id: int

    /// A String containing the name of the method to be invoked.
    /// Method names that begin with the word rpc followed by a period character (U+002E or ASCII 46)
    /// are reserved for rpc-internal methods and extensions and MUST NOT be used for anything else.
    Method: string

    /// A Structured value that holds the parameter values to be used during the invocation of the method.
    /// This member MAY be omitted.
    Params: obj
}

[<RequireQualifiedAccess>]
module Request =
    let toDto: Request -> RequestDto = fun request -> {
            Jsonrpc = JsonRpc.Version
            Id = request.Id
            Method = request.Method |> Method.value
            Params =
                match request.Parameters with
                | RawJson data -> data |> RawJsonData.parameters
                | Dto dto -> dto
        }

    type private RequestSchema = JsonProvider<"JsonRpc/schema/request.json", SampleIsList=true>

    let parse request =
        try
            let parsed = request |> RequestSchema.Parse

            if parsed.Jsonrpc |> string <> JsonRpc.Version then
                Error (JsonRpcErrorDto.invalidRequest request)
            else
                Ok {
                    Id = parsed.Id
                    Method = Method parsed.Method
                    Parameters = RawJson (RawJsonData parsed.Params.JsonValue)
                }

        with e ->
            Error (JsonRpcErrorDto.parseError {| Request = request; Error = e |})

//
// Response
//

type Response = {
    Id: int
    Result: obj
}

[<RequireQualifiedAccess>]
type ResponseError =
    | Exception of exn
    | JsonRpcError of JsonRpcErrorDto

[<RequireQualifiedAccess>]
module Response =
    type private ResponseSchema = JsonProvider<"JsonRpc/schema/response.json", SampleIsList=true>

    type private ResponseDto = {
        Id: int
        Error: ResponseErrorDto option
        Status: string option
        ResultData: obj
    }

    and private ResponseErrorDto = {
        Code: int
        Message: string
        Data: string
    }

    let parse response =
        try
            let (parsed: ResponseSchema.Root) = response |> ResponseSchema.Parse

            let responseDto = {
                Id = parsed.Id
                Error =
                    parsed.Error
                    |> Option.map (fun error -> {
                        Code = error.Code
                        Message = error.Message
                        Data = error.Data
                    })
                Status = parsed.Result |> Option.bind (fun result -> result.Status)
                ResultData =
                    match parsed.Result with
                    | Some result -> RawJsonData result.JsonValue
                    | _ -> RawJsonData JsonValue.Null
            }

            match responseDto with
            | { Error = Some error } ->
                Error (ResponseError.JsonRpcError {
                    Code = error.Code
                    Message = error.Message
                    Data = error.Data
                })

            | { Status = Some status } when status <> "ok" ->
                Error (ResponseError.JsonRpcError {
                    Code = 400
                    Message = status
                    Data = ""
                })

            | _ ->
                Ok {
                    Id = responseDto.Id
                    Result = responseDto.ResultData
                }

        with e ->
            Error (ResponseError.Exception e)

    let tryParseResult<'Result, 'Parsed> (f: 'Result -> 'Parsed option) response: 'Parsed option =
        match response.Result with
        | :? 'Result as result -> f result
        | _ -> None

    let tryParseResultAsJsonString response =
        response
        |> tryParseResult<RawJsonData, string> (fun (RawJsonData json) -> Some <| json.ToString())

//
// Call
//

[<RequireQualifiedAccess>]
type JsonRpcCallError<'PostError> =
    | RequestError of 'PostError
    | ResponseError of ResponseError

[<RequireQualifiedAccess>]
module JsonRpcCall =
    /// Send jsonrpc request by your own `postJson` function, it MUST be a `HTTP` request with `application/json` Accept and ContentType.
    /// RequestDto MUST be serialized to json.
    let send postJson request: AsyncResult<_, JsonRpcCallError<_>> = asyncResult {
        let! (rawResponse: string) =
            request
            |> Request.toDto
            |> postJson
            |> AsyncResult.mapError JsonRpcCallError.RequestError

        let! (response: Response) =
            rawResponse
            |> Response.parse
            |> Result.mapError JsonRpcCallError.ResponseError

        if request.Id <> response.Id then
            return!
                "Request.id is different than Response.id"
                |> JsonRpcErrorDto.internalError
                |> ResponseError.JsonRpcError
                |> JsonRpcCallError.ResponseError
                |> AsyncResult.ofError

        return response
    }

    open MF.Utils

    /// Post jsonrpc request by a default HTTP.post implementation, is is as simple as it could be.
    /// It serializes request to json and adds correct HTTP headers.
    let post url =
        let post request =
            Http.AsyncRequestString (
                url,
                headers = (
                    [
                        HttpRequestHeaders.Accept "application/json"
                        HttpRequestHeaders.ContentType "application/json"
                    ]
                ),
                body = TextRequest (request |> Serialize.toJson)
            )
            |> AsyncResult.ofAsyncCatch id

        send post

//
// Handler
//

type ActionError = string

/// Parameters parsed by Action.ParseParameters function.
/// "Hidden" as `obj` to allow simple generic Action
type ActionParameters = private ActionParameters of obj

/// This should be a DTO which could be directly serialized into json
type ActionResult = private ActionResult of obj

[<RequireQualifiedAccessAttribute>]
module private ActionParameters =
    let create parameters = parameters :> obj |> ActionParameters

    /// This is a helper function to handle case, when destructure ActionParameters obj value.
    /// Tts more like a logic error, since the jsonRpc handler is responsible internally for delivering parsed parameters directly to Action.Run.
    let invalid (ActionParameters parameters) =
        ActionError (sprintf "Invalid action parameters: %A" parameters)
        |> AsyncResult.ofError

[<RequireQualifiedAccessAttribute>]
module private ActionResult =
    let create result = result :> obj |> ActionResult

type Action = private {
    ParseParameters: RawJsonData -> Result<ActionParameters, ActionError>
    Run: ActionParameters -> AsyncResult<ActionResult, ActionError>
}

[<RequireQualifiedAccess>]
module Action =
    let create<'Parameters, 'Result>
        (parseParameters: RawJsonData -> Result<'Parameters, ActionError>)
        (run: 'Parameters -> AsyncResult<'Result, ActionError>) =

        {
            ParseParameters = parseParameters >> Result.map ActionParameters.create
            Run = function
                | ActionParameters (:? 'Parameters as parameters) -> run parameters |> AsyncResult.map ActionResult.create
                | parameters -> ActionParameters.invalid parameters
        }

type JsonRpcEndpoint = {
    Authorization: HttpHandler option
    HandleMethod: Map<Method, Action>
}

[<RequireQualifiedAccess>]
module Handler =
    let notFound: HttpHandler =
        routef "/%s"
            (JsonRpcErrorDto.notFound >> JsonRpcErrorResponseDto.ofError >> json)
            >=> setStatusCode 404

    let private handle endpoint: HttpHandler = fun next ctx -> task {
        let! response =
            asyncResult {
                use reader = new StreamReader(ctx.Request.Body)

                let! body =
                    reader.ReadToEndAsync()
                    |> AsyncResult.ofTaskCatch JsonRpcErrorDto.invalidRequest
                printfn "Request:\n%A" body

                let! request = body |> Request.parse

                let! action =
                    endpoint.HandleMethod
                    |> Map.tryFind request.Method
                    |> Result.ofOption (JsonRpcErrorDto.methodNotFound request.Method)

                let! parameters =
                    match request.Parameters with
                    | RawJson rawJson -> Ok rawJson
                    | _ -> Error <| JsonRpcErrorDto.internalError {| Request = request; Detail = "Handle request can parse RequestParameters.RawJson only." |}

                let! arguments =
                    parameters
                    |> action.ParseParameters
                    |> Result.mapError JsonRpcErrorDto.invalidParams

                let! (ActionResult data) =
                    arguments
                    |> action.Run
                    |> AsyncResult.mapError JsonRpcErrorDto.internalError

                return {
                    Id = request.Id
                    Result = data
                }
            }
            |> AsyncResult.mapError JsonRpcErrorResponseDto.ofError

        return!
            match response with
            | Ok success -> json success next ctx
            | Error error -> json error next ctx
    }

    let jsonRpc endpoint =
        let jsonRpcRoute =
            match endpoint with
            | { Authorization = Some authorization } -> route "/jsonrpc/" >=> authorization
            | _ -> route "/jsonrpc/"

        jsonRpcRoute
            >=> choose [
                POST >=> handle endpoint

                notFound
            ]
