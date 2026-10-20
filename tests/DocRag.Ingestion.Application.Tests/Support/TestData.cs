using System.Text.Json;
using Bogus;
using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Domain.Documents;
using DocRag.Ingestion.Domain.Files;
using DocRag.Ingestion.Domain.MasterData;
using DocRag.Ingestion.Domain.Versions;

namespace DocRag.Ingestion.Application.Tests.Support;

public static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid TypeId = Guid.Parse("0199a8bb-7bc4-7000-8000-4d981eb52c93");

    public static readonly Guid LanguageId = Guid.Parse("0199a8bb-7bc4-7000-8000-4d981eb52c94");

    public static Faker<CreateDocumentCommand> CreateDocumentCommand(string? idempotencyKey = null) =>
        new Faker<CreateDocumentCommand>()
            .UseSeed(42)
            .CustomInstantiator(f => new CreateDocumentCommand(
                idempotencyKey,
                f.Lorem.Sentence(3),
                f.Lorem.Sentence(),
                TypeId,
                LanguageId,
                [f.Lorem.Word(), f.Lorem.Word() + "-x"],
                JsonSerializer.Deserialize<JsonElement>("""{"publisher":"ENCAT"}""")));

    public static DocumentType DocumentType()
    {
        var type = Domain.MasterData.DocumentType.Rehydrate(1, TypeId, "fiscal-manual", Now, Now);
        return type;
    }

    public static DocumentLanguage DocumentLanguage() => Domain.MasterData.DocumentLanguage.Rehydrate(2, LanguageId, "pt-BR", Now, Now);

    public static Document Document(long id = 10)
    {
        var document = DocumentBuilder.New()
            .WithPublicId(Guid.CreateVersion7(Now))
            .WithTitle("Manual")
            .OfType(DocumentType())
            .InLanguage(DocumentLanguage())
            .Build(Now).Value;
        document.AssignId(id);
        return document;
    }

    public static DocumentVersion Version(Document document, string label = "4.00", long id = 20)
    {
        var version = DocumentVersionBuilder.New()
            .WithPublicId(Guid.CreateVersion7(Now))
            .ForDocument(document.Id, document.PublicId)
            .WithLabel(label)
            .Build(Now).Value;
        version.AssignId(id);
        return version;
    }

    public static DocumentFile File(DocumentVersion version, string sha256 = "")
    {
        var file = DocumentFile.Create(
            Guid.CreateVersion7(Now),
            version.Id,
            "manual.pdf",
            "application/pdf",
            21,
            sha256.Length == 64 ? sha256 : new string('b', 64),
            "bucket",
            "key",
            Now).Value;
        file.AssignId(30);
        return file;
    }

    public static UploadAttempt Attempt(long id = 40)
    {
        var attempt = UploadAttempt.Start(Guid.CreateVersion7(Now), 20, Guid.CreateVersion7(Now), $"key-{id}", Now, TimeSpan.FromMinutes(30));
        attempt.AssignId(id);
        return attempt;
    }
}
