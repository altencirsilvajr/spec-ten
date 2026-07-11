namespace SpecTen.Web.Scraping;

public abstract class FixturePhoneSourceAdapter : IPhoneSourceAdapter
{
    public abstract string SourceName { get; }
    public virtual string PolicyStatus => "FixtureOnly";
    public virtual bool RobotsAllowed => false;
    public virtual bool IsOfficialSource => false;

    public Task<IReadOnlyList<SourcePhoneRecord>> FetchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildRecords());
    }

    protected abstract IReadOnlyList<SourcePhoneRecord> BuildRecords();

    protected SourcePhoneRecord Record(
        string sourceUrl,
        bool official,
        string brandName,
        string? officialDomain,
        string modelName,
        string? summary,
        DateTimeOffset? releasedAt,
        decimal? launchPriceUsd,
        string? imageUrl,
        string? imageSourceUrl,
        IReadOnlyList<SourceSpecClaim> specs,
        IReadOnlyList<SourceVariantClaim> variants,
        IReadOnlyList<SourceBenchmarkClaim> benchmarks)
    {
        return new SourcePhoneRecord(
            SourceName,
            sourceUrl,
            PolicyStatus,
            RobotsAllowed,
            official,
            brandName,
            officialDomain,
            modelName,
            summary,
            releasedAt,
            launchPriceUsd,
            imageUrl,
            imageSourceUrl,
            specs,
            variants,
            benchmarks);
    }

    protected SourceVariantClaim Variant(string name, int? ramGb, int? storageGb, string? color = null)
    {
        return new SourceVariantClaim(name, ramGb, storageGb, color);
    }

    protected SourceBenchmarkClaim Benchmark(string benchmarkName, int score, string sourceUrl)
    {
        return new SourceBenchmarkClaim(benchmarkName, score, SourceName, sourceUrl, DateTimeOffset.UtcNow);
    }

    protected SourceSpecClaim Spec(
        string sourceUrl,
        bool official,
        string group,
        string key,
        string displayName,
        string value,
        string? unit,
        bool critical,
        double confidence)
    {
        return new SourceSpecClaim(
            SourceName,
            sourceUrl,
            official,
            group,
            key,
            displayName,
            value,
            Normalize(value),
            value,
            unit,
            critical,
            confidence,
            DateTimeOffset.UtcNow);
    }

    protected IReadOnlyList<SourceSpecClaim> PublicTechPack(
        string sourceUrl,
        bool official,
        string dimensions,
        string sim,
        string wifi,
        string bluetooth,
        string nfc,
        string usb)
    {
        return
        [
            Spec(sourceUrl, official, "Construcao", "dimensions", "Dimensoes", dimensions, null, false, 0.89),
            Spec(sourceUrl, official, "Conectividade", "sim", "SIM / eSIM", sim, null, false, 0.88),
            Spec(sourceUrl, official, "Conectividade", "wifi", "Wi-Fi", wifi, null, false, 0.9),
            Spec(sourceUrl, official, "Conectividade", "bluetooth", "Bluetooth", bluetooth, null, false, 0.89),
            Spec(sourceUrl, official, "Conectividade", "nfc", "NFC", nfc, null, false, 0.87),
            Spec(sourceUrl, official, "Conectividade", "usb", "USB", usb, null, false, 0.87),
        ];
    }

    protected static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
    }
}

public sealed class OfficialFixtureAdapter : FixturePhoneSourceAdapter
{
    public override string SourceName => "Official";
    public override bool IsOfficialSource => true;

    protected override IReadOnlyList<SourcePhoneRecord> BuildRecords()
    {
        return
        [
            GalaxyS25Ultra(),
            GalaxyS25Plus(),
            GalaxyS25(),
            Iphone16ProMax(),
            Iphone16Pro(),
            Iphone16(),
            Xiaomi15TPro(),
            Xiaomi15T(),
            Xiaomi15(),
            RedmiNote14Pro5G(),
            OnePlus13(),
            Pixel9a(),
            NothingPhone3aPro(),
            GalaxyA56(),
            GalaxyA36(),
            Edge60Fusion(),
            GalaxyA16(),
            Redmi14C()
        ];
    }

    private SourcePhoneRecord GalaxyS25Ultra()
    {
        const string url = "https://www.samsung.com/uk/smartphones/galaxy-s25-ultra/specs/";
        return Record(
            url,
            true,
            "Samsung",
            "samsung.com",
            "Galaxy S25 Ultra",
            "Flagship com caneta integrada, cameras versateis e foco total em desempenho.",
            new DateTimeOffset(2025, 1, 22, 0, 0, 0, TimeSpan.Zero),
            1299,
            "https://images.samsung.com/is/image/samsung/assets/uk/2501/smartphones/galaxy-s25-ultra/specs/galaxy-s25-thumbnail-image.jpg?$ORIGIN_JPG$",
            url,
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite for Galaxy", null, true, 0.98),
                Spec(url, true, "Memoria", "ram", "RAM", "12 GB", "GB", true, 0.96),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "256 GB", "GB", false, 0.95),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.9 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "Dynamic AMOLED 2X", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "3120 x 1440", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.96),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "200 MP", "MP", true, 0.97),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "50 MP", "MP", false, 0.95),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "50 MP + 10 MP", "MP", false, 0.94),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "12 MP", "MP", false, 0.94),
                Spec(url, true, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.95),
                Spec(url, true, "Bateria", "charging", "Carregamento", "45 W", "W", true, 0.93),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "15 W", "W", false, 0.84),
                Spec(url, true, "Construcao", "weight", "Peso", "218 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "162.8 x 77.6 x 8.2 mm", "Nano-SIM + eSIM", "Wi-Fi 7", "Bluetooth 5.4", "Sim", "USB-C 3.2"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Gorilla Armor 2", null, false, 0.86),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.95),
                Spec(url, true, "Software", "os", "Sistema", "Android 15 / One UI 7", null, false, 0.88),
            ],
            [
                Variant("12 GB / 256 GB", 12, 256),
                Variant("12 GB / 512 GB", 12, 512),
                Variant("12 GB / 1 TB", 12, 1024),
            ],
            [
                Benchmark("AnTuTu", 2_030_000, url),
            ]);
    }

    private SourcePhoneRecord Iphone16Pro()
    {
        const string url = "https://support.apple.com/en-us/121031";
        return Record(
            url,
            true,
            "Apple",
            "apple.com",
            "iPhone 16 Pro",
            "Modelo Pro compacto com foco em video, camera e acabamento premium em titanio.",
            new DateTimeOffset(2024, 9, 20, 0, 0, 0, TimeSpan.Zero),
            999,
            "https://fdn2.gsmarena.com/vv/bigpic/apple-iphone-16-pro.jpg",
            "https://www.gsmarena.com/apple_iphone_16_pro-13315.php",
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Apple A18 Pro", null, true, 0.98),
                Spec(url, true, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.8),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.97),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.3 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "Super Retina XDR OLED", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2622 x 1206", null, false, 0.95),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "48 MP", "MP", true, 0.97),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "48 MP", "MP", false, 0.96),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "12 MP", "MP", false, 0.96),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "12 MP", "MP", false, 0.96),
                Spec(url, true, "Bateria", "battery", "Bateria", "3582 mAh", "mAh", true, 0.7),
                Spec(url, true, "Bateria", "charging", "Carregamento", "Fast charge (50% in 30 min)", null, false, 0.62),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "25 W", "W", false, 0.9),
                Spec(url, true, "Construcao", "weight", "Peso", "199 g", "g", false, 0.96),
                .. PublicTechPack(url, true, "149.6 x 71.5 x 8.3 mm", "Nano-SIM + eSIM", "Wi-Fi 7", "Bluetooth 5.3", "Sim", "USB-C 3"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Ceramic Shield", null, false, 0.95),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.96),
                Spec(url, true, "Software", "os", "Sistema", "iOS 18", null, false, 0.84),
            ],
            [
                Variant("128 GB", null, 128),
                Variant("256 GB", null, 256),
                Variant("512 GB", null, 512),
                Variant("1 TB", null, 1024),
            ],
            [
                Benchmark("Geekbench multi-core", 8_500, url),
            ]);
    }

    private SourcePhoneRecord Xiaomi15TPro()
    {
        const string url = "https://www.mi.com/global/product/xiaomi-15t-pro/specs/";
        return Record(
            url,
            true,
            "Xiaomi",
            "mi.com",
            "Xiaomi 15T Pro",
            "Flagship da linha T com tela 144 Hz, conjunto Leica e carregamento muito rapido.",
            new DateTimeOffset(2025, 9, 26, 0, 0, 0, TimeSpan.Zero),
            899,
            "https://i02.appmifile.com/mi-com-product/fly-birds/xiaomi-15t-pro/pc/29c2588bd13e274ba7f431c2b0a4aed8.jpg",
            url,
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "MediaTek Dimensity 9400+", null, true, 0.98),
                Spec(url, true, "Memoria", "ram", "RAM", "12 GB", "GB", true, 0.97),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "256 GB", "GB", false, 0.97),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.83 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "AMOLED", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2772 x 1280", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "144 Hz", "Hz", false, 0.96),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.97),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "12 MP", "MP", false, 0.95),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "50 MP", "MP", false, 0.96),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "32 MP", "MP", false, 0.95),
                Spec(url, true, "Bateria", "battery", "Bateria", "5500 mAh", "mAh", true, 0.96),
                Spec(url, true, "Bateria", "charging", "Carregamento", "90 W", "W", true, 0.96),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "50 W", "W", false, 0.95),
                Spec(url, true, "Construcao", "weight", "Peso", "210 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "160.4 x 75.1 x 8.4 mm", "Dual Nano-SIM + eSIM", "Wi-Fi 7", "Bluetooth 5.4", "Sim", "USB-C 2.0"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Gorilla Glass 7i", null, false, 0.93),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.96),
                Spec(url, true, "Software", "os", "Sistema", "Xiaomi HyperOS 2", null, false, 0.95),
            ],
            [
                Variant("12 GB / 256 GB", 12, 256),
                Variant("12 GB / 512 GB", 12, 512),
                Variant("12 GB / 1 TB", 12, 1024),
            ],
            [
                Benchmark("AnTuTu", 2_400_000, url),
            ]);
    }

    private SourcePhoneRecord RedmiNote14Pro5G()
    {
        const string url = "https://www.mi.com/global/product/redmi-note-14-pro-5g/specs/";
        return Record(
            url,
            true,
            "Xiaomi",
            "mi.com",
            "Redmi Note 14 Pro 5G",
            "Intermediario forte com camera de 200 MP, tela 1.5K e resistencia IP68.",
            new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero),
            399,
            "https://fdn2.gsmarena.com/vv/bigpic/xiaomi-redmi-note-14-pro-5g.jpg",
            "https://www.gsmarena.com/xiaomi_redmi_note_14_pro_5g-13364.php",
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Dimensity 7300 Ultra", null, true, 0.96),
                Spec(url, true, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.94),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "256 GB", "GB", false, 0.95),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.67 in", "in", true, 0.96),
                Spec(url, true, "Tela", "display_type", "Painel", "CrystalRes AMOLED", null, false, 0.95),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2712 x 1220", null, false, 0.95),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "200 MP", "MP", true, 0.94),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "8 MP", "MP", false, 0.93),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "2 MP macro", "MP", false, 0.8),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "20 MP", "MP", false, 0.93),
                Spec(url, true, "Bateria", "battery", "Bateria", "5110 mAh", "mAh", true, 0.95),
                Spec(url, true, "Bateria", "charging", "Carregamento", "45 W", "W", true, 0.93),
                Spec(url, true, "Construcao", "weight", "Peso", "190 g", "g", false, 0.93),
                .. PublicTechPack(url, true, "162.3 x 74.4 x 8.4 mm", "Dual Nano-SIM + eSIM", "Wi-Fi 6", "Bluetooth 5.4", "Sim", "USB-C 2.0"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Gorilla Glass Victus 2", null, false, 0.94),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.95),
                Spec(url, true, "Software", "os", "Sistema", "Xiaomi HyperOS", null, false, 0.93),
            ],
            [
                Variant("8 GB / 256 GB", 8, 256),
                Variant("12 GB / 256 GB", 12, 256),
                Variant("12 GB / 512 GB", 12, 512),
            ],
            [
                Benchmark("AnTuTu", 704_404, url),
            ]);
    }

    private SourcePhoneRecord GalaxyA56()
    {
        const string url = "https://www.samsung.com/uk/smartphones/galaxy-a/galaxy-a56-5g-awesome-lightgrey-256gb-sm-a566bzaceub/";
        return Record(
            url,
            true,
            "Samsung",
            "samsung.com",
            "Galaxy A56 5G",
            "Intermediario premium da Samsung com tela grande, IP67 e suporte longo de atualizacoes.",
            new DateTimeOffset(2025, 3, 14, 0, 0, 0, TimeSpan.Zero),
            499,
            "https://images.samsung.com/is/image/samsung/p6pim/uk/sm-a566bzaceub/gallery/uk-galaxy-a56-5g-sm-a566-sm-a566bzaceub-thumb-545178551",
            url,
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Exynos 1580", null, true, 0.95),
                Spec(url, true, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.95),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.94),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.7 in", "in", true, 0.96),
                Spec(url, true, "Tela", "display_type", "Painel", "Super AMOLED", null, false, 0.95),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2340 x 1080", null, false, 0.95),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.95),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "12 MP", "MP", false, 0.95),
                Spec(url, true, "Camera", "telephoto_camera", "Camera auxiliar", "5 MP macro", "MP", false, 0.88),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "12 MP", "MP", false, 0.95),
                Spec(url, true, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.95),
                Spec(url, true, "Bateria", "charging", "Carregamento", "45 W", "W", true, 0.9),
                Spec(url, true, "Construcao", "weight", "Peso", "198 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "162.2 x 77.5 x 7.4 mm", "Nano-SIM + eSIM", "Wi-Fi 6", "Bluetooth 5.3", "Sim", "USB-C 2.0"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Gorilla Glass Victus+", null, false, 0.72),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP67", null, false, 0.94),
                Spec(url, true, "Software", "os", "Sistema", "Android 15 / One UI 7", null, false, 0.9),
            ],
            [
                Variant("8 GB / 128 GB", 8, 128),
                Variant("8 GB / 256 GB", 8, 256),
            ],
            [
                Benchmark("AnTuTu", 820_000, url),
            ]);
    }

    private SourcePhoneRecord Edge60Fusion()
    {
        const string url = "https://en-in.support.motorola.com/app/answers/detail/a_id/187360/~/specifications---motorola-edge-60-fusion";
        return Record(
            url,
            true,
            "Motorola",
            "motorola.com",
            "Edge 60 Fusion",
            "Intermediario com tela pOLED 1.5K, boa bateria e acabamento mais resistente que a media.",
            new DateTimeOffset(2025, 4, 2, 0, 0, 0, TimeSpan.Zero),
            349,
            "https://p1-ofp.static.pub/ShareResource/edge60fusion/edge60fusion.png",
            "https://www.motorola.com/au/en/p/phones/motorola-edge/60-fusion/pmipmhq40mz",
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "MediaTek Dimensity 7400", null, true, 0.95),
                Spec(url, true, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.95),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "256 GB", "GB", false, 0.95),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.67 in", "in", true, 0.95),
                Spec(url, true, "Tela", "display_type", "Painel", "pOLED", null, false, 0.95),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2712 x 1220", null, false, 0.95),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.92),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "13 MP", "MP", false, 0.85),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "32 MP", "MP", false, 0.82),
                Spec(url, true, "Bateria", "battery", "Bateria", "5500 mAh", "mAh", true, 0.95),
                Spec(url, true, "Bateria", "charging", "Carregamento", "68 W", "W", true, 0.95),
                Spec(url, true, "Construcao", "weight", "Peso", "180.1 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "161 x 73 x 7.9 mm", "Dual Nano-SIM + eSIM", "Wi-Fi 6E", "Bluetooth 5.4", "Sim", "USB-C 2.0"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Gorilla Glass 7i", null, false, 0.84),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68 / IP69", null, false, 0.95),
                Spec(url, true, "Software", "os", "Sistema", "Android 15", null, false, 0.95),
            ],
            [
                Variant("8 GB / 256 GB", 8, 256),
                Variant("12 GB / 256 GB", 12, 256),
            ],
            [
                Benchmark("AnTuTu", 690_000, url),
            ]);
    }

    private SourcePhoneRecord GalaxyA16()
    {
        const string url = "https://www.samsung.com/br/smartphones/galaxy-a/galaxy-a16-5g-blue-black-128gb-sm-a166mzkdzto/";
        return Record(
            url,
            true,
            "Samsung",
            "samsung.com",
            "Galaxy A16 5G",
            "Modelo de entrada com tela AMOLED grande, 5G e ficha bem clara para uso diario.",
            new DateTimeOffset(2024, 10, 7, 0, 0, 0, TimeSpan.Zero),
            249,
            "https://images.samsung.com/is/image/samsung/p6pim/br/sm-a166mzkdzto/gallery/br-galaxy-a16-5g-sm-a166-sm-a166mzkdzto-544258979?$624_468_PNG$",
            url,
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Exynos 1330", null, true, 0.86),
                Spec(url, true, "Memoria", "ram", "RAM", "6 GB", "GB", true, 0.92),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.93),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.7 in", "in", true, 0.95),
                Spec(url, true, "Tela", "display_type", "Painel", "Super AMOLED", null, false, 0.95),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2340 x 1080", null, false, 0.95),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "90 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.94),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "5 MP", "MP", false, 0.94),
                Spec(url, true, "Camera", "telephoto_camera", "Camera auxiliar", "2 MP macro", "MP", false, 0.93),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "13 MP", "MP", false, 0.94),
                Spec(url, true, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.95),
                Spec(url, true, "Bateria", "charging", "Carregamento", "25 W", "W", true, 0.86),
                Spec(url, true, "Construcao", "weight", "Peso", "200 g", "g", false, 0.85),
                .. PublicTechPack(url, true, "164.4 x 77.9 x 7.9 mm", "Dual Nano-SIM", "Wi-Fi 5", "Bluetooth 5.3", "Sim", "USB-C 2.0"),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP54", null, false, 0.93),
                Spec(url, true, "Software", "os", "Sistema", "Android 14 / One UI", null, false, 0.76),
            ],
            [
                Variant("6 GB / 128 GB", 6, 128),
                Variant("8 GB / 128 GB", 8, 128),
                Variant("8 GB / 256 GB", 8, 256),
            ],
            [
                Benchmark("AnTuTu", 420_000, url),
            ]);
    }

    private SourcePhoneRecord Redmi14C()
    {
        const string url = "https://www.mi.com/global/product/redmi-14-c/specs/";
        return Record(
            url,
            true,
            "Xiaomi",
            "mi.com",
            "Redmi 14C",
            "Entrada com tela bem grande, bateria duradoura e foco em custo acessivel.",
            new DateTimeOffset(2024, 8, 30, 0, 0, 0, TimeSpan.Zero),
            149,
            "https://i02.appmifile.com/mi-com-product/fly-birds/redmi-14-c/pc/6e63d590b9370e2dc8ac34f3d02302c5.jpg",
            "https://www.mi.com/global/product/redmi-14-c/",
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Helio G81-Ultra", null, true, 0.96),
                Spec(url, true, "Memoria", "ram", "RAM", "4 GB", "GB", true, 0.96),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.96),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.88 in", "in", true, 0.96),
                Spec(url, true, "Tela", "display_type", "Painel", "LCD", null, false, 0.9),
                Spec(url, true, "Tela", "resolution", "Resolucao", "1640 x 720", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.95),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "13 MP", "MP", false, 0.95),
                Spec(url, true, "Bateria", "battery", "Bateria", "5160 mAh", "mAh", true, 0.96),
                Spec(url, true, "Bateria", "charging", "Carregamento", "18 W", "W", true, 0.96),
                Spec(url, true, "Construcao", "weight", "Peso", "211 g", "g", false, 0.94),
                .. PublicTechPack(url, true, "171.9 x 77.8 x 8.2 mm", "Dual Nano-SIM", "Wi-Fi 5", "Bluetooth 5.4", "Nao", "USB-C 2.0"),
                Spec(url, true, "Software", "os", "Sistema", "Xiaomi HyperOS", null, false, 0.95),
            ],
            [
                Variant("4 GB / 128 GB", 4, 128),
                Variant("4 GB / 256 GB", 4, 256),
                Variant("6 GB / 128 GB", 6, 128),
                Variant("8 GB / 256 GB", 8, 256),
            ],
            [
                Benchmark("AnTuTu", 260_000, url),
            ]);
    }

    private SourcePhoneRecord GalaxyS25Plus()
    {
        const string url = "https://www.samsung.com/uk/smartphones/galaxy-s25/specs/";
        return Record(
            url,
            true,
            "Samsung",
            "samsung.com",
            "Galaxy S25 Plus",
            "Flagship com tela grande, bateria mais forte e o mesmo foco em desempenho da linha S25.",
            new DateTimeOffset(2025, 1, 22, 0, 0, 0, TimeSpan.Zero),
            999,
            "https://images.samsung.com/uk/smartphones/galaxy-s25/specs/images/galaxy-s25-share-image.jpg",
            url,
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite for Galaxy", null, true, 0.98),
                Spec(url, true, "Memoria", "ram", "RAM", "12 GB", "GB", true, 0.96),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "256 GB", "GB", false, 0.96),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.7 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "Dynamic AMOLED 2X", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "3120 x 1440", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.96),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.96),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "12 MP", "MP", false, 0.95),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "10 MP", "MP", false, 0.95),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "12 MP", "MP", false, 0.95),
                Spec(url, true, "Bateria", "battery", "Bateria", "4900 mAh", "mAh", true, 0.96),
                Spec(url, true, "Bateria", "charging", "Carregamento", "45 W", "W", true, 0.95),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "15 W", "W", false, 0.9),
                Spec(url, true, "Construcao", "weight", "Peso", "190 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "158.4 x 75.8 x 7.3 mm", "Nano-SIM + eSIM", "Wi-Fi 7", "Bluetooth 5.4", "Sim", "USB-C 3.2"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Gorilla Glass Victus 2", null, false, 0.9),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.95),
                Spec(url, true, "Software", "os", "Sistema", "Android 15 / One UI 7", null, false, 0.9),
            ],
            [
                Variant("12 GB / 256 GB", 12, 256),
                Variant("12 GB / 512 GB", 12, 512),
            ],
            [
                Benchmark("AnTuTu", 1_970_000, url),
            ]);
    }

    private SourcePhoneRecord GalaxyS25()
    {
        const string url = "https://www.samsung.com/uk/smartphones/galaxy-s25/specs/";
        return Record(
            url,
            true,
            "Samsung",
            "samsung.com",
            "Galaxy S25",
            "Flagship compacto da Samsung, com foco em camera, desempenho e tamanho facil de usar no dia a dia.",
            new DateTimeOffset(2025, 1, 22, 0, 0, 0, TimeSpan.Zero),
            799,
            "https://images.samsung.com/uk/smartphones/galaxy-s25/specs/images/galaxy-s25-share-image.jpg",
            url,
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite for Galaxy", null, true, 0.98),
                Spec(url, true, "Memoria", "ram", "RAM", "12 GB", "GB", true, 0.96),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.95),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.2 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "Dynamic AMOLED 2X", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2340 x 1080", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.96),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.96),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "12 MP", "MP", false, 0.95),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "10 MP", "MP", false, 0.95),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "12 MP", "MP", false, 0.95),
                Spec(url, true, "Bateria", "battery", "Bateria", "4000 mAh", "mAh", true, 0.96),
                Spec(url, true, "Bateria", "charging", "Carregamento", "25 W", "W", true, 0.94),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "15 W", "W", false, 0.9),
                Spec(url, true, "Construcao", "weight", "Peso", "162 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "146.9 x 70.5 x 7.2 mm", "Nano-SIM + eSIM", "Wi-Fi 7", "Bluetooth 5.4", "Sim", "USB-C 3.2"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Gorilla Glass Victus 2", null, false, 0.9),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.95),
                Spec(url, true, "Software", "os", "Sistema", "Android 15 / One UI 7", null, false, 0.9),
            ],
            [
                Variant("12 GB / 128 GB", 12, 128),
                Variant("12 GB / 256 GB", 12, 256),
                Variant("12 GB / 512 GB", 12, 512),
            ],
            [
                Benchmark("AnTuTu", 1_930_000, url),
            ]);
    }

    private SourcePhoneRecord Iphone16ProMax()
    {
        const string url = "https://support.apple.com/en-us/121032";
        return Record(
            url,
            true,
            "Apple",
            "apple.com",
            "iPhone 16 Pro Max",
            "Modelo Pro Max com a maior tela da linha, camera teleobjetiva de 5x e foco em autonomia.",
            new DateTimeOffset(2024, 9, 20, 0, 0, 0, TimeSpan.Zero),
            1199,
            "https://fdn2.gsmarena.com/vv/bigpic/apple-iphone-16-pro-max.jpg",
            "https://www.gsmarena.com/apple_iphone_16_pro_max-13316.php",
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Apple A18 Pro", null, true, 0.98),
                Spec(url, true, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.8),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "256 GB", "GB", false, 0.97),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.9 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "Super Retina XDR OLED", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2868 x 1320", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "48 MP", "MP", true, 0.97),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "48 MP", "MP", false, 0.96),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "12 MP 5x", "MP", false, 0.96),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "12 MP", "MP", false, 0.96),
                Spec(url, true, "Bateria", "battery", "Bateria", "4685 mAh", "mAh", true, 0.68),
                Spec(url, true, "Bateria", "charging", "Carregamento", "Fast charge (50% in 30 min)", null, false, 0.62),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "25 W", "W", false, 0.9),
                Spec(url, true, "Construcao", "weight", "Peso", "227 g", "g", false, 0.96),
                .. PublicTechPack(url, true, "163 x 77.6 x 8.3 mm", "Nano-SIM + eSIM", "Wi-Fi 7", "Bluetooth 5.3", "Sim", "USB-C 3"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Ceramic Shield", null, false, 0.95),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.96),
                Spec(url, true, "Software", "os", "Sistema", "iOS 18", null, false, 0.84),
            ],
            [
                Variant("256 GB", null, 256),
                Variant("512 GB", null, 512),
                Variant("1 TB", null, 1024),
            ],
            [
                Benchmark("Geekbench multi-core", 8_800, url),
            ]);
    }

    private SourcePhoneRecord Iphone16()
    {
        const string url = "https://support.apple.com/en-us/121029";
        return Record(
            url,
            true,
            "Apple",
            "apple.com",
            "iPhone 16",
            "Modelo base com tela compacta, chip A18 e cameras fortes para foto e video no dia a dia.",
            new DateTimeOffset(2024, 9, 20, 0, 0, 0, TimeSpan.Zero),
            799,
            "https://fdn2.gsmarena.com/vv/bigpic/apple-iphone-16.jpg",
            "https://www.gsmarena.com/apple_iphone_16-13317.php",
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Apple A18", null, true, 0.98),
                Spec(url, true, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.78),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.97),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.1 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "Super Retina XDR OLED", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2556 x 1179", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "60 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "48 MP", "MP", true, 0.97),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "12 MP", "MP", false, 0.96),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "12 MP", "MP", false, 0.96),
                Spec(url, true, "Bateria", "battery", "Bateria", "3561 mAh", "mAh", true, 0.68),
                Spec(url, true, "Bateria", "charging", "Carregamento", "Fast charge (50% in 30 min)", null, false, 0.62),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "25 W", "W", false, 0.9),
                Spec(url, true, "Construcao", "weight", "Peso", "170 g", "g", false, 0.96),
                .. PublicTechPack(url, true, "147.6 x 71.6 x 7.8 mm", "Nano-SIM + eSIM", "Wi-Fi 7", "Bluetooth 5.3", "Sim", "USB-C 2"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Ceramic Shield", null, false, 0.95),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.96),
                Spec(url, true, "Software", "os", "Sistema", "iOS 18", null, false, 0.84),
            ],
            [
                Variant("128 GB", null, 128),
                Variant("256 GB", null, 256),
                Variant("512 GB", null, 512),
            ],
            [
                Benchmark("Geekbench multi-core", 7_900, url),
            ]);
    }

    private SourcePhoneRecord Xiaomi15T()
    {
        const string url = "https://www.mi.com/global/product/xiaomi-15t/specs/";
        return Record(
            url,
            true,
            "Xiaomi",
            "mi.com",
            "Xiaomi 15T",
            "Modelo forte da linha T com tela grande, conjunto triplo de cameras e foco em desempenho premium sem ir ao topo de preco.",
            new DateTimeOffset(2025, 9, 26, 0, 0, 0, TimeSpan.Zero),
            649,
            "https://i02.appmifile.com/mi-com-product/fly-birds/xiaomi-15t/pc/blackphonecolor.jpg",
            url,
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "MediaTek Dimensity 8400-Ultra", null, true, 0.97),
                Spec(url, true, "Memoria", "ram", "RAM", "12 GB", "GB", true, 0.96),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "256 GB", "GB", false, 0.96),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.83 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "AMOLED", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2772 x 1280", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.96),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.97),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "12 MP", "MP", false, 0.95),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "50 MP", "MP", false, 0.96),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "32 MP", "MP", false, 0.95),
                Spec(url, true, "Bateria", "battery", "Bateria", "5500 mAh", "mAh", true, 0.96),
                Spec(url, true, "Bateria", "charging", "Carregamento", "67 W", "W", true, 0.96),
                Spec(url, true, "Construcao", "weight", "Peso", "194 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "160.4 x 75.1 x 8.2 mm", "Dual Nano-SIM + eSIM", "Wi-Fi 6E", "Bluetooth 5.4", "Sim", "USB-C 2.0"),
                Spec(url, true, "Construcao", "protection", "Protecao frontal", "Gorilla Glass 7i", null, false, 0.93),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.96),
                Spec(url, true, "Software", "os", "Sistema", "Xiaomi HyperOS 2", null, false, 0.95),
            ],
            [
                Variant("12 GB / 256 GB", 12, 256),
                Variant("12 GB / 512 GB", 12, 512),
            ],
            [
                Benchmark("AnTuTu", 1_650_000, url),
            ]);
    }

    private SourcePhoneRecord GalaxyA36()
    {
        const string url = "https://www.samsung.com/uk/smartphones/galaxy-a/galaxy-a36-5g-awesome-lavender-256gb-sm-a366blvgeub/";
        return Record(
            url,
            true,
            "Samsung",
            "samsung.com",
            "Galaxy A36 5G",
            "Intermediario focado em equilibrio, com AMOLED 120 Hz, IP67 e carregamento rapido para uso publico em massa.",
            new DateTimeOffset(2025, 3, 26, 0, 0, 0, TimeSpan.Zero),
            399,
            "https://images.samsung.com/is/image/samsung/p6pim/uk/sm-a366blvgeub/gallery/uk-galaxy-a36-5g-sm-a366-sm-a366blvgeub-thumb-545177073",
            url,
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Snapdragon 6 Gen 3", null, true, 0.95),
                Spec(url, true, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.93),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.93),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.7 in", "in", true, 0.96),
                Spec(url, true, "Tela", "display_type", "Painel", "Super AMOLED", null, false, 0.95),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2340 x 1080", null, false, 0.95),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.95),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "8 MP", "MP", false, 0.94),
                Spec(url, true, "Camera", "telephoto_camera", "Camera auxiliar", "5 MP macro", "MP", false, 0.88),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "12 MP", "MP", false, 0.95),
                Spec(url, true, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.95),
                Spec(url, true, "Bateria", "charging", "Carregamento", "45 W", "W", true, 0.93),
                Spec(url, true, "Construcao", "weight", "Peso", "195 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "162.9 x 78.2 x 7.4 mm", "Nano-SIM + eSIM", "Wi-Fi 6", "Bluetooth 5.3", "Sim", "USB-C 2.0"),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP67", null, false, 0.95),
                Spec(url, true, "Software", "os", "Sistema", "Android 15 / One UI 7", null, false, 0.9),
            ],
            [
                Variant("6 GB / 128 GB", 6, 128),
                Variant("8 GB / 256 GB", 8, 256),
                Variant("12 GB / 256 GB", 12, 256),
            ],
            [
                Benchmark("AnTuTu", 630_000, url),
            ]);
    }

    private SourcePhoneRecord Xiaomi15()
    {
        const string url = "https://www.mi.com/global/product/xiaomi-15/specs/";
        return Record(
            url,
            true,
            "Xiaomi",
            "mi.com",
            "Xiaomi 15",
            "Flagship compacto com Snapdragon 8 Elite, lentes Leica e carregamento rapido sem abrir mao da bateria.",
            new DateTimeOffset(2025, 3, 2, 0, 0, 0, TimeSpan.Zero),
            799,
            "https://i02.appmifile.com/339_operatorx_operatorx_opx/02/03/2025/73371d9d0ca9843bcab6875541dc2905.png",
            "https://www.mi.com/global/product/xiaomi-15/",
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite Mobile Platform", null, true, 0.98),
                Spec(url, true, "Memoria", "ram", "RAM", "12 GB", "GB", true, 0.97),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "256 GB", "GB", false, 0.96),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.36 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "CrystalRes AMOLED LTPO", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2670 x 1200", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.96),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.97),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "50 MP", "MP", false, 0.96),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "50 MP", "MP", false, 0.96),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "32 MP", "MP", false, 0.95),
                Spec(url, true, "Bateria", "battery", "Bateria", "5240 mAh", "mAh", true, 0.97),
                Spec(url, true, "Bateria", "charging", "Carregamento", "90 W", "W", true, 0.96),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "50 W", "W", false, 0.95),
                Spec(url, true, "Construcao", "weight", "Peso", "191 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "152.3 x 71.2 x 8.1 mm", "Dual Nano-SIM + eSIM", "Wi-Fi 7", "Bluetooth 5.4", "Sim", "USB-C 3.2"),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.96),
                Spec(url, true, "Software", "os", "Sistema", "Xiaomi HyperOS 2", null, false, 0.95),
            ],
            [
                Variant("12 GB / 256 GB", 12, 256),
                Variant("12 GB / 512 GB", 12, 512),
            ],
            [
                Benchmark("AnTuTu", 2_550_000, url),
            ]);
    }

    private SourcePhoneRecord OnePlus13()
    {
        const string url = "https://www.oneplus.com/us/13/specs";
        return Record(
            url,
            true,
            "OnePlus",
            "oneplus.com",
            "13",
            "Topo de linha com foco total em desempenho, autonomia longa e conjunto de cameras Hasselblad.",
            new DateTimeOffset(2025, 1, 7, 0, 0, 0, TimeSpan.Zero),
            899.99m,
            "https://www.oneplus.com/content/dam/oneplus/2024/nav/13-Black.png",
            "https://www.oneplus.com/us/13",
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite Mobile Platform", null, true, 0.98),
                Spec(url, true, "Memoria", "ram", "RAM", "12 GB", "GB", true, 0.97),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "256 GB", "GB", false, 0.96),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.82 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "ProXDR LTPO 4.1 AMOLED", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "3168 x 1440", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.96),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.97),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "50 MP", "MP", false, 0.96),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "50 MP 3x", "MP", false, 0.96),
                Spec(url, true, "Bateria", "battery", "Bateria", "6000 mAh", "mAh", true, 0.97),
                Spec(url, true, "Bateria", "charging", "Carregamento", "80 W", "W", true, 0.96),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "50 W", "W", false, 0.95),
                Spec(url, true, "Construcao", "weight", "Peso", "210 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "162.9 x 76.5 x 8.5 mm", "Dual Nano-SIM + eSIM", "Wi-Fi 7", "Bluetooth 5.4", "Sim", "USB-C 3.2"),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP69 / IP68", null, false, 0.96),
                Spec(url, true, "Software", "os", "Sistema", "OxygenOS 15.0 based on Android 15", null, false, 0.95),
            ],
            [
                Variant("12 GB / 256 GB", 12, 256),
                Variant("16 GB / 512 GB", 16, 512),
            ],
            [
                Benchmark("AnTuTu", 2_600_000, url),
            ]);
    }

    private SourcePhoneRecord Pixel9a()
    {
        const string url = "https://support.google.com/pixelphone/answer/7158570?hl=en";
        return Record(
            url,
            true,
            "Google",
            "google.com",
            "Pixel 9a",
            "Intermediario premium com camera forte, bateria grande e experiencia Android limpa por um preco mais acessivel.",
            new DateTimeOffset(2025, 4, 10, 0, 0, 0, TimeSpan.Zero),
            499,
            "https://lh3.googleusercontent.com/0FQnNXFGulZ3DGGAPJJOfgwwAW3qEPyjpWg2SLBkmuIU3-c6FxDcmGBEspqPxKFZzTRHXuiCilf-VlDVAEEZzVb4TeP3JNuKnPQi=rj-sc0xffffffff",
            "https://blog.google/products-and-platforms/devices/pixel/google-pixel-9a/",
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Google Tensor G4", null, true, 0.98),
                Spec(url, true, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.97),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.96),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.3 in", "in", true, 0.97),
                Spec(url, true, "Tela", "display_type", "Painel", "pOLED", null, false, 0.96),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2424 x 1080", null, false, 0.96),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "48 MP", "MP", true, 0.97),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "13 MP", "MP", false, 0.96),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "13 MP", "MP", false, 0.95),
                Spec(url, true, "Bateria", "battery", "Bateria", "5100 mAh", "mAh", true, 0.97),
                Spec(url, true, "Bateria", "charging", "Carregamento", "27 W", "W", true, 0.94),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "7.5 W", "W", false, 0.88),
                Spec(url, true, "Construcao", "weight", "Peso", "185.9 g", "g", false, 0.95),
                .. PublicTechPack(url, true, "154.7 x 73.3 x 8.9 mm", "Nano-SIM + eSIM", "Wi-Fi 6E", "Bluetooth 5.3", "Sim", "USB-C 3.2"),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.96),
                Spec(url, true, "Software", "os", "Sistema", "Android 15", null, false, 0.95),
            ],
            [
                Variant("8 GB / 128 GB", 8, 128),
                Variant("8 GB / 256 GB", 8, 256),
            ],
            [
                Benchmark("AnTuTu", 1_321_069, url),
            ]);
    }

    private SourcePhoneRecord NothingPhone3aPro()
    {
        const string url = "https://intl.nothing.tech/products/phone-3a-pro";
        return Record(
            url,
            true,
            "Nothing",
            "nothing.tech",
            "Phone (3a) Pro",
            "Intermediario premium com design marcante, periscopio raro na faixa e foco grande em usabilidade.",
            new DateTimeOffset(2025, 3, 4, 0, 0, 0, TimeSpan.Zero),
            null,
            "https://cdn.shopify.com/s/files/1/0585/2479/5086/files/ArcPro1352x1352-Black-Glyphon.png?v=1740649983",
            url,
            [
                Spec(url, true, "Performance", "chipset", "Chipset", "Snapdragon 7s Gen 3", null, true, 0.97),
                Spec(url, true, "Memoria", "ram", "RAM", "12 GB", "GB", true, 0.95),
                Spec(url, true, "Armazenamento", "storage_base", "Armazenamento base", "256 GB", "GB", false, 0.95),
                Spec(url, true, "Tela", "display_size", "Tamanho da tela", "6.77 in", "in", true, 0.96),
                Spec(url, true, "Tela", "display_type", "Painel", "AMOLED", null, false, 0.95),
                Spec(url, true, "Tela", "resolution", "Resolucao", "2392 x 1080", null, false, 0.94),
                Spec(url, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.95),
                Spec(url, true, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.96),
                Spec(url, true, "Camera", "ultrawide_camera", "Ultra-wide", "8 MP", "MP", false, 0.93),
                Spec(url, true, "Camera", "telephoto_camera", "Teleobjetiva", "50 MP periscopio", "MP", false, 0.95),
                Spec(url, true, "Camera", "selfie_camera", "Camera frontal", "50 MP", "MP", false, 0.94),
                Spec(url, true, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.96),
                Spec(url, true, "Bateria", "charging", "Carregamento", "50 W", "W", true, 0.93),
                Spec(url, true, "Bateria", "wireless_charging", "Carregamento sem fio", "Nao suporta", null, false, 0.95),
                .. PublicTechPack(url, true, "163.5 x 77.5 x 8.4 mm", "Dual Nano-SIM + eSIM", "Wi-Fi 6", "Bluetooth 5.4", "Sim", "USB-C 2.0"),
                Spec(url, true, "Construcao", "ip_rating", "Resistencia", "IP64", null, false, 0.95),
                Spec(url, true, "Software", "os", "Sistema", "Nothing OS 3.1 com Android 15", null, false, 0.95),
            ],
            [
                Variant("12 GB / 256 GB", 12, 256),
            ],
            []);
    }
}

public sealed class GsmArenaFixtureAdapter : FixturePhoneSourceAdapter
{
    public override string SourceName => "GSMArena";

    protected override IReadOnlyList<SourcePhoneRecord> BuildRecords()
    {
        const string samsung = "https://www.gsmarena.com/samsung_galaxy_s25_ultra-13626.php";
        const string samsungPlus = "https://www.gsmarena.com/samsung_galaxy_s25+-13627.php";
        const string samsungBase = "https://www.gsmarena.com/samsung_galaxy_s25-13628.php";
        const string xiaomi15t = "https://www.gsmarena.com/xiaomi_15t_pro-14114.php";
        const string xiaomi15tBase = "https://www.gsmarena.com/xiaomi_15t-14115.php";
        const string redmi = "https://www.gsmarena.com/xiaomi_redmi_note_14_pro_5g-13512.php";

        return
        [
            LegacyIphone14(),
            LegacyGalaxyS22(),
            LegacyGalaxyA54(),
            LegacyRedmiNote12(),
            Record(
                samsung,
                false,
                "Samsung",
                null,
                "Galaxy S25 Ultra",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(samsung, false, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite", null, true, 0.82),
                    Spec(samsung, false, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.82),
                    Spec(samsung, false, "Camera", "main_camera", "Camera principal", "200 MP", "MP", true, 0.8),
                ],
                [],
                [Benchmark("AnTuTu", 2_000_000, samsung)]),
            Record(
                samsungPlus,
                false,
                "Samsung",
                null,
                "Galaxy S25 Plus",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(samsungPlus, false, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite for Galaxy", null, true, 0.8),
                    Spec(samsungPlus, false, "Bateria", "battery", "Bateria", "4900 mAh", "mAh", true, 0.8),
                    Spec(samsungPlus, false, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.78),
                ],
                [],
                [Benchmark("AnTuTu", 1_950_000, samsungPlus)]),
            Record(
                samsungBase,
                false,
                "Samsung",
                null,
                "Galaxy S25",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(samsungBase, false, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite for Galaxy", null, true, 0.8),
                    Spec(samsungBase, false, "Bateria", "battery", "Bateria", "4000 mAh", "mAh", true, 0.8),
                    Spec(samsungBase, false, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.78),
                ],
                [],
                [Benchmark("AnTuTu", 1_920_000, samsungBase)]),
            Record(
                xiaomi15t,
                false,
                "Xiaomi",
                null,
                "Xiaomi 15T Pro",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(xiaomi15t, false, "Performance", "chipset", "Chipset", "Dimensity 9400+", null, true, 0.82),
                    Spec(xiaomi15t, false, "Bateria", "battery", "Bateria", "5500 mAh", "mAh", true, 0.8),
                    Spec(xiaomi15t, false, "Bateria", "charging", "Carregamento", "90 W", "W", true, 0.8),
                ],
                [],
                [Benchmark("AnTuTu", 2_350_000, xiaomi15t)]),
            Record(
                xiaomi15tBase,
                false,
                "Xiaomi",
                null,
                "Xiaomi 15T",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(xiaomi15tBase, false, "Performance", "chipset", "Chipset", "Dimensity 8400-Ultra", null, true, 0.8),
                    Spec(xiaomi15tBase, false, "Bateria", "battery", "Bateria", "5500 mAh", "mAh", true, 0.8),
                    Spec(xiaomi15tBase, false, "Bateria", "charging", "Carregamento", "67 W", "W", true, 0.78),
                ],
                [],
                [Benchmark("AnTuTu", 1_620_000, xiaomi15tBase)]),
            Record(
                redmi,
                false,
                "Xiaomi",
                null,
                "Redmi Note 14 Pro 5G",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(redmi, false, "Performance", "chipset", "Chipset", "Dimensity 7300 Ultra", null, true, 0.82),
                    Spec(redmi, false, "Bateria", "battery", "Bateria", "5110 mAh", "mAh", true, 0.8),
                ],
                [],
                [Benchmark("AnTuTu", 710_000, redmi)]),
        ];
    }

    private SourcePhoneRecord LegacyIphone14()
    {
        const string url = "https://www.gsmarena.com/apple_iphone_14-11861.php";
        return Record(
            url,
            false,
            "Apple",
            null,
            "iPhone 14",
            "Flagship mais antigo da linha Apple, ainda forte em camera, video e desempenho para quem busca modelo consolidado.",
            new DateTimeOffset(2022, 9, 16, 0, 0, 0, TimeSpan.Zero),
            799,
            "https://fdn2.gsmarena.com/vv/bigpic/apple-iphone-14.jpg",
            url,
            [
                Spec(url, false, "Performance", "chipset", "Chipset", "Apple A15 Bionic", null, true, 0.82),
                Spec(url, false, "Memoria", "ram", "RAM", "6 GB", "GB", true, 0.8),
                Spec(url, false, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.82),
                Spec(url, false, "Tela", "display_size", "Tamanho da tela", "6.1 in", "in", true, 0.82),
                Spec(url, false, "Tela", "display_type", "Painel", "Super Retina XDR OLED", null, false, 0.81),
                Spec(url, false, "Tela", "resolution", "Resolucao", "2532 x 1170", null, false, 0.81),
                Spec(url, false, "Tela", "refresh_rate", "Taxa de atualizacao", "60 Hz", "Hz", false, 0.78),
                Spec(url, false, "Camera", "main_camera", "Camera principal", "12 MP", "MP", true, 0.82),
                Spec(url, false, "Camera", "ultrawide_camera", "Ultra-wide", "12 MP", "MP", false, 0.81),
                Spec(url, false, "Camera", "selfie_camera", "Camera frontal", "12 MP", "MP", false, 0.81),
                Spec(url, false, "Bateria", "battery", "Bateria", "3279 mAh", "mAh", true, 0.76),
                Spec(url, false, "Bateria", "charging", "Carregamento", "20 W", "W", false, 0.72),
                Spec(url, false, "Bateria", "wireless_charging", "Carregamento sem fio", "15 W", "W", false, 0.78),
                Spec(url, false, "Construcao", "weight", "Peso", "172 g", "g", false, 0.8),
                .. PublicTechPack(url, false, "146.7 x 71.5 x 7.8 mm", "Nano-SIM + eSIM", "Wi-Fi 6", "Bluetooth 5.3", "Sim", "Lightning 2.0"),
                Spec(url, false, "Construcao", "protection", "Protecao frontal", "Ceramic Shield", null, false, 0.8),
                Spec(url, false, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.82),
                Spec(url, false, "Software", "os", "Sistema", "iOS 16", null, false, 0.78),
            ],
            [
                Variant("128 GB", null, 128),
                Variant("256 GB", null, 256),
                Variant("512 GB", null, 512),
            ],
            []);
    }

    private SourcePhoneRecord LegacyGalaxyS22()
    {
        const string url = "https://www.gsmarena.com/samsung_galaxy_s22_5g-11253.php";
        return Record(
            url,
            false,
            "Samsung",
            null,
            "Galaxy S22",
            "Topo de linha compacto de geracao anterior, com tela 120 Hz e cameras fortes sem virar um aparelho enorme.",
            new DateTimeOffset(2022, 2, 25, 0, 0, 0, TimeSpan.Zero),
            799,
            "https://fdn2.gsmarena.com/vv/bigpic/samsung-galaxy-s22-5g.jpg",
            url,
            [
                Spec(url, false, "Performance", "chipset", "Chipset", "Snapdragon 8 Gen 1", null, true, 0.82),
                Spec(url, false, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.81),
                Spec(url, false, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.81),
                Spec(url, false, "Tela", "display_size", "Tamanho da tela", "6.1 in", "in", true, 0.82),
                Spec(url, false, "Tela", "display_type", "Painel", "Dynamic AMOLED 2X", null, false, 0.81),
                Spec(url, false, "Tela", "resolution", "Resolucao", "2340 x 1080", null, false, 0.81),
                Spec(url, false, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.8),
                Spec(url, false, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.82),
                Spec(url, false, "Camera", "ultrawide_camera", "Ultra-wide", "12 MP", "MP", false, 0.81),
                Spec(url, false, "Camera", "telephoto_camera", "Teleobjetiva", "10 MP 3x", "MP", false, 0.8),
                Spec(url, false, "Camera", "selfie_camera", "Camera frontal", "10 MP", "MP", false, 0.8),
                Spec(url, false, "Bateria", "battery", "Bateria", "3700 mAh", "mAh", true, 0.8),
                Spec(url, false, "Bateria", "charging", "Carregamento", "25 W", "W", true, 0.79),
                Spec(url, false, "Bateria", "wireless_charging", "Carregamento sem fio", "15 W", "W", false, 0.79),
                Spec(url, false, "Construcao", "weight", "Peso", "167 g", "g", false, 0.79),
                .. PublicTechPack(url, false, "146 x 70.6 x 7.6 mm", "Nano-SIM + eSIM", "Wi-Fi 6E", "Bluetooth 5.2", "Sim", "USB-C 3.2"),
                Spec(url, false, "Construcao", "protection", "Protecao frontal", "Gorilla Glass Victus+", null, false, 0.78),
                Spec(url, false, "Construcao", "ip_rating", "Resistencia", "IP68", null, false, 0.81),
                Spec(url, false, "Software", "os", "Sistema", "Android 12 / One UI 4.1", null, false, 0.76),
            ],
            [
                Variant("8 GB / 128 GB", 8, 128),
                Variant("8 GB / 256 GB", 8, 256),
            ],
            []);
    }

    private SourcePhoneRecord LegacyGalaxyA54()
    {
        const string url = "https://www.gsmarena.com/samsung_galaxy_a54-12070.php";
        return Record(
            url,
            false,
            "Samsung",
            null,
            "Galaxy A54 5G",
            "Intermediario popular da geracao passada, com AMOLED 120 Hz, bateria grande e ficha bem conhecida no mercado.",
            new DateTimeOffset(2023, 3, 24, 0, 0, 0, TimeSpan.Zero),
            449,
            "https://fdn2.gsmarena.com/vv/bigpic/samsung-galaxy-a54.jpg",
            url,
            [
                Spec(url, false, "Performance", "chipset", "Chipset", "Exynos 1380", null, true, 0.81),
                Spec(url, false, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.8),
                Spec(url, false, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.8),
                Spec(url, false, "Tela", "display_size", "Tamanho da tela", "6.4 in", "in", true, 0.81),
                Spec(url, false, "Tela", "display_type", "Painel", "Super AMOLED", null, false, 0.8),
                Spec(url, false, "Tela", "resolution", "Resolucao", "2340 x 1080", null, false, 0.8),
                Spec(url, false, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.79),
                Spec(url, false, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.81),
                Spec(url, false, "Camera", "ultrawide_camera", "Ultra-wide", "12 MP", "MP", false, 0.79),
                Spec(url, false, "Camera", "telephoto_camera", "Camera auxiliar", "5 MP macro", "MP", false, 0.72),
                Spec(url, false, "Camera", "selfie_camera", "Camera frontal", "32 MP", "MP", false, 0.79),
                Spec(url, false, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.81),
                Spec(url, false, "Bateria", "charging", "Carregamento", "25 W", "W", true, 0.78),
                Spec(url, false, "Construcao", "weight", "Peso", "202 g", "g", false, 0.79),
                .. PublicTechPack(url, false, "158.2 x 76.7 x 8.2 mm", "Dual Nano-SIM + eSIM", "Wi-Fi 6", "Bluetooth 5.3", "Sim", "USB-C 2.0"),
                Spec(url, false, "Construcao", "protection", "Protecao frontal", "Gorilla Glass 5", null, false, 0.76),
                Spec(url, false, "Construcao", "ip_rating", "Resistencia", "IP67", null, false, 0.8),
                Spec(url, false, "Software", "os", "Sistema", "Android 13 / One UI 5.1", null, false, 0.76),
            ],
            [
                Variant("8 GB / 128 GB", 8, 128),
                Variant("8 GB / 256 GB", 8, 256),
            ],
            []);
    }

    private SourcePhoneRecord LegacyRedmiNote12()
    {
        const string url = "https://www.gsmarena.com/xiaomi_redmi_note_12_4g-12188.php";
        return Record(
            url,
            false,
            "Xiaomi",
            null,
            "Redmi Note 12",
            "Intermediario acessivel de geracao anterior, com AMOLED 120 Hz, bateria grande e ficha util para quem pesquisa modelos populares mais antigos.",
            new DateTimeOffset(2023, 3, 30, 0, 0, 0, TimeSpan.Zero),
            199,
            "https://i02.appmifile.com/861_operatorx_operatorx_xm/16/03/2023/0ac834a7279d58346efb2fa8196442cc.jpg",
            url,
            [
                Spec(url, false, "Performance", "chipset", "Chipset", "Snapdragon 685", null, true, 0.81),
                Spec(url, false, "Memoria", "ram", "RAM", "6 GB", "GB", true, 0.79),
                Spec(url, false, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "GB", false, 0.79),
                Spec(url, false, "Tela", "display_size", "Tamanho da tela", "6.67 in", "in", true, 0.81),
                Spec(url, false, "Tela", "display_type", "Painel", "AMOLED", null, false, 0.8),
                Spec(url, false, "Tela", "resolution", "Resolucao", "2400 x 1080", null, false, 0.8),
                Spec(url, false, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "Hz", false, 0.79),
                Spec(url, false, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.8),
                Spec(url, false, "Camera", "ultrawide_camera", "Ultra-wide", "8 MP", "MP", false, 0.78),
                Spec(url, false, "Camera", "telephoto_camera", "Camera auxiliar", "2 MP macro", "MP", false, 0.72),
                Spec(url, false, "Camera", "selfie_camera", "Camera frontal", "13 MP", "MP", false, 0.77),
                Spec(url, false, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.81),
                Spec(url, false, "Bateria", "charging", "Carregamento", "33 W", "W", true, 0.79),
                Spec(url, false, "Construcao", "weight", "Peso", "183.5 g", "g", false, 0.78),
                .. PublicTechPack(url, false, "165.7 x 76 x 7.9 mm", "Dual Nano-SIM", "Wi-Fi 5", "Bluetooth 5.0", "Depende da regiao", "USB-C 2.0"),
                Spec(url, false, "Construcao", "protection", "Protecao frontal", "Gorilla Glass 3", null, false, 0.76),
                Spec(url, false, "Construcao", "ip_rating", "Resistencia", "IP53", null, false, 0.79),
                Spec(url, false, "Software", "os", "Sistema", "Android 13 / MIUI 14", null, false, 0.75),
            ],
            [
                Variant("4 GB / 64 GB", 4, 64),
                Variant("4 GB / 128 GB", 4, 128),
                Variant("6 GB / 128 GB", 6, 128),
                Variant("8 GB / 128 GB", 8, 128),
            ],
            [
                Benchmark("AnTuTu", 338_000, url),
            ]);
    }
}

public sealed class KimovilFixtureAdapter : FixturePhoneSourceAdapter
{
    public override string SourceName => "Kimovil";

    protected override IReadOnlyList<SourcePhoneRecord> BuildRecords()
    {
        const string iphone = "https://www.kimovil.com/en/where-to-buy-apple-iphone-16-pro";
        const string iphoneBase = "https://www.kimovil.com/en/where-to-buy-apple-iphone-16";
        const string iphoneMax = "https://www.kimovil.com/en/where-to-buy-apple-iphone-16-pro-max";
        const string a56 = "https://www.kimovil.com/en/where-to-buy-samsung-galaxy-a56-5g";
        const string a36 = "https://www.kimovil.com/en/where-to-buy-samsung-galaxy-a36-5g";
        const string redmi = "https://www.kimovil.com/en/where-to-buy-xiaomi-redmi-note-14-pro-5g";

        return
        [
            Record(
                iphone,
                false,
                "Apple",
                null,
                "iPhone 16 Pro",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(iphone, false, "Performance", "chipset", "Chipset", "Apple A18 Pro", null, true, 0.78),
                    Spec(iphone, false, "Bateria", "battery", "Bateria", "3582 mAh", "mAh", true, 0.68),
                    Spec(iphone, false, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.72),
                ],
                [],
                [Benchmark("Geekbench multi-core", 8_450, iphone)]),
            Record(
                iphoneBase,
                false,
                "Apple",
                null,
                "iPhone 16",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(iphoneBase, false, "Performance", "chipset", "Chipset", "Apple A18", null, true, 0.76),
                    Spec(iphoneBase, false, "Bateria", "battery", "Bateria", "3561 mAh", "mAh", true, 0.68),
                    Spec(iphoneBase, false, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.7),
                ],
                [],
                [Benchmark("Geekbench multi-core", 7_850, iphoneBase)]),
            Record(
                iphoneMax,
                false,
                "Apple",
                null,
                "iPhone 16 Pro Max",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(iphoneMax, false, "Performance", "chipset", "Chipset", "Apple A18 Pro", null, true, 0.78),
                    Spec(iphoneMax, false, "Bateria", "battery", "Bateria", "4685 mAh", "mAh", true, 0.7),
                    Spec(iphoneMax, false, "Memoria", "ram", "RAM", "8 GB", "GB", true, 0.72),
                ],
                [],
                [Benchmark("Geekbench multi-core", 8_760, iphoneMax)]),
            Record(
                a56,
                false,
                "Samsung",
                null,
                "Galaxy A56 5G",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(a56, false, "Performance", "chipset", "Chipset", "Exynos 1580", null, true, 0.74),
                    Spec(a56, false, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.76),
                ],
                [],
                [Benchmark("AnTuTu", 805_000, a56)]),
            Record(
                a36,
                false,
                "Samsung",
                null,
                "Galaxy A36 5G",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(a36, false, "Performance", "chipset", "Chipset", "Snapdragon 6 Gen 3", null, true, 0.74),
                    Spec(a36, false, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.76),
                ],
                [],
                [Benchmark("AnTuTu", 620_000, a36)]),
            Record(
                redmi,
                false,
                "Xiaomi",
                null,
                "Redmi Note 14 Pro 5G",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(redmi, false, "Bateria", "battery", "Bateria", "5100 mAh", "mAh", true, 0.72),
                    Spec(redmi, false, "Bateria", "charging", "Carregamento", "45 W", "W", true, 0.78),
                ],
                [],
                [Benchmark("AnTuTu", 700_000, redmi)]),
        ];
    }
}

public sealed class TudoCelularFixtureAdapter : FixturePhoneSourceAdapter
{
    public override string SourceName => "TudoCelular";

    protected override IReadOnlyList<SourcePhoneRecord> BuildRecords()
    {
        const string samsung = "https://www.tudocelular.com/Samsung/fichas-tecnicas/n9999/Samsung-Galaxy-S25-Ultra.html";
        const string samsungBase = "https://www.tudocelular.com/Samsung/fichas-tecnicas/n9998/Samsung-Galaxy-S25.html";
        const string a36 = "https://www.tudocelular.com/Samsung/fichas-tecnicas/n10410/Samsung-Galaxy-A36-5G.html";
        const string a16 = "https://www.tudocelular.com/Samsung/fichas-tecnicas/n10400/Samsung-Galaxy-A16-5G.html";

        return
        [
            Record(
                samsung,
                false,
                "Samsung",
                null,
                "Galaxy S25 Ultra",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(samsung, false, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite for Galaxy", null, true, 0.78),
                    Spec(samsung, false, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.79),
                    Spec(samsung, false, "Camera", "main_camera", "Camera principal", "200 MP", "MP", true, 0.8),
                ],
                [],
                [Benchmark("AnTuTu", 2_020_000, samsung)]),
            Record(
                samsungBase,
                false,
                "Samsung",
                null,
                "Galaxy S25",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(samsungBase, false, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite for Galaxy", null, true, 0.76),
                    Spec(samsungBase, false, "Bateria", "battery", "Bateria", "4000 mAh", "mAh", true, 0.78),
                    Spec(samsungBase, false, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.78),
                ],
                [],
                [Benchmark("AnTuTu", 1_910_000, samsungBase)]),
            Record(
                a36,
                false,
                "Samsung",
                null,
                "Galaxy A36 5G",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(a36, false, "Performance", "chipset", "Chipset", "Snapdragon 6 Gen 3", null, true, 0.72),
                    Spec(a36, false, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.78),
                    Spec(a36, false, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.78),
                ],
                [],
                [Benchmark("AnTuTu", 615_000, a36)]),
            Record(
                a16,
                false,
                "Samsung",
                null,
                "Galaxy A16 5G",
                null,
                null,
                null,
                null,
                null,
                [
                    Spec(a16, false, "Performance", "chipset", "Chipset", "Exynos 1330", null, true, 0.72),
                    Spec(a16, false, "Bateria", "battery", "Bateria", "5000 mAh", "mAh", true, 0.78),
                    Spec(a16, false, "Camera", "main_camera", "Camera principal", "50 MP", "MP", true, 0.8),
                ],
                [],
                [Benchmark("AnTuTu", 410_000, a16)]),
        ];
    }
}
