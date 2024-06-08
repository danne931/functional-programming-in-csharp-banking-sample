[<RequireQualifiedAccess>]
module Serialization

open Lib.SharedTypes

#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
open System.Text.Json
open System.Text.Json.Serialization

let private baseConfig =
   JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags()

let mergeDefaultJsonOptions (options: JsonSerializerOptions) =
   options.Converters.Add(JsonStringEnumConverter())
   options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
   options

let jsonOptions = baseConfig.ToJsonSerializerOptions()
mergeDefaultJsonOptions jsonOptions |> ignore
#endif

let coders = Extra.empty |> Extra.withDecimal |> Extra.withInt64

#if FABLE_COMPILER
let inline deserializeUnsafe<'t> (data: string) : 't =
#else
let deserializeUnsafe<'t> (data: string) : 't =
#endif
   Decode.Auto.unsafeFromString<'t> (data, extra = coders)

#if FABLE_COMPILER
let inline deserialize<'t> (data: string) : Result<'t, Err> =
#else
let deserialize<'t> (data: string) : Result<'t, Err> =
#endif
   Decode.Auto.fromString<'t> (data, extra = coders)
   |> Result.mapError Err.SerializationError

#if FABLE_COMPILER
let inline serialize data =
#else
let serialize data =
#endif
   Encode.Auto.toString (data, extra = coders, skipNullField = false)
