using System.Text.Json;
using Bogus;
using DocRag.Ingestion.Domain.Documents;

namespace DocRag.Ingestion.Domain.Tests.Documents;

internal static class DocumentTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Faker Faker = new() { Random = new Randomizer(1234) };

    public static DocumentBuilder ValidBuilder() => DocumentBuilder.New()
        .WithPublicId(Guid.CreateVersion7(Now))
        .WithTitle(Faker.Lorem.Sentence(3))
        .WithDescription(Faker.Lorem.Sentence())
        .OfType("fiscal-manual")
        .InLanguage("pt-BR")
        .WithTags([Faker.Lorem.Word(), "cte"])
        .WithMetadata(Json("""{"publisher":"ENCAT","nested":{"a":1,"b":2}}"""));

    public static Document ValidDocument() => ValidBuilder().Build(Now).Value;

    public static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
