using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

namespace DocRag.Ingestion.Infrastructure.Tests.Support;

/// <summary>Real PostgreSQL, Garage (S3) and Kafka containers shared by every integration test class.</summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    public const string GarageAccessKey = "GK000000000000000000000001";
    public const string GarageSecretKey = "0000000000000000000000000000000000000000000000000000000000000001";

    private const string GarageConfig = """
        metadata_dir = "/var/lib/garage/meta"
        data_dir = "/var/lib/garage/data"
        db_engine = "sqlite"
        replication_factor = 1
        rpc_bind_addr = "[::]:3901"
        rpc_public_addr = "127.0.0.1:3901"
        rpc_secret = "1799bccfd7411eddcf9ebd316bc1f5287ad12a68094e1c6ac968f6ee0fea2e42"

        [s3_api]
        s3_region = "garage"
        api_bind_addr = "[::]:3900"
        root_domain = ".s3.garage.localhost"

        [admin]
        api_bind_addr = "[::]:3903"
        admin_token = "test-admin-token"
        """;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .WithUsername("docrag")
        .WithPassword("docrag-test")
        .WithCommand("-c", "max_connections=500")
        .Build();

    private readonly IContainer _garage = new ContainerBuilder("dxflrs/garage:v2.4.1")
        .WithPortBinding(3900, true)
        .WithResourceMapping(Encoding.UTF8.GetBytes(GarageConfig), "/etc/garage.toml")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.5.12").Build();

    private int _databaseCounter;
    private int _bucketCounter;

    public string KafkaBootstrapServers => _kafka.GetBootstrapAddress();

    public string GarageServiceUrl => $"http://{_garage.Hostname}:{_garage.GetMappedPublicPort(3900)}";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _garage.StartAsync(), _kafka.StartAsync());
        await ConfigureGarageAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _garage.DisposeAsync().AsTask(), _kafka.DisposeAsync().AsTask());
    }

    /// <summary>Creates an empty, isolated database and returns its connection string.</summary>
    public async Task<string> NewDatabaseAsync()
    {
        var name = $"docrag_{Interlocked.Increment(ref _databaseCounter)}_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name, Pooling = true, MaxPoolSize = 8 }.ConnectionString;
    }

    /// <summary>Creates an isolated, private bucket the test key may use, and returns its name.</summary>
    public async Task<string> NewBucketAsync()
    {
        var name = $"docrag-test-{Interlocked.Increment(ref _bucketCounter)}-{Guid.NewGuid():N}"[..40];
        await GarageAsync("bucket", "create", name);
        await GarageAsync("bucket", "allow", "--read", "--write", "--owner", name, "--key", "docrag");
        return name;
    }

    private async Task ConfigureGarageAsync()
    {
        string nodeId = string.Empty;
        for (var attempt = 0; attempt < 60 && nodeId.Length == 0; attempt++)
        {
            var status = await _garage.ExecAsync(["/garage", "status"]);
            nodeId = status.ExitCode == 0
                ? status.Stdout.Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty)
                    .FirstOrDefault(token => token.Length == 16 && token.All(Uri.IsHexDigit)) ?? string.Empty
                : string.Empty;
            if (nodeId.Length == 0)
            {
                await Task.Delay(500);
            }
        }

        await GarageAsync("layout", "assign", "-z", "dc1", "-c", "1G", nodeId);
        await GarageAsync("layout", "apply", "--version", "1");
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if ((await _garage.ExecAsync(["/garage", "key", "import", "--yes", GarageAccessKey, GarageSecretKey, "-n", "docrag"])).ExitCode == 0)
            {
                return;
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException("Garage did not become ready.");
    }

    private async Task GarageAsync(params string[] arguments)
    {
        var result = await _garage.ExecAsync(["/garage", .. arguments]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"garage {string.Join(' ', arguments)} failed: {result.Stderr}");
        }
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "infrastructure";
}
