using System.Text.Json;
using Bogus;
using DocRag.Ingestion.Domain.Documents;
using DocRag.Ingestion.Domain.MasterData;

namespace DocRag.Ingestion.Domain.Tests.Documents;

internal static class DocumentTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Faker Faker = new() { Random = new Randomizer(1234) };

    public static DocumentBuilder ValidBuilder() => DocumentBuilder.New()
        .WithPublicId(Guid.CreateVersion7(Now))
        .WithTitle(Faker.Lorem.Sentence(3))
        .WithDescription(Faker.Lorem.Sentence())
        .OfType(Type())
        .InLanguage(Language())
        .WithTags([Faker.Lorem.Word(), "cte"])
        .WithMetadata(Json("""{"publisher":"ENCAT","nested":{"a":1,"b":2}}"""));

    public static DocumentType Type(string name = "fiscal-manual") =>
        DocumentType.Builder().WithPublicId(Guid.CreateVersion7(Now)).Named(name).Build(Now).Value;

    public static DocumentLanguage Language(string code = "pt-BR") =>
        DocumentLanguage.Builder().WithPublicId(Guid.CreateVersion7(Now)).WithCode(code).Build(Now).Value;

    public static Document ValidDocument() => ValidBuilder().Build(Now).Value;

    public static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
