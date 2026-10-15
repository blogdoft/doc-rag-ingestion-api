using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.Versions;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Versions.VersionServiceTests;

public class GetAsyncTests : BaseVersionServiceTests
{
    [Fact]
    public async Task Should_ReturnVersion_When_ItExists()
    {
        var service = Service;
        var version = TestData.Version(Document);
        Versions.GetAsync(Document.PublicId, version.PublicId, false, Arg.Any<CancellationToken>()).Returns(version);

        var result = await service.GetAsync(Document.PublicId, version.PublicId, CancellationToken.None);

        result.Value.ShouldBe(version);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_VersionDoesNotExist()
    {
        var service = Service;
        Versions.GetAsync(Document.PublicId, Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns((DocumentVersion?)null);

        var result = await service.GetAsync(Document.PublicId, Guid.NewGuid(), CancellationToken.None);

        result.Failure.Code.ShouldBe("404-version-not-found");
    }
}
