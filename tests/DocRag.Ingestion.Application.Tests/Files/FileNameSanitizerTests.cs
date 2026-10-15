using DocRag.Ingestion.Application.Files;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Files;

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("manual.pdf", "manual.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\temp\\manual.pdf", "manual.pdf")]
    [InlineData("a<b>:c|d?.pdf", "abcd.pdf")]
    [InlineData("  ..hidden.txt.. ", "hidden.txt")]
    [InlineData("tab\tname\u0000.pdf", "tabname.pdf")]
    public void Should_StripUnsafeParts_When_NameIsSanitized(string raw, string expected)
    {
        var result = FileNameSanitizer.Sanitize(raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../")]
    [InlineData("...")]
    public void Should_ReturnBadRequest_When_NothingUsableRemains(string? raw)
    {
        var result = FileNameSanitizer.Sanitize(raw);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-file-name-invalid");
    }

    [Fact]
    public void Should_KeepExtension_When_NameIsTooLong()
    {
        var result = FileNameSanitizer.Sanitize(new string('a', 400) + ".pdf");

        result.Value.Length.ShouldBe(255);
        result.Value.ShouldEndWith(".pdf");
    }
}
