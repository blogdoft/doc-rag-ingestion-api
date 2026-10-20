using DocRag.Ingestion.Domain.MasterData;
using Shouldly;

namespace DocRag.Ingestion.Domain.Tests.MasterData;

public class DocumentLanguageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static DocumentLanguageBuilder ValidBuilder() =>
        DocumentLanguage.Builder().WithPublicId(Guid.CreateVersion7(Now)).WithCode("pt-BR");

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en")]
    [InlineData("zh-Hant-TW")]
    public void Should_BuildDocumentLanguage_When_CodeIsBcp47(string code)
    {
        var result = ValidBuilder().WithCode(code).Build(Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.ShouldBe(code);
        result.Value.CreatedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData("pt_BR")]
    [InlineData("PT-br")]
    [InlineData("x")]
    [InlineData("portuguese!")]
    public void Should_ReturnFailure_When_CodeIsNotBcp47(string code)
    {
        var result = ValidBuilder().WithCode(code).Build(Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-document-language-invalid");
        result.Failure.Message.ShouldContain("BCP 47");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Should_ReturnFailure_When_CodeIsMissing(string? code)
    {
        var result = ValidBuilder().WithCode(code).Build(Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("code is required");
    }

    [Fact]
    public void Should_ReturnFailure_When_CodeExceedsMaximumLength()
    {
        var result = ValidBuilder().WithCode("pt-" + new string('a', 40)).Build(Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("at most 35");
    }

    [Fact]
    public void Should_ChangeCodeAndUpdateInstant_When_ChangedToAValidCode()
    {
        var language = ValidBuilder().Build(Now).Value;
        var later = Now.AddHours(1);

        var result = language.ChangeCode("en-US", later);

        result.IsSuccess.ShouldBeTrue();
        language.Code.ShouldBe("en-US");
        language.UpdatedAt.ShouldBe(later);
    }

    [Fact]
    public void Should_KeepState_When_ChangedToAnInvalidCode()
    {
        var language = ValidBuilder().Build(Now).Value;

        var result = language.ChangeCode("nope!", Now.AddHours(1));

        result.IsFailure.ShouldBeTrue();
        language.Code.ShouldBe("pt-BR");
        language.UpdatedAt.ShouldBe(Now);
    }
}
