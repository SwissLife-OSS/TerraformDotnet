using TerraformDotnet.Naming;

namespace TerraformDotnet.Tests.Naming;

public class NameFormatTests
{
    private static readonly NameFormat Hyphens = NameFormat.Custom("^[a-z0-9-]+$");

    [Theory]
    [InlineData("web", true)]
    [InlineData("web_app_2", true)]
    [InlineData("web_2_app", true)]
    [InlineData("webApp", false)]
    [InlineData("web-app", false)]
    [InlineData("web__app", false)]
    [InlineData("_web", false)]
    [InlineData("web_", false)]
    [InlineData("2web", false)]
    [InlineData("", false)]
    public void SnakeCaseMatchesLikeTflint(string name, bool expected)
    {
        Assert.Equal(expected, NameFormat.SnakeCase.IsMatch(name));
    }

    [Theory]
    [InlineData("Web_App", true)]
    [InlineData("webApp", true)]
    [InlineData("web-app", false)]
    [InlineData("_Web", false)]
    public void MixedSnakeCaseMatchesLikeTflint(string name, bool expected)
    {
        Assert.Equal(expected, NameFormat.MixedSnakeCase.IsMatch(name));
    }

    [Theory]
    [InlineData("web-app-2", true)]
    [InlineData("web_app", false)]
    [InlineData("web--app", false)]
    [InlineData("Web-App", false)]
    public void KebabCaseMatches(string name, bool expected)
    {
        Assert.Equal(expected, NameFormat.KebabCase.IsMatch(name));
    }

    [Fact]
    public void NoneMatchesEveryName()
    {
        Assert.False(NameFormat.None.IsChecked);
        Assert.Null(NameFormat.None.Pattern);
        Assert.True(NameFormat.None.IsMatch("Any.Thing at all"));
    }

    [Theory]
    [InlineData("web_app", "web_app")]
    [InlineData("web-app", "web_app")]
    [InlineData("Web.App", "web_app")]
    [InlineData("  web   app  ", "web_app")]
    [InlineData("web--app__api", "web_app_api")]
    [InlineData("-web-", "web")]
    [InlineData("MyApp", "myapp")]
    [InlineData("café", "caf")]
    public void SnakeCaseFormatsNames(string name, string expected)
    {
        Assert.Equal(expected, NameFormat.SnakeCase.Format(name));
    }

    [Theory]
    [InlineData("web_app", "web-app")]
    [InlineData("Web.App", "web-app")]
    [InlineData("MyApp", "myapp")]
    public void KebabCaseFormatsNames(string name, string expected)
    {
        Assert.Equal(expected, NameFormat.KebabCase.Format(name));
    }

    [Theory]
    [InlineData("Web_App", "Web_App")]
    [InlineData("Web-App", "Web_App")]
    [InlineData("My.App", "My_App")]
    public void MixedSnakeCaseKeepsTheCase(string name, string expected)
    {
        Assert.Equal(expected, NameFormat.MixedSnakeCase.Format(name));
    }

    [Fact]
    public void FormatJoinsPartsWithTheSeparatorOfTheStyle()
    {
        Assert.Equal("dns_web_app", NameFormat.SnakeCase.Format("dns", "web-app"));
        Assert.Equal("dns-web-app", NameFormat.KebabCase.Format("dns", "web_app"));
        Assert.Equal("dns-web-app", Hyphens.Format("dns", "web_app"));
    }

    [Fact]
    public void FormatSkipsEmptyParts()
    {
        Assert.Equal("dns_web", NameFormat.SnakeCase.Format(null, "dns", "", "web"));
    }

    [Theory]
    [InlineData("my--app")]
    [InlineData("app-")]
    [InlineData("a-1-b")]
    public void FormatKeepsANameThatAlreadyConforms(string name)
    {
        // The pattern allows what normalizing would change; deployed names must stay as they are.
        Assert.Equal(name, Hyphens.Format(name));
    }

    [Fact]
    public void FormatNormalizesANameThatMatchesButIsNoIdentifier()
    {
        NameFormat anything = NameFormat.Custom(".*", NameStyle.SnakeCase);

        Assert.Equal("my_app", anything.Format("My.App"));
    }

    [Fact]
    public void UncheckedFormatKeepsIdentifiersAndNormalizesOtherNames()
    {
        NameFormat format = NameFormat.Unchecked(NameStyle.KebabCase);

        Assert.Equal("My_App-1", format.Format("My_App-1"));
        Assert.Equal("my-app", format.Format("My.App"));
        Assert.Equal("rg-My_App", format.Format("rg", "My_App"));
    }

    [Fact]
    public void FormatDoesNotAddCharacters()
    {
        string name = NameFormat.SnakeCase.Format("2", "web");

        Assert.Equal("2_web", name);
        Assert.False(NameFormat.SnakeCase.IsMatch(name));
    }

    [Theory]
    [InlineData]
    [InlineData("")]
    [InlineData("--", "..")]
    public void FormatThrowsWithoutLettersOrDigits(params string[] parts)
    {
        Assert.Throws<ArgumentException>(() => NameFormat.SnakeCase.Format(parts));
    }

    [Theory]
    [InlineData("^[a-z0-9_]+$", NameStyle.SnakeCase)]
    [InlineData("^[a-z0-9_-]+$", NameStyle.SnakeCase)]
    [InlineData("^[a-z0-9-]+$", NameStyle.KebabCase)]
    [InlineData("^[A-Z][a-zA-Z0-9_]*$", NameStyle.MixedSnakeCase)]
    [InlineData("^[a-zA-Z]+([_-][a-zA-Z]+)*$", NameStyle.SnakeCase)]
    [InlineData("^sl[0-9]+$", NameStyle.SnakeCase)]
    public void CustomInfersTheStyleFromThePattern(string pattern, NameStyle expected)
    {
        Assert.Equal(expected, NameFormat.Custom(pattern).Style);
    }

    [Fact]
    public void CustomUsesTheGivenStyle()
    {
        NameFormat format = NameFormat.Custom("^[a-z0-9_-]+$", NameStyle.KebabCase);

        Assert.Equal("web-app", format.Format("Web App"));
    }

    [Fact]
    public void CustomIsNamedAfterItsDescriptionOrPattern()
    {
        Assert.Equal("^[a-z0-9-]+$", Hyphens.Name);
        Assert.Equal("^[a-z0-9-]+$", Hyphens.Pattern);
        Assert.Equal("Hyphens", NameFormat.Custom("^[a-z0-9-]+$", description: "Hyphens").ToString());
    }

    [Fact]
    public void CustomMatchesAnywhereLikeTflint()
    {
        Assert.True(NameFormat.Custom("[a-z]+").IsMatch("123abc456"));
    }

    [Fact]
    public void CustomAcceptsPatternsThatNeedBacktracking()
    {
        NameFormat format = NameFormat.Custom("^(?!tmp)[a-z_]+$");

        Assert.True(format.IsMatch("web_app"));
        Assert.False(format.IsMatch("tmp_app"));
    }

    [Fact]
    public void CustomThrowsForAnInvalidPattern()
    {
        Assert.ThrowsAny<ArgumentException>(() => NameFormat.Custom("^[a-z"));
    }

    [Fact]
    public void FormatsAreEqualByValue()
    {
        Assert.Equal(NameFormat.Custom("^[a-z0-9-]+$"), Hyphens);
        Assert.Equal(NameFormat.Custom("^[a-z0-9-]+$").GetHashCode(), Hyphens.GetHashCode());
        Assert.NotEqual(NameFormat.Custom("^[a-z0-9-]+$", NameStyle.SnakeCase), Hyphens);
        Assert.NotEqual(NameFormat.Custom("^[a-z0-9-]+$", description: "Hyphens"), Hyphens);
        Assert.Equal(NameFormat.Unchecked(NameStyle.SnakeCase), NameFormat.None);
        Assert.NotEqual(NameFormat.KebabCase, NameFormat.SnakeCase);
    }
}
