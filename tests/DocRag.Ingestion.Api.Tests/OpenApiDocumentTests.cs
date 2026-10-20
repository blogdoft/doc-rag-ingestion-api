using System.Text.Json;
using DocRag.Ingestion.Api.Tests.Support;
using Shouldly;

namespace DocRag.Ingestion.Api.Tests;

public class OpenApiDocumentTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OpenApiDocumentTests(ApiFactory factory) => _factory = factory;

    private async Task<JsonElement> LoadAsync()
    {
        var json = await _factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        return JsonDocument.Parse(json).RootElement;
    }

    [Theory]
    [InlineData("/api/v1/documents", "post")]
    [InlineData("/api/v1/documents/{documentId}", "get")]
    [InlineData("/api/v1/documents/{documentId}", "patch")]
    [InlineData("/api/v1/documents/{documentId}/versions", "post")]
    [InlineData("/api/v1/documents/{documentId}/versions", "get")]
    [InlineData("/api/v1/documents/{documentId}/versions/{versionId}", "get")]
    [InlineData("/api/v1/documents/{documentId}/versions/{versionId}/files", "post")]
    public async Task Should_DescribeEveryContractOperationWithSummaryAndResponses_When_DocumentIsGenerated(string path, string method)
    {
        var document = await LoadAsync();

        var operation = document.GetProperty("paths").GetProperty(path).GetProperty(method);

        operation.GetProperty("summary").GetString().ShouldNotBeNullOrWhiteSpace();
        operation.GetProperty("description").GetString()!.Length.ShouldBeGreaterThan(40);
        operation.GetProperty("responses").EnumerateObject().Count().ShouldBeGreaterThan(2);
        operation.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldAllBe(tag => tag == tag!.ToLowerInvariant());
    }

    [Fact]
    public async Task Should_DeclareBearerAuthenticationGlobally_When_DocumentIsGenerated()
    {
        var document = await LoadAsync();

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

        scheme.GetProperty("scheme").GetString().ShouldBe("bearer");
        scheme.GetProperty("bearerFormat").GetString().ShouldBe("JWT");
        document.GetProperty("security").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Should_DocumentRawBinaryBodyAndHeaders_When_DescribingTheUpload()
    {
        var document = await LoadAsync();

        var post = document.GetProperty("paths").GetProperty("/api/v1/documents/{documentId}/versions/{versionId}/files").GetProperty("post");

        var content = post.GetProperty("requestBody").GetProperty("content");
        content.GetProperty("application/pdf").GetProperty("schema").GetProperty("format").GetString().ShouldBe("binary");
        content.TryGetProperty("multipart/form-data", out _).ShouldBeFalse();
        var headers = post.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        headers.ShouldContain("Content-Disposition");
        headers.ShouldContain("Idempotency-Key");
        headers.ShouldContain("X-Content-SHA256");
        post.GetProperty("responses").TryGetProperty("413", out _).ShouldBeFalse();
        post.GetProperty("responses").TryGetProperty("415", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_UseMergePatchMediaTypeAndRequireIfMatch_When_DescribingPatch()
    {
        var document = await LoadAsync();

        var patch = document.GetProperty("paths").GetProperty("/api/v1/documents/{documentId}").GetProperty("patch");

        patch.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/merge-patch+json", out _).ShouldBeTrue();
        var ifMatch = patch.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "If-Match");
        ifMatch.GetProperty("required").GetBoolean().ShouldBeTrue();
        patch.GetProperty("responses").TryGetProperty("412", out _).ShouldBeFalse();
        patch.GetProperty("responses").TryGetProperty("428", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_ExposeCamelCaseSchemasWithExamples_When_DocumentIsGenerated()
    {
        var document = await LoadAsync();

        var schemas = document.GetProperty("components").GetProperty("schemas");

        var request = schemas.GetProperty("CreateDocumentRequest");
        request.GetProperty("properties").TryGetProperty("title", out _).ShouldBeTrue();
        request.GetProperty("example").TryGetProperty("typeId", out _).ShouldBeTrue();
        request.GetProperty("example").TryGetProperty("languageId", out _).ShouldBeTrue();
        schemas.GetProperty("VersionResponse").GetProperty("properties").TryGetProperty("documentId", out _).ShouldBeTrue();
        schemas.GetProperty("FileResponse").GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .ShouldNotContain(name => name.Contains("bucket", StringComparison.OrdinalIgnoreCase) || name.Contains("objectKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_ServeSwaggerUi_When_RequestingTheUiPage()
    {
        var response = await _factory.CreateClient().GetAsync("/swagger/index.html");

        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_DeclareOpenApi31AndExportTheDocument_When_DocumentIsGenerated()
    {
        var json = await _factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        JsonDocument.Parse(json).RootElement.GetProperty("openapi").GetString().ShouldStartWith("3.1");
        var output = Environment.GetEnvironmentVariable("OPENAPI_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            await File.WriteAllTextAsync(output, json);
        }
    }
}
