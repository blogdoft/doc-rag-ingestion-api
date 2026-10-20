using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocRag.Ingestion.Api.Tests.Support;
using Npgsql;

namespace DocRag.Ingestion.Infrastructure.Tests.Support;

/// <summary>Boots the real API over an isolated database and bucket, and offers HTTP and SQL helpers.</summary>
public abstract class E2ETestBase(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    protected static readonly byte[] PdfBytes = "%PDF-1.4\nhello integration world\n"u8.ToArray();

    protected InfrastructureFixture Infrastructure { get; } = infrastructure;

    protected IngestionApiFactory Factory { get; private set; } = null!;

    protected string ConnectionString { get; private set; } = string.Empty;

    protected string Bucket { get; private set; } = string.Empty;

    protected virtual IDictionary<string, string?>? ConfigurationOverrides => null;

    private readonly Dictionary<string, Guid> _masterData = [];
    private readonly SemaphoreSlim _masterDataLock = new(1, 1);

    public async Task InitializeAsync()
    {
        ConnectionString = await Infrastructure.NewDatabaseAsync();
        Bucket = await Infrastructure.NewBucketAsync();
        Factory = new IngestionApiFactory(ConnectionString, Bucket, Infrastructure.GarageServiceUrl, ConfigurationOverrides);
        await Factory.WaitUntilReadyAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }

    protected HttpClient Client(string scope = "ingestion:read ingestion:write") =>
        Factory.CreateDefaultClient().WithToken(TestTokens.Create(scope));

    protected static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    protected Task<Guid> TypeIdAsync(HttpClient client, string name = "fiscal-manual") =>
        MasterDataIdAsync(client, "document-types", "name", name);

    protected Task<Guid> LanguageIdAsync(HttpClient client, string code = "pt-BR") =>
        MasterDataIdAsync(client, "document-languages", "code", code);

    /// <summary>Registers the master data record through the API the first time it is asked for, then reuses its identifier.</summary>
    private async Task<Guid> MasterDataIdAsync(HttpClient client, string resource, string field, string value)
    {
        await _masterDataLock.WaitAsync();
        try
        {
            var key = $"{resource}:{value}";
            if (_masterData.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var response = await client.PostAsJsonAsync($"/api/v1/{resource}", new Dictionary<string, string> { [field] = value });
            response.EnsureSuccessStatusCode();
            var id = (await ReadAsync(response)).GetProperty("id").GetGuid();
            _masterData[key] = id;
            return id;
        }
        finally
        {
            _masterDataLock.Release();
        }
    }

    protected async Task<Guid> CreateDocumentAsync(HttpClient client, string title = "Manual", string? idempotencyKey = null)
    {
        var typeId = await TypeIdAsync(client);
        var languageId = await LanguageIdAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents")
        {
            Content = JsonContent.Create(new { title, typeId, languageId, tags = new[] { "cte" }, metadata = new { publisher = "ENCAT" } }),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    protected async Task<Guid> CreateVersionAsync(HttpClient client, Guid documentId, string label = "4.00")
    {
        var response = await client.PostAsJsonAsync($"/api/v1/documents/{documentId}/versions", new { label });
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    protected Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid documentId,
        Guid versionId,
        HttpContent content,
        string contentType = "application/pdf",
        string fileName = "manual.pdf",
        string? idempotencyKey = null,
        string? sha256 = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/documents/{documentId}/versions/{versionId}/files") { Content = content };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Content.Headers.TryAddWithoutValidation("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (sha256 is not null)
        {
            request.Headers.Add("X-Content-SHA256", sha256);
        }

        return client.SendAsync(request, cancellationToken);
    }

    protected Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid documentId, Guid versionId, byte[] bytes, string contentType = "application/pdf", string? idempotencyKey = null, string? sha256 = null) =>
        UploadAsync(client, documentId, versionId, new ByteArrayContent(bytes), contentType, "manual.pdf", idempotencyKey, sha256);

    protected async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)Convert.ChangeType(result, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    protected async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    protected async Task EventuallyAsync(Func<Task<bool>> condition, string because, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Condition not met in {seconds}s: {because}\n{string.Join('\n', Factory.Logs.Entries)}");
    }
}
