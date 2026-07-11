using System.Reflection;
using SpecTen.Web.Data;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class CatalogReadinessTests
{
    [Fact]
    public void Evaluate_ReturnsPublicReady_ForLegacyFeaturePhoneWithoutChipset_WhenCoreSpecsArePresent()
    {
        SpecGroupDto[] specGroups =
        [
            Group("Mercado", Spec("network", "Rede", "GSM / HSPA / LTE")),
            Group("Tela",
                Spec("display_size", "Tamanho da tela", "2.4 in"),
                Spec("display_type", "Painel", "TFT")),
            Group("Camera", Spec("main_camera", "Camera principal", "2 MP")),
            Group("Bateria", Spec("battery", "Bateria", "1200 mAh")),
            Group("Construcao",
                Spec("dimensions", "Dimensoes", "117 x 52.4 x 13.4 mm"),
                Spec("weight", "Peso", "88.1 g"),
                Spec("sim", "SIM / eSIM", "Micro-SIM")),
            Group("Conectividade",
                Spec("wifi", "Wi-Fi", "Wi-Fi 802.11 b/g/n"),
                Spec("bluetooth", "Bluetooth", "4.0"),
                Spec("usb", "USB", "microUSB 2.0")),
            Group("Armazenamento",
                Spec("storage_options", "Opcoes de armazenamento", "512MB 256MB RAM"),
                Spec("card_slot", "Cartao de memoria", "microSDXC")),
        ];

        var readiness = CatalogReadiness.Evaluate(
            "https://fdn2.gsmarena.com/vv/bigpic/nokia-3310-2017-4g.jpg",
            new DateTimeOffset(2018, 2, 1, 0, 0, 0, TimeSpan.Zero),
            specGroups);

        Assert.True(readiness.IsPublicReady);
        Assert.True(readiness.UsesEntryFallbackTier);
    }

    [Fact]
    public void Evaluate_ReturnsPublicReady_ForLegacyVoicePhoneWithoutCamera_WhenCoreSpecsArePresent()
    {
        SpecGroupDto[] specGroups =
        [
            Group("Mercado", Spec("network", "Rede", "GSM")),
            Group("Tela",
                Spec("display_type", "Painel", "Monochrome graphic"),
                Spec("resolution", "Resolucao", "5 lines")),
            Group("Bateria", Spec("battery", "Bateria", "900 mAh")),
            Group("Construcao",
                Spec("dimensions", "Dimensoes", "113 x 48 x 22 mm"),
                Spec("weight", "Peso", "133 g"),
                Spec("sim", "SIM / eSIM", "Mini-SIM")),
            Group("Conectividade",
                Spec("wifi", "Wi-Fi", "No"),
                Spec("bluetooth", "Bluetooth", "No"),
                Spec("radio", "Radio", "No")),
        ];

        var readiness = CatalogReadiness.Evaluate(
            "https://fdn2.gsmarena.com/vv/bigpic/no3310b.gif",
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            specGroups);

        Assert.True(readiness.IsPublicReady);
        Assert.True(readiness.UsesEntryFallbackTier);
    }

    [Fact]
    public void Evaluate_KeepsIncompleteSmartphoneAsNotReady_WhenPlaceholderImageAndMissingBatteryRemain()
    {
        SpecGroupDto[] specGroups =
        [
            Group("Performance", Spec("chipset", "Chipset", "Snapdragon 820")),
            Group("Tela", Spec("display_size", "Tamanho da tela", "4.6 in")),
            Group("Camera", Spec("main_camera", "Camera principal", "12 MP")),
        ];

        var readiness = CatalogReadiness.Evaluate(
            "https://fdn2.gsmarena.com/vv/bigpic/smartphone.jpg",
            null,
            specGroups);

        Assert.False(readiness.IsPublicReady);
        Assert.False(readiness.UsesEntryFallbackTier);
    }

    [Fact]
    public void Evaluate_KeepsRecentSingleSourceSmartphoneAsNotReady_UntilConsensusOrOfficialSourceExists()
    {
        SpecGroupDto[] specGroups =
        [
            Group("Performance", Spec("chipset", "Chipset", "Unisoc T7200 (12 nm)", isCritical: true)),
            Group("Memoria", Spec("ram", "RAM", "4 GB", isCritical: true)),
            Group("Tela",
                Spec("display_size", "Tamanho da tela", "6.75 in", isCritical: true),
                Spec("display_type", "Painel", "IPS LCD"),
                Spec("resolution", "Resolucao", "1600 x 720"),
                Spec("refresh_rate", "Taxa de atualizacao", "90 Hz")),
            Group("Camera",
                Spec("main_camera", "Camera principal", "13 MP", isCritical: true),
                Spec("selfie_camera", "Camera frontal", "8 MP")),
            Group("Bateria",
                Spec("battery", "Bateria", "5000 mAh", isCritical: true),
                Spec("charging", "Carregamento", "10 W")),
            Group("Construcao",
                Spec("dimensions", "Dimensoes", "167.7 x 77.4 x 8.5 mm"),
                Spec("weight", "Peso", "193 g"),
                Spec("sim", "SIM / eSIM", "Dual Nano-SIM")),
            Group("Conectividade",
                Spec("wifi", "Wi-Fi", "Wi-Fi 5"),
                Spec("bluetooth", "Bluetooth", "5.2"),
                Spec("usb", "USB", "USB-C"),
                Spec("network", "Rede", "GSM / HSPA / LTE")),
            Group("Software", Spec("os", "Sistema", "Android 15")),
        ];

        var readiness = CatalogReadiness.Evaluate(
            "https://fdn2.gsmarena.com/vv/bigpic/zte-blade-a56-.jpg",
            new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero),
            specGroups);

        Assert.False(readiness.IsPublicReady);
        Assert.Equal("Fonte unica", readiness.TrustLabel);
        Assert.Contains("fonte unica", readiness.ReadinessNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_AllowsRecentRichSingleSourceSmartphone_WhenCoverageIsDeepAndConfidenceIsHigh()
    {
        SpecGroupDto[] specGroups =
        [
            Group("Performance",
                Spec("chipset", "Chipset", "Unisoc T7200 (12 nm)", confidence: 0.78, isCritical: true),
                Spec("cpu", "CPU", "Octa-core", confidence: 0.79),
                Spec("gpu", "GPU", "Mali-G57", confidence: 0.79)),
            Group("Memoria",
                Spec("ram", "RAM", "8 GB", confidence: 0.78, isCritical: true),
                Spec("storage_base", "Armazenamento base", "256 GB", confidence: 0.78),
                Spec("storage_options", "Opcoes de armazenamento", "256 GB", confidence: 0.78),
                Spec("card_slot", "Cartao de memoria", "microSDXC", confidence: 0.79)),
            Group("Tela",
                Spec("display_size", "Tamanho da tela", "6.6 in", confidence: 0.78, isCritical: true),
                Spec("display_type", "Painel", "IPS LCD", confidence: 0.79),
                Spec("resolution", "Resolucao", "1612 x 720", confidence: 0.79),
                Spec("refresh_rate", "Taxa de atualizacao", "90 Hz", confidence: 0.79)),
            Group("Camera",
                Spec("main_camera", "Camera principal", "50 MP", confidence: 0.78, isCritical: true),
                Spec("ultrawide_camera", "Ultra-wide", "2 MP", confidence: 0.79),
                Spec("selfie_camera", "Camera frontal", "8 MP", confidence: 0.79),
                Spec("camera_features", "Recursos da camera", "LED flash, HDR", confidence: 0.79),
                Spec("main_camera_video", "Video principal", "1440p", confidence: 0.79)),
            Group("Bateria",
                Spec("battery", "Bateria", "10300 mAh", confidence: 0.78, isCritical: true),
                Spec("charging", "Carregamento", "18 W", confidence: 0.79)),
            Group("Construcao",
                Spec("dimensions", "Dimensoes", "174.2 x 81.3 x 15.8 mm", confidence: 0.79),
                Spec("weight", "Peso", "356 g", confidence: 0.79),
                Spec("sim", "SIM / eSIM", "Dual Nano-SIM", confidence: 0.79),
                Spec("ip_rating", "Resistencia", "IP68/IP69K", confidence: 0.79)),
            Group("Conectividade",
                Spec("network", "Rede", "GSM / HSPA / LTE", confidence: 0.79),
                Spec("wifi", "Wi-Fi", "Wi-Fi 5", confidence: 0.79),
                Spec("bluetooth", "Bluetooth", "5.2", confidence: 0.79),
                Spec("positioning", "Localizacao", "GPS, GALILEO, GLONASS", confidence: 0.79),
                Spec("nfc", "NFC", "Yes", confidence: 0.79),
                Spec("usb", "USB", "USB-C", confidence: 0.79)),
            Group("Software",
                Spec("os", "Sistema", "Android 15", confidence: 0.79),
                Spec("sensors", "Sensores", "Fingerprint", confidence: 0.79)),
            Group("Mercado",
                Spec("colors", "Cores", "Black", confidence: 0.79),
                Spec("models", "Modelos", "Blade20", confidence: 0.79)),
        ];

        var readiness = CatalogReadiness.Evaluate(
            "https://fdn2.gsmarena.com/vv/bigpic/doogee-blade20.jpg",
            new DateTimeOffset(2025, 4, 30, 0, 0, 0, TimeSpan.Zero),
            specGroups);

        Assert.True(readiness.IsPublicReady);
        Assert.Equal("Fonte unica", readiness.TrustLabel);
        Assert.Contains("fonte unica dominante", readiness.ReadinessNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_AllowsRecentSmartphone_WhenCriticalSpecsHaveConsensusAcrossSources()
    {
        SpecGroupDto[] specGroups =
        [
            Group("Performance",
                Spec("chipset", "Chipset", "MediaTek Dimensity 9400+", sourceName: "Official", confidence: 0.94, isCritical: true),
                Spec("chipset", "Chipset", "MediaTek Dimensity 9400+", sourceName: "GSMArena", confidence: 0.92, isCritical: true)),
            Group("Memoria", Spec("ram", "RAM", "12 GB", sourceName: "Official", confidence: 0.94, isCritical: true)),
            Group("Tela",
                Spec("display_size", "Tamanho da tela", "6.83 in", sourceName: "Official", confidence: 0.94, isCritical: true),
                Spec("display_type", "Painel", "AMOLED", sourceName: "GSMArena", confidence: 0.92),
                Spec("resolution", "Resolucao", "2772 x 1280", sourceName: "Official", confidence: 0.94),
                Spec("refresh_rate", "Taxa de atualizacao", "144 Hz", sourceName: "GSMArena", confidence: 0.9)),
            Group("Camera",
                Spec("main_camera", "Camera principal", "50 MP", sourceName: "Official", confidence: 0.94, isCritical: true),
                Spec("selfie_camera", "Camera frontal", "32 MP", sourceName: "GSMArena", confidence: 0.9)),
            Group("Bateria",
                Spec("battery", "Bateria", "5500 mAh", sourceName: "Official", confidence: 0.94, isCritical: true),
                Spec("charging", "Carregamento", "90 W", sourceName: "GSMArena", confidence: 0.9)),
            Group("Construcao",
                Spec("dimensions", "Dimensoes", "161.3 x 75.3 x 8.4 mm", sourceName: "Official", confidence: 0.94),
                Spec("weight", "Peso", "209 g", sourceName: "Official", confidence: 0.94),
                Spec("sim", "SIM / eSIM", "Dual Nano-SIM", sourceName: "GSMArena", confidence: 0.9)),
            Group("Conectividade",
                Spec("wifi", "Wi-Fi", "Wi-Fi 7", sourceName: "Official", confidence: 0.94),
                Spec("bluetooth", "Bluetooth", "5.4", sourceName: "Official", confidence: 0.94),
                Spec("usb", "USB", "USB-C 3.2", sourceName: "GSMArena", confidence: 0.9),
                Spec("network", "Rede", "GSM / HSPA / LTE / 5G", sourceName: "GSMArena", confidence: 0.9)),
            Group("Software", Spec("os", "Sistema", "Android 15", sourceName: "Official", confidence: 0.94)),
        ];

        var readiness = CatalogReadiness.Evaluate(
            "https://example.com/xiaomi-15t-pro.jpg",
            new DateTimeOffset(2025, 9, 26, 0, 0, 0, TimeSpan.Zero),
            specGroups);

        Assert.True(readiness.IsPublicReady);
        Assert.Equal("Fonte oficial", readiness.TrustLabel);
    }

    [Fact]
    public void Evaluate_AllowsDeepLegacyVoicePhoneWithoutBattery_WhenCoverageIsStillBroad()
    {
        SpecGroupDto[] specGroups =
        [
            Group("Mercado",
                Spec("network", "Rede", "GSM"),
                Spec("colors", "Cores", "User exchangeable front and back covers")),
            Group("Armazenamento", Spec("card_slot", "Cartao de memoria", "No")),
            Group("Tela",
                Spec("display_type", "Painel", "Monochrome graphic"),
                Spec("resolution", "Resolucao", "5 lines")),
            Group("Construcao",
                Spec("dimensions", "Dimensoes", "123.8 x 50.5 x 16.7-22.5 mm"),
                Spec("weight", "Peso", "151 g"),
                Spec("sim", "SIM / eSIM", "Mini-SIM")),
            Group("Conectividade",
                Spec("wifi", "Wi-Fi", "No"),
                Spec("bluetooth", "Bluetooth", "No"),
                Spec("loudspeaker", "Alto-falante", "No"),
                Spec("headphone_jack", "Entrada 3.5 mm", "No"),
                Spec("positioning", "Localizacao", "No"),
                Spec("radio", "Radio", "No")),
        ];

        var readiness = CatalogReadiness.Evaluate(
            "https://fdn2.gsmarena.com/vv/bigpic/no3210b.gif",
            null,
            specGroups);

        Assert.True(readiness.IsPublicReady);
        Assert.True(readiness.UsesEntryFallbackTier);
        Assert.Equal("Fonte unica", readiness.TrustLabel);
    }

    [Fact]
    public void CatalogService_ToDetails_UsesEntryFallbackTier_ForLegacyVoicePhone()
    {
        var phone = new PhoneModel
        {
            Id = 3310,
            Brand = new Brand
            {
                Id = 1,
                Name = "Nokia",
                Slug = "nokia",
            },
            BrandId = 1,
            Name = "3310",
            Slug = "3310",
            ImageUrl = "https://fdn2.gsmarena.com/vv/bigpic/no3310b.gif",
            ImageSourceUrl = "https://www.gsmarena.com/nokia_3310-192.php",
            ReleasedAt = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Specs =
            [
                EntitySpec("Mercado", "network", "Rede", "GSM"),
                EntitySpec("Tela", "display_type", "Painel", "Monochrome graphic"),
                EntitySpec("Tela", "resolution", "Resolucao", "5 lines"),
                EntitySpec("Bateria", "battery", "Bateria", "900 mAh", isCritical: true),
                EntitySpec("Construcao", "dimensions", "Dimensoes", "113 x 48 x 22 mm"),
                EntitySpec("Construcao", "weight", "Peso", "133 g"),
                EntitySpec("Construcao", "sim", "SIM / eSIM", "Mini-SIM"),
                EntitySpec("Conectividade", "wifi", "Wi-Fi", "No"),
                EntitySpec("Conectividade", "bluetooth", "Bluetooth", "No"),
                EntitySpec("Conectividade", "radio", "Radio", "No"),
            ],
        };

        var method = typeof(CatalogService).GetMethod("ToDetails", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var details = Assert.IsType<PhoneDetailsDto>(method!.Invoke(null, [phone]));

        Assert.True(details.IsPublicReady);
        Assert.Equal(ClassificationTier.Entry, details.Classification.Tier);
        Assert.Equal("Entrada", details.Classification.Label);
        Assert.Equal("legacy-profile", details.Classification.Basis);
        Assert.Equal("Fonte unica", details.TrustLabel);
    }

    private static SpecGroupDto Group(string name, params SpecFactDto[] specs) => new(name, specs);

    private static SpecFactDto Spec(
        string key,
        string displayName,
        string displayValue,
        string sourceName = "GSMArena",
        double confidence = 0.62,
        SpecStatus status = SpecStatus.Published,
        bool isCritical = false)
        => new(
            key,
            displayName,
            displayValue,
            null,
            sourceName,
            "https://www.gsmarena.com/example.php",
            confidence,
            status,
            isCritical,
            DateTimeOffset.UtcNow);

    private static SpecFact EntitySpec(string group, string key, string displayName, string displayValue, bool isCritical = false)
        => new()
        {
            Group = group,
            Key = key,
            DisplayName = displayName,
            DisplayValue = displayValue,
            NormalizedValue = displayValue,
            SourceName = "GSMArena",
            SourceUrl = "https://www.gsmarena.com/example.php",
            Confidence = 0.62,
            Status = SpecStatus.Published,
            IsCritical = isCritical,
            CollectedAt = DateTimeOffset.UtcNow,
        };
}
