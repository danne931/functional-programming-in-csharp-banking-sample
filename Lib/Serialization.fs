[<RequireQualifiedAccess>]
module Serialization

open System.Text.Json
open System.Text.Json.Serialization

let private baseConfig =
   JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags()

let mergeDefaultJsonOptions (options: JsonSerializerOptions) =
   options.Converters.Add(JsonStringEnumConverter())
   options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase

let jsonOptions = baseConfig.ToJsonSerializerOptions()
mergeDefaultJsonOptions jsonOptions

let withInjectedOptions opts =
   mergeDefaultJsonOptions opts
   baseConfig.AddToJsonSerializerOptions opts

let serialize (data: _) : string =
   JsonSerializer.Serialize(data, jsonOptions)

let deserializeUnsafe<'t> (data: string) : 't =
   JsonSerializer.Deserialize<'t>(data, jsonOptions)

let deserialize<'t> (data: string) : Result<'t, string> =
   try
      deserializeUnsafe<'t> data |> Ok
   with err ->
      $"Deserialization error: {err.Message}" |> Error
