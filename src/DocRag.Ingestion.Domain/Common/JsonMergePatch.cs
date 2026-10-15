using System.Text.Json;
using System.Text.Json.Nodes;

namespace DocRag.Ingestion.Domain.Common;

/// <summary>JSON Merge Patch (RFC 7396) application over JSON objects.</summary>
public static class JsonMergePatch
{
    /// <summary>
    /// Applies <paramref name="patch"/> onto <paramref name="target"/>: objects merge recursively,
    /// <c>null</c> removes a member, any other value replaces it.
    /// </summary>
    /// <param name="target">Current JSON object.</param>
    /// <param name="patch">Patch JSON object.</param>
    /// <returns>The merged JSON object (a new instance).</returns>
    public static JsonElement Apply(JsonElement target, JsonElement patch)
    {
        var merged = Merge(JsonNode.Parse(target.GetRawText()), JsonNode.Parse(patch.GetRawText()));
        return JsonSerializer.SerializeToElement(merged);
    }

    private static JsonNode? Merge(JsonNode? target, JsonNode? patch)
    {
        if (patch is not JsonObject patchObject)
        {
            return patch?.DeepClone();
        }

        var result = target as JsonObject ?? [];
        foreach (var (key, value) in patchObject)
        {
            if (value is null)
            {
                result.Remove(key);
                continue;
            }

            var current = result[key];
            result.Remove(key);
            result[key] = Merge(current, value);
        }

        return result;
    }
}
