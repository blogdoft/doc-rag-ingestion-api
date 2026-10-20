using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.Postgres;

[Collection(InfrastructureCollection.Name)]
public class MasterDataRepositoryTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = await infrastructure.NewDatabaseAsync();
        _provider = ServiceHost.Build(_connectionString, infrastructure.GarageServiceUrl, "unused");
        await ServiceHost.MigrateAsync(_provider);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    [Fact]
    public async Task Should_PersistTypeWithGeneratedKeyAndPublicId_When_Created()
    {
        var created = await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().CreateAsync("fiscal-manual", default));

        created.IsSuccess.ShouldBeTrue();
        created.Value.Id.ShouldBeGreaterThan(0);
        var found = await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().GetAsync(created.Value.PublicId, default));
        found.Value.Name.ShouldBe("fiscal-manual");
        found.Value.CreatedAt.ShouldBe(created.Value.CreatedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Should_RejectDuplicateTypeName_When_OnlyTheCaseDiffers()
    {
        await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().CreateAsync("Fiscal-Manual", default));

        var duplicate = await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().CreateAsync("fiscal-manual", default));

        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Failure.Code.ShouldBe("400-document-type-name-exists");
    }

    [Fact]
    public async Task Should_RejectRenameToExistingTypeName_When_UpdatingAnotherRow()
    {
        await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().CreateAsync("a", default));
        var second = await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().CreateAsync("b", default));

        var result = await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().UpdateAsync(second.Value.PublicId, "A", default));

        result.Failure.Code.ShouldBe("400-document-type-name-exists");
    }

    [Fact]
    public async Task Should_PageTypesNewestFirst_When_ListingWithACursor()
    {
        foreach (var name in new[] { "a", "b", "c" })
        {
            await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().CreateAsync(name, default));
        }

        var first = await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().ListAsync(2, null, default));
        var second = await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().ListAsync(2, first.Value.NextCursor, default));

        first.Value.Items.Select(i => i.Name).ShouldBe(["c", "b"]);
        second.Value.Items.Select(i => i.Name).ShouldBe(["a"]);
        second.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Should_RemoveType_When_Deleted()
    {
        var created = await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().CreateAsync("gone", default));

        var deleted = await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().DeleteAsync(created.Value.PublicId, default));
        var again = await InScopeAsync(sp => sp.GetRequiredService<DocumentTypeService>().DeleteAsync(created.Value.PublicId, default));

        deleted.IsSuccess.ShouldBeTrue();
        again.Failure.Code.ShouldBe("404-document-type-not-found");
    }

    [Fact]
    public async Task Should_PersistLanguageAndRejectDuplicateCode_When_OnlyTheCaseDiffers()
    {
        var created = await InScopeAsync(sp => sp.GetRequiredService<DocumentLanguageService>().CreateAsync("pt-BR", default));
        var duplicate = await InScopeAsync(sp => sp.GetRequiredService<DocumentLanguageService>().CreateAsync("pt-br", default));

        created.IsSuccess.ShouldBeTrue();
        created.Value.Id.ShouldBeGreaterThan(0);
        duplicate.Failure.Code.ShouldBe("400-document-language-code-exists");
    }

    [Fact]
    public async Task Should_ChangeLanguageCode_When_Updated()
    {
        var created = await InScopeAsync(sp => sp.GetRequiredService<DocumentLanguageService>().CreateAsync("pt-BR", default));

        var updated = await InScopeAsync(sp => sp.GetRequiredService<DocumentLanguageService>().UpdateAsync(created.Value.PublicId, "en-US", default));
        var found = await InScopeAsync(sp => sp.GetRequiredService<DocumentLanguageService>().GetAsync(created.Value.PublicId, default));

        updated.IsSuccess.ShouldBeTrue();
        found.Value.Code.ShouldBe("en-US");
        found.Value.UpdatedAt.ShouldBeGreaterThanOrEqualTo(found.Value.CreatedAt);
    }

    [Fact]
    public async Task Should_RemoveLanguage_When_Deleted()
    {
        var created = await InScopeAsync(sp => sp.GetRequiredService<DocumentLanguageService>().CreateAsync("es", default));

        var deleted = await InScopeAsync(sp => sp.GetRequiredService<DocumentLanguageService>().DeleteAsync(created.Value.PublicId, default));

        deleted.IsSuccess.ShouldBeTrue();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM document_languages", connection);
        Convert.ToInt64(await command.ExecuteScalarAsync()).ShouldBe(0);
    }
}
