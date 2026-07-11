using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class PhoneNameFormatterTests
{
    [Theory]
    [InlineData("Qualcomm SM8650-AB Snapdragon 8 Gen 3 (4 nm)", "Snapdragon 8 Gen 3")]
    [InlineData("MediaTek Dimensity 9400+ (3 nm)", "Dimensity 9400+")]
    [InlineData("Snapdragon 8 Elite Mobile Platform", "Snapdragon 8 Elite")]
    [InlineData("Apple A18 Pro", "Apple A18 Pro")]
    public void CompactChipset_RemovesNoiseButKeepsRecognizableLabel(string chipset, string expected)
    {
        var compact = PhoneNameFormatter.CompactChipset(chipset);

        Assert.Equal(expected, compact);
    }

    [Theory]
    [InlineData("Vivo", "iQOO Neo 10", "iQOO", "Neo 10", "iQOO Neo 10")]
    [InlineData("Xiaomi", "Poco F7 Pro", "POCO", "F7 Pro", "POCO F7 Pro")]
    [InlineData("Xiaomi", "Redmi Note 14 Pro 5G", "Redmi", "Note 14 Pro 5G", "Redmi Note 14 Pro 5G")]
    [InlineData("ZTE", "nubia RedMagic 10 Pro", "RedMagic", "10 Pro", "RedMagic 10 Pro")]
    public void Formatter_PresentsSubBrandsCleanlyForPublicUi(
        string brand,
        string name,
        string expectedDisplayBrand,
        string expectedModel,
        string expectedFullName)
    {
        Assert.Equal(expectedDisplayBrand, PhoneNameFormatter.DisplayBrand(brand, name));
        Assert.Equal(expectedModel, PhoneNameFormatter.ModelName(brand, name));
        Assert.Equal(expectedFullName, PhoneNameFormatter.FullName(brand, name));
    }
}
