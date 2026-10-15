using System.Text.Json;
using DocRag.Ingestion.Application.Common;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Common;

public class FingerprintTests
{
    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void Should_BeEqual_When_JsonDiffersOnlyByKeyOrderAndWhitespace()
    {
        var first = Fingerprint.Of("op", Json("""{"a":1,"b":{"x":1,"y":2}}"""));
        var second = Fingerprint.Of("op", Json("""{ "b": {"y":2,"x":1}, "a": 1 }"""));

        first.ShouldBe(second);
    }

    [Fact]
    public void Should_Differ_When_AnyPartChanges()
    {
        var first = Fingerprint.Of("op", "title", new[] { "a", "b" });
        var second = Fingerprint.Of("op", "title", new[] { "a", "c" });

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Should_NotCollide_When_PartsAreSplitDifferently()
    {
        Fingerprint.Of("ab", "c").ShouldNotBe(Fingerprint.Of("a", "bc"));
    }
}
