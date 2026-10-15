namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations;

/// <summary>Connection string the migrator uses, kept apart from the pooled data source.</summary>
/// <param name="ConnectionString">PostgreSQL connection string.</param>
public sealed record PostgresDatabaseOptions(string ConnectionString);
