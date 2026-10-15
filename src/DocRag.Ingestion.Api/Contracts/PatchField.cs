using System.Text.Json;
using System.Text.Json.Serialization;
using DocRag.Ingestion.Domain.Common;

namespace DocRag.Ingestion.Api.Contracts;

/// <summary>A JSON Merge Patch member that distinguishes "absent" from "explicitly null".</summary>
/// <typeparam name="T">Member type.</typeparam>
/// <param name="IsSet">Whether the member was present in the request body.</param>
/// <param name="Value">The provided value (possibly <see langword="null"/>).</param>
[JsonConverter(typeof(PatchFieldConverterFactory))]
public readonly record struct PatchField<T>(bool IsSet, T? Value)
{
    /// <summary>Converts to the domain's <see cref="Optional{T}"/>.</summary>
    /// <returns>The equivalent optional.</returns>
    public Optional<T> ToOptional() => IsSet ? Optional<T>.Of(Value) : Optional<T>.Unset;
}

internal sealed class PatchFieldConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(PatchField<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(PatchFieldConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class PatchFieldConverter<T> : JsonConverter<PatchField<T>>
{
    public override bool HandleNull => true;

    public override PatchField<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(true, JsonSerializer.Deserialize<T>(ref reader, options));

    public override void Write(Utf8JsonWriter writer, PatchField<T> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Value, options);
}
