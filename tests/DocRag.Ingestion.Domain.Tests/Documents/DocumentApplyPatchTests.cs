using DocRag.Ingestion.Domain.Common;
using DocRag.Ingestion.Domain.Documents;
using Shouldly;

namespace DocRag.Ingestion.Domain.Tests.Documents;

public class DocumentApplyPatchTests
{
    private static readonly DateTimeOffset Later = DocumentTestData.Now.AddHours(1);

    [Fact]
    public void Should_KeepOtherFieldsAndBumpRevision_When_OnlyDescriptionIsPatched()
    {
        var document = DocumentTestData.ValidDocument();
        var originalTitle = document.Title;
        var patch = new DocumentPatch { Description = Optional<string?>.Of("new description") };

        var result = document.ApplyPatch(patch, Later);

        result.IsSuccess.ShouldBeTrue();
        document.Description.ShouldBe("new description");
        document.Title.ShouldBe(originalTitle);
        document.Revision.ShouldBe(2);
        document.UpdatedAt.ShouldBe(Later);
    }

    [Fact]
    public void Should_ClearDescription_When_DescriptionIsExplicitlyNull()
    {
        var document = DocumentTestData.ValidDocument();
        var patch = new DocumentPatch { Description = Optional<string?>.Of(null) };

        var result = document.ApplyPatch(patch, Later);

        result.IsSuccess.ShouldBeTrue();
        document.Description.ShouldBeNull();
    }

    [Fact]
    public void Should_ReturnFailureAndKeepState_When_TitleIsPatchedToNull()
    {
        var document = DocumentTestData.ValidDocument();
        var originalTitle = document.Title;
        var patch = new DocumentPatch { Title = Optional<string?>.Of(null) };

        var result = document.ApplyPatch(patch, Later);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-document-invalid");
        document.Title.ShouldBe(originalTitle);
        document.Revision.ShouldBe(1);
    }

    [Fact]
    public void Should_ReplaceTags_When_TagsArePatched()
    {
        var document = DocumentTestData.ValidDocument();
        var patch = new DocumentPatch { Tags = Optional<IReadOnlyList<string>?>.Of(["only"]) };

        var result = document.ApplyPatch(patch, Later);

        result.IsSuccess.ShouldBeTrue();
        document.Tags.ShouldBe(["only"]);
    }

    [Fact]
    public void Should_MergeMetadataRecursively_When_MetadataIsPatched()
    {
        var document = DocumentTestData.ValidDocument();
        var patch = new DocumentPatch
        {
            Metadata = Optional<System.Text.Json.JsonElement?>.Of(DocumentTestData.Json("""{"publisher":null,"nested":{"b":null,"c":3}}""")),
        };

        var result = document.ApplyPatch(patch, Later);

        result.IsSuccess.ShouldBeTrue();
        document.Metadata.GetRawText().ShouldBe("""{"nested":{"a":1,"c":3}}""");
    }

    [Fact]
    public void Should_ResetMetadataToEmptyObject_When_MetadataIsPatchedToNull()
    {
        var document = DocumentTestData.ValidDocument();
        var patch = new DocumentPatch { Metadata = Optional<System.Text.Json.JsonElement?>.Of(null) };

        var result = document.ApplyPatch(patch, Later);

        result.IsSuccess.ShouldBeTrue();
        document.Metadata.EnumerateObject().ShouldBeEmpty();
    }
}
