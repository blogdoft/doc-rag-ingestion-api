using DocRag.Ingestion.Domain.Versions;
using Shouldly;

namespace DocRag.Ingestion.Domain.Tests.Versions;

public class DocumentVersionBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static DocumentVersionBuilder ValidBuilder() => DocumentVersionBuilder.New()
        .WithPublicId(Guid.CreateVersion7(Now))
        .ForDocument(10, Guid.CreateVersion7(Now))
        .WithLabel("4.00");

    [Fact]
    public void Should_StartAwaitingUploadWithoutInferringDates_When_OnlyLabelIsProvided()
    {
        var builder = ValidBuilder();

        var result = builder.Build(Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(IngestionStatus.AwaitingUpload);
        result.Value.PublishedAt.ShouldBeNull();
        result.Value.EffectiveFrom.ShouldBeNull();
        result.Value.EffectiveTo.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Should_ReturnFailure_When_LabelIsMissing(string? label)
    {
        var builder = ValidBuilder().WithLabel(label);

        var result = builder.Build(Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-version-invalid");
        result.Failure.Message.ShouldContain("label is required");
    }

    [Fact]
    public void Should_ReturnFailure_When_EffectiveToPrecedesEffectiveFrom()
    {
        var builder = ValidBuilder().WithDates(null, Now, Now.AddDays(-1));

        var result = builder.Build(Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Message.ShouldContain("effectiveTo");
    }

    [Fact]
    public void Should_Succeed_When_OnlyOneEffectiveBoundIsProvided()
    {
        var builder = ValidBuilder().WithDates(null, null, Now);

        var result = builder.Build(Now);

        result.IsSuccess.ShouldBeTrue();
    }
}
