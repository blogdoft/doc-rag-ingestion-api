using DocRag.Ingestion.Domain.Common;
using Shouldly;

namespace DocRag.Ingestion.Domain.Tests.Common;

public class JsonMergePatchTests
{
    [Theory]
    [InlineData("""{"a":"b"}""", """{"a":"c"}""", """{"a":"c"}""")]
    [InlineData("""{"a":"b"}""", """{"b":"c"}""", """{"a":"b","b":"c"}""")]
    [InlineData("""{"a":"b"}""", """{"a":null}""", """{}""")]
    [InlineData("""{"a":"b","b":"c"}""", """{"a":null}""", """{"b":"c"}""")]
    [InlineData("""{"a":["b"]}""", """{"a":"c"}""", """{"a":"c"}""")]
    [InlineData("""{"a":"c"}""", """{"a":["b"]}""", """{"a":["b"]}""")]
    [InlineData("""{"a":{"b":"c"}}""", """{"a":{"b":"d","c":null}}""", """{"a":{"b":"d"}}""")]
    [InlineData("""{}""", """{"a":{"bb":{"ccc":null}}}""", """{"a":{"bb":{}}}""")]
    public void Should_FollowRfc7396_When_PatchIsApplied(string target, string patch, string expected)
    {
        var result = JsonMergePatch.Apply(System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(target),
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(patch));

        result.GetRawText().ShouldBe(expected);
    }
}
