using System.Text.Json;
using DocRag.Ingestion.Domain.Documents;
using Shouldly;

namespace DocRag.Ingestion.Domain.Tests.Documents;

public class DocumentBuilderTests
{
    [Fact]
    public void Should_BuildDocumentWithRevisionOne_When_AllFieldsAreValid()
    {
        var builder = DocumentTestData.ValidBuilder();

        var result = builder.Build(DocumentTestData.Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Revision.ShouldBe(1);
        result.Value.CreatedAt.ShouldBe(DocumentTestData.Now);
        result.Value.Metadata.GetProperty("publisher").GetString().ShouldBe("ENCAT");
    }

    [Fact]
    public void Should_DefaultMetadataToEmptyObject_When_MetadataIsNotProvided()
    {
        var builder = DocumentTestData.ValidBuilder().WithMetadata(null);

        var result = builder.Build(DocumentTestData.Now);

        result.Value.Metadata.ValueKind.ShouldBe(JsonValueKind.Object);
        result.Value.Metadata.EnumerateObject().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_ReturnFailure_When_TitleIsMissing(string? title)
    {
        var builder = DocumentTestData.ValidBuilder().WithTitle(title);

        var result = builder.Build(DocumentTestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("422-document-invalid");
        result.Failure.Message.ShouldContain("title is required");
    }

    [Fact]
    public void Should_ReturnFailure_When_TitleExceedsMaximumLength()
    {
        var builder = DocumentTestData.ValidBuilder().WithTitle(new string('a', 301));

        var result = builder.Build(DocumentTestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("at most 300");
    }

    [Theory]
    [InlineData("pt_BR")]
    [InlineData("PT-br")]
    [InlineData("x")]
    [InlineData("portuguese!")]
    public void Should_ReturnFailure_When_LanguageIsNotBcp47(string language)
    {
        var builder = DocumentTestData.ValidBuilder().InLanguage(language);

        var result = builder.Build(DocumentTestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("BCP 47");
    }

    [Fact]
    public void Should_ReturnFailure_When_TagsAreDuplicated()
    {
        var builder = DocumentTestData.ValidBuilder().WithTags(["cte", "cte"]);

        var result = builder.Build(DocumentTestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("unique");
    }

    [Fact]
    public void Should_ReturnFailure_When_MoreThanFiftyTagsAreProvided()
    {
        var builder = DocumentTestData.ValidBuilder().WithTags(Enumerable.Range(0, 51).Select(i => $"tag-{i}"));

        var result = builder.Build(DocumentTestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("at most 50");
    }

    [Fact]
    public void Should_ReturnFailure_When_MetadataIsNotAnObject()
    {
        var builder = DocumentTestData.ValidBuilder().WithMetadata(DocumentTestData.Json("[1,2]"));

        var result = builder.Build(DocumentTestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("JSON object");
    }

    [Fact]
    public void Should_ReturnFailure_When_MetadataExceedsSizeLimit()
    {
        var big = DocumentTestData.Json($$"""{"blob":"{{new string('x', 70_000)}}"}""");
        var builder = DocumentTestData.ValidBuilder().WithMetadata(big);

        var result = builder.Build(DocumentTestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("64 KiB");
    }

    [Fact]
    public void Should_ReturnFailure_When_MetadataIsNestedTooDeep()
    {
        var json = string.Concat(Enumerable.Repeat("{\"a\":", 20)) + "1" + new string('}', 20);
        var builder = DocumentTestData.ValidBuilder().WithMetadata(DocumentTestData.Json(json));

        var result = builder.Build(DocumentTestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("nesting");
    }
}
