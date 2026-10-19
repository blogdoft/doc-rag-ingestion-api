using DocRag.Ingestion.Domain.Files;
using Shouldly;

namespace DocRag.Ingestion.Domain.Tests.Files;

public class DocumentFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static BlogDoFT.Libs.ResultPattern.Result<DocumentFile> Create(long size = 10, string? sha = null) => DocumentFile.Create(
        Guid.CreateVersion7(Now), 1, "manual.pdf", "application/pdf", size, sha ?? new string('a', 64), "bucket", "key", Now);

    [Fact]
    public void Should_CreateFile_When_AllValuesAreValid()
    {
        var result = Create();

        result.IsSuccess.ShouldBeTrue();
        result.Value.SizeBytes.ShouldBe(10);
        result.Value.Bucket.ShouldBe("bucket");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Should_ReturnFailure_When_FileIsEmpty(long size)
    {
        var result = Create(size);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-file-empty");
    }

    [Theory]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Should_ReturnFailure_When_Sha256IsNotLowercaseHex(string sha)
    {
        var result = Create(sha: sha);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-file-sha256-invalid");
    }
}
