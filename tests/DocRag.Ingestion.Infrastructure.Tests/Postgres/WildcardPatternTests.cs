using DocRag.Ingestion.Infrastructure.Postgres.Repositories;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.Postgres;

public class WildcardPatternTests
{
    [Theory]
    [InlineData("manual", "manual")]
    [InlineData("manual*", "manual%")]
    [InlineData("*cte", "%cte")]
    [InlineData("*fiscal*cte*", "%fiscal%cte%")]
    [InlineData("*manual*", "%manual%")]
    [InlineData("a**b", "a%b")]
    [InlineData("*", "%")]
    public void Should_TranslateAsterisksAndKeepTheRestAsAnEquality(string search, string expected) =>
        WildcardPattern.ToLikePattern(search).ShouldBe(expected);

    [Theory]
    [InlineData("100%", "100\\%")]
    [InlineData("a_b*", "a\\_b%")]
    [InlineData("c:\\dir", "c:\\\\dir")]
    public void Should_EscapeLikeMetacharacters_When_TheyAppearInTheSearch(string search, string expected) =>
        WildcardPattern.ToLikePattern(search).ShouldBe(expected);
}
