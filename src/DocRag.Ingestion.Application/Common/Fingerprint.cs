using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DocRag.Ingestion.Application.Common;

/// <summary>Deterministic fingerprints of requests, used to detect idempotency key misuse.</summary>
public static class Fingerprint
{
    /// <summary>Hashes the parts in order; JSON values are canonicalized (sorted keys).</summary>
    /// <param name="parts">Scalars, <see cref="JsonElement"/> or sequences of scalars.</param>
    /// <returns>A lowercase hexadecimal SHA-256.</returns>
    public static string Of(params object?[] parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(Describe(part)).Append('\u001f');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string Describe(object? part) => part switch
    {
        null => "~",
        JsonElement element => Canonical(JsonNode.Parse(element.GetRawText())),
        string text => text,
        IEnumerable<string> items => string.Join('\u001e', items),
        DateTimeOffset date => date.UtcDateTime.ToString("O"),
        _ => Convert.ToString(part, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };
}
