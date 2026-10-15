using DocRag.Ingestion.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocRag.Ingestion.Infrastructure.Tests.Support;

/// <summary>The real API (real migrations, repositories, Garage storage) over isolated PostgreSQL database and bucket.</summary>
public sealed class IngestionApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _bucket;
    private readonly string _garageUrl;
    private readonly IDictionary<string, string?>? _overrides;

    /// <summary>Initializes a new instance of the <see cref="IngestionApiFactory"/> class.</summary>
    /// <param name="connectionString">Isolated PostgreSQL database.</param>
    /// <param name="bucket">Isolated Garage bucket.</param>
    /// <param name="garageUrl">Garage S3 endpoint.</param>
    /// <param name="overrides">Extra configuration values.</param>
    public IngestionApiFactory(string connectionString, string bucket, string garageUrl, IDictionary<string, string?>? overrides = null)
    {
        _connectionString = connectionString;
        _bucket = bucket;
        _garageUrl = garageUrl;
        _overrides = overrides;

        // Real Kestrel instead of TestServer: TestServer buffers non-StreamContent request bodies
        // entirely before invoking the app, which would defeat every streaming scenario under test.
        UseKestrel();
    }

    /// <summary>Gets the warnings and errors logged by the API under test.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _connectionString,
                ["Auth:SigningKey"] = ApiTestConstants.SigningKey,
                ["Auth:Issuer"] = ApiTestConstants.Issuer,
                ["Auth:Audience"] = ApiTestConstants.Audience,
                ["Auth:RequireHttpsMetadata"] = "false",
                ["Garage:ServiceUrl"] = _garageUrl,
                ["Garage:Bucket"] = _bucket,
                ["Garage:AccessKey"] = InfrastructureFixture.GarageAccessKey,
                ["Garage:SecretKey"] = InfrastructureFixture.GarageSecretKey,
                ["Garage:PartSizeMiB"] = "5",
                ["Upload:MaxConcurrentUploads"] = "10",
            };
            foreach (var (key, value) in _overrides ?? new Dictionary<string, string?>())
            {
                settings[key] = value;
            }

            configuration.AddInMemoryCollection(settings);
        });
    }

    /// <summary>Waits until the warm-up migrations are done and dependencies answer.</summary>
    /// <returns>A task completing when <c>/health/ready</c> is healthy.</returns>
    public async Task WaitUntilReadyAsync()
    {
        using var client = CreateClient();
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var response = await client.GetAsync("/health/ready");
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("The API did not become ready.");
    }
}

/// <summary>Constants shared with the API test project's token helper.</summary>
public static class ApiTestConstants
{
    public const string SigningKey = ApiFactory.SigningKey;
    public const string Issuer = ApiFactory.Issuer;
    public const string Audience = ApiFactory.Audience;
}
