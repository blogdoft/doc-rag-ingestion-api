using System.Text;
using DocRag.Ingestion.Application.Files;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Files;

public class FileSignatureTests
{
    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [Fact]
    public void Should_Accept_When_PdfHasMagicBytes()
    {
        FileSignature.Matches("application/pdf", "%PDF-1.7\n..."u8).ShouldBeTrue();
    }

    [Fact]
    public void Should_Reject_When_PdfLacksMagicBytes()
    {
        FileSignature.Matches("application/pdf", "<html></html>"u8).ShouldBeFalse();
    }

    [Fact]
    public void Should_Accept_When_DocxIsAZipArchive()
    {
        FileSignature.Matches(Docx, [0x50, 0x4B, 0x03, 0x04, 0x00]).ShouldBeTrue();
    }

    [Fact]
    public void Should_Reject_When_DocxIsNotAZipArchive()
    {
        FileSignature.Matches(Docx, "plain text"u8).ShouldBeFalse();
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    [InlineData("text/html")]
    public void Should_Accept_When_TextIsValidUtf8(string contentType)
    {
        FileSignature.Matches(contentType, Encoding.UTF8.GetBytes("# Título – ação\n")).ShouldBeTrue();
    }

    [Fact]
    public void Should_Accept_When_AMultiByteCharacterIsCutAtTheWindowEnd()
    {
        var bytes = Encoding.UTF8.GetBytes("abc€");

        FileSignature.Matches("text/plain", bytes.AsSpan(0, bytes.Length - 1)).ShouldBeTrue();
    }

    [Fact]
    public void Should_Reject_When_TextContainsNulBytes()
    {
        FileSignature.Matches("text/plain", [0x61, 0x00, 0x62]).ShouldBeFalse();
    }

    [Fact]
    public void Should_Reject_When_TextIsActuallyAPdf()
    {
        FileSignature.Matches("text/plain", "%PDF-1.4 ascii only"u8).ShouldBeFalse();
    }

    [Fact]
    public void Should_Reject_When_TypeIsUnknown()
    {
        FileSignature.Matches("image/png", [1, 2, 3]).ShouldBeFalse();
    }
}
