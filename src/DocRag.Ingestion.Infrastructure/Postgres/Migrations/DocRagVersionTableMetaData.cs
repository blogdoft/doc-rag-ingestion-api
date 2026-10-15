using FluentMigrator.Runner.Conventions;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.VersionTableInfo;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations;

/// <summary>
/// Stores FluentMigrator's bookkeeping in a snake_case table named after this service, so it never
/// collides with another service's migrations when they share a database.
/// </summary>
[VersionTableMetaData]
public sealed class DocRagVersionTableMetaData(IConventionSet conventionSet, IOptions<RunnerOptions> runnerOptions)
    : DefaultVersionTableMetaData(conventionSet, runnerOptions)
{
    /// <inheritdoc />
    public override string TableName => "doc_rag_ingestion_schema_versions";

    /// <inheritdoc />
    public override string ColumnName => "version";

    /// <inheritdoc />
    public override string DescriptionColumnName => "description";

    /// <inheritdoc />
    public override string AppliedOnColumnName => "applied_on";

    /// <inheritdoc />
    public override string UniqueIndexName => "ux_doc_rag_ingestion_schema_versions_version";
}
