using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class SluggerTests
{
    [Theory]
    [InlineData("Galaxy S25 Ultra", "galaxy-s25-ultra")]
    [InlineData("Redmi Note 14 Pro 5G", "redmi-note-14-pro-5g")]
    [InlineData("Moto G (2026)", "moto-g-2026")]
    public void Slugify_NormalizesModelNames(string input, string expected)
    {
        Assert.Equal(expected, Slugger.Slugify(input));
    }
}
