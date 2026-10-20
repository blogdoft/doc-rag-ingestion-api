using DocRag.Ingestion.Domain.MasterData;
using Shouldly;

namespace DocRag.Ingestion.Domain.Tests.MasterData;

public class DocumentTypeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static DocumentTypeBuilder ValidBuilder() =>
        DocumentType.Builder().WithPublicId(Guid.CreateVersion7(Now)).Named("fiscal-manual");

    [Fact]
    public void Should_BuildDocumentType_When_NameIsValid()
    {
        var result = ValidBuilder().Build(Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("fiscal-manual");
        result.Value.Id.ShouldBe(0);
        result.Value.CreatedAt.ShouldBe(Now);
        result.Value.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Should_TrimTheName_When_ItHasSurroundingWhitespace()
    {
        var result = ValidBuilder().Named("  fiscal-manual  ").Build(Now);

        result.Value.Name.ShouldBe("fiscal-manual");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_ReturnFailure_When_NameIsMissing(string? name)
    {
        var result = ValidBuilder().Named(name).Build(Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-document-type-invalid");
        result.Failure.Message.ShouldContain("name is required");
    }

    [Fact]
    public void Should_ReturnFailure_When_NameExceedsMaximumLength()
    {
        var result = ValidBuilder().Named(new string('a', 101)).Build(Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("at most 100");
    }

    [Fact]
    public void Should_ReturnFailure_When_PublicIdWasNotGenerated()
    {
        var result = DocumentType.Builder().Named("x").Build(Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("500-document-type-id-missing");
    }

    [Fact]
    public void Should_ChangeNameAndUpdateInstant_When_RenamedWithAValidName()
    {
        var documentType = ValidBuilder().Build(Now).Value;
        var later = Now.AddHours(1);

        var result = documentType.Rename("legal-opinion", later);

        result.IsSuccess.ShouldBeTrue();
        documentType.Name.ShouldBe("legal-opinion");
        documentType.UpdatedAt.ShouldBe(later);
        documentType.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Should_KeepState_When_RenamedWithAnInvalidName()
    {
        var documentType = ValidBuilder().Build(Now).Value;

        var result = documentType.Rename(" ", Now.AddHours(1));

        result.IsFailure.ShouldBeTrue();
        documentType.Name.ShouldBe("fiscal-manual");
        documentType.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Should_RejectASecondKeyAssignment_When_TheIdWasAlreadyAssigned()
    {
        var documentType = ValidBuilder().Build(Now).Value;
        documentType.AssignId(7);

        Should.Throw<InvalidOperationException>(() => documentType.AssignId(8));
    }
}
