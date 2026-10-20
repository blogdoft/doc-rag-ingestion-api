using System.Net.Http.Json;
using System.Text.Json;
using DocRag.Ingestion.Infrastructure.Tests.Support;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.E2E;

[Collection(InfrastructureCollection.Name)]
public class ListingTests(InfrastructureFixture infrastructure) : E2ETestBase(infrastructure)
{
    private async Task<Guid> CreateAsync(HttpClient client, string title, string type, string language, params string[] tags)
    {
        var typeId = await TypeIdAsync(client, type);
        var languageId = await LanguageIdAsync(client, language);
        var response = await client.PostAsJsonAsync("/api/v1/documents", new { title, typeId, languageId, tags });
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string query)
    {
        var page = await ReadAsync(await client.GetAsync($"/api/v1/documents?{query}"));
        return page.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }

    [Fact]
    public async Task Should_ListNewestFirstAcrossPages_When_PagingWithTheCursor()
    {
        var client = Client();
        var created = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            created.Add(await CreateAsync(client, $"Doc {i}", "manual", "pt-BR"));
        }

        var first = await ReadAsync(await client.GetAsync("/api/v1/documents?pageSize=2"));
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await ReadAsync(await client.GetAsync($"/api/v1/documents?pageSize=2&cursor={Uri.EscapeDataString(cursor!)}"));
        var third = await ReadAsync(await client.GetAsync($"/api/v1/documents?pageSize=2&cursor={Uri.EscapeDataString(second.GetProperty("nextCursor").GetString()!)}"));

        var paged = new[] { first, second, third }
            .SelectMany(page => page.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()))
            .ToList();
        paged.ShouldBe(Enumerable.Reverse(created).ToList());
        third.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Should_ApplyFiltersWithAnd_When_TypeLanguageAndTagAreSent()
    {
        var client = Client();
        var match = await CreateAsync(client, "Match", "fiscal-manual", "pt-BR", "cte", "sefaz");
        await CreateAsync(client, "Other type", "contract", "pt-BR", "cte");
        await CreateAsync(client, "Other language", "fiscal-manual", "en", "cte");
        await CreateAsync(client, "Other tag", "fiscal-manual", "pt-BR", "nfe");

        var typeId = await TypeIdAsync(client, "fiscal-manual");
        var languageId = await LanguageIdAsync(client, "pt-BR");

        (await IdsAsync(client, $"typeId={typeId}&languageId={languageId}&tag=cte")).ShouldBe([match]);
        (await IdsAsync(client, "tag=sefaz")).ShouldBe([match]);
        (await IdsAsync(client, $"typeId={typeId}")).Count.ShouldBe(3);
        (await IdsAsync(client, $"typeId={Guid.NewGuid()}")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_MatchTheWholeTitleIgnoringCaseAndApplyWildcards_When_TheTitleFilterIsSent()
    {
        var client = Client();
        var cte = await CreateAsync(client, "Manual Fiscal CT-e", "manual", "pt-BR");
        var nfe = await CreateAsync(client, "Guia da NF-e", "manual", "pt-BR");
        var rate = await CreateAsync(client, "Taxa de 100% e_x", "manual", "pt-BR");

        (await IdsAsync(client, "title=manual%20fiscal%20ct-e")).ShouldBe([cte]);
        (await IdsAsync(client, "title=fiscal")).ShouldBeEmpty();
        (await IdsAsync(client, "title=*fiscal*")).ShouldBe([cte]);
        (await IdsAsync(client, "title=MANUAL*")).ShouldBe([cte]);
        (await IdsAsync(client, "title=*NF-e")).ShouldBe([nfe]);
        (await IdsAsync(client, "title=*fiscal*ct-e*")).ShouldBe([cte]);
        (await IdsAsync(client, "title=fiscal*")).ShouldBeEmpty();
        (await IdsAsync(client, "title=*100%25*")).ShouldBe([rate]);
        (await IdsAsync(client, "title=*e_x")).ShouldBe([rate]);
        (await IdsAsync(client, "title=*e_y")).ShouldBeEmpty();
        (await IdsAsync(client, "title=taxa%20de%20100%25%20e_x")).ShouldBe([rate]);
        (await IdsAsync(client, "title=taxa%25")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_RejectATitleFilterOfMoreThan300Characters()
    {
        var response = await Client().GetAsync($"/api/v1/documents?title={new string('a', 301)}");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
    }
}
