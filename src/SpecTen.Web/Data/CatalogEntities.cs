namespace SpecTen.Web.Data;

public sealed class Brand
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? OfficialDomain { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PhoneModel> Models { get; set; } = [];
}

public sealed class PhoneModel
{
    public int Id { get; set; }
    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Summary { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public decimal? LaunchPriceUsd { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageSourceUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PhoneVariant> Variants { get; set; } = [];
    public List<SpecFact> Specs { get; set; } = [];
    public List<BenchmarkScore> Benchmarks { get; set; } = [];
    public List<ClassificationSnapshot> Classifications { get; set; } = [];
}

public sealed class PhoneVariant
{
    public int Id { get; set; }
    public int PhoneModelId { get; set; }
    public PhoneModel PhoneModel { get; set; } = null!;
    public string Name { get; set; } = "";
    public int? RamGb { get; set; }
    public int? StorageGb { get; set; }
    public string? Color { get; set; }
}

public sealed class SpecFact
{
    public int Id { get; set; }
    public int PhoneModelId { get; set; }
    public PhoneModel PhoneModel { get; set; } = null!;
    public string Group { get; set; } = "";
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string NormalizedValue { get; set; } = "";
    public string DisplayValue { get; set; } = "";
    public string? Unit { get; set; }
    public string SourceName { get; set; } = "";
    public string? SourceUrl { get; set; }
    public double Confidence { get; set; }
    public SpecStatus Status { get; set; } = SpecStatus.Published;
    public bool IsCritical { get; set; }
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SourceDocument
{
    public int Id { get; set; }
    public string SourceName { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public int? PhoneModelId { get; set; }
    public PhoneModel? PhoneModel { get; set; }
    public string ContentHash { get; set; } = "";
    public string PolicyStatus { get; set; } = "";
    public bool RobotsAllowed { get; set; }
    public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }

    public List<SourceClaim> Claims { get; set; } = [];
}

public sealed class SourceClaim
{
    public int Id { get; set; }
    public int SourceDocumentId { get; set; }
    public SourceDocument SourceDocument { get; set; } = null!;
    public int? PhoneModelId { get; set; }
    public PhoneModel? PhoneModel { get; set; }
    public string FieldKey { get; set; } = "";
    public string RawValue { get; set; } = "";
    public string NormalizedValue { get; set; } = "";
    public double Confidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ImportRun
{
    public int Id { get; set; }
    public string Trigger { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public ImportRunStatus Status { get; set; } = ImportRunStatus.Running;
    public string? Message { get; set; }
    public int AddedModels { get; set; }
    public int UpdatedSpecs { get; set; }
    public int ReviewItemsCreated { get; set; }
}

public sealed class ReviewItem
{
    public int Id { get; set; }
    public int? PhoneModelId { get; set; }
    public PhoneModel? PhoneModel { get; set; }
    public string FieldKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public ReviewStatus Status { get; set; } = ReviewStatus.Open;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? Resolution { get; set; }
}

public sealed class BenchmarkScore
{
    public int Id { get; set; }
    public int PhoneModelId { get; set; }
    public PhoneModel PhoneModel { get; set; } = null!;
    public string BenchmarkName { get; set; } = "";
    public int Score { get; set; }
    public string SourceName { get; set; } = "";
    public string? SourceUrl { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClassificationSnapshot
{
    public int Id { get; set; }
    public int PhoneModelId { get; set; }
    public PhoneModel PhoneModel { get; set; } = null!;
    public ClassificationTier Tier { get; set; } = ClassificationTier.Undefined;
    public int Score { get; set; }
    public string Basis { get; set; } = "";
    public string Explanation { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CorrectionReport
{
    public int Id { get; set; }
    public int PhoneModelId { get; set; }
    public PhoneModel PhoneModel { get; set; } = null!;
    public string? FieldKey { get; set; }
    public string? ReporterEmail { get; set; }
    public string Message { get; set; } = "";
    public ReviewStatus Status { get; set; } = ReviewStatus.Open;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum SpecStatus
{
    Published,
    NeedsReview,
    Rejected,
    ManualOverride,
}

public enum ReviewStatus
{
    Open,
    Resolved,
    Dismissed,
}

public enum ImportRunStatus
{
    Running,
    Completed,
    Failed,
    Skipped,
}

public enum ClassificationTier
{
    Undefined,
    Entry,
    MidRange,
    Flagship,
}
