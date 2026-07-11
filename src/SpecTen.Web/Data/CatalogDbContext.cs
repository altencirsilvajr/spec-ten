using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SpecTen.Web.Data;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<PhoneModel> PhoneModels => Set<PhoneModel>();
    public DbSet<PhoneVariant> PhoneVariants => Set<PhoneVariant>();
    public DbSet<SpecFact> SpecFacts => Set<SpecFact>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<SourceClaim> SourceClaims => Set<SourceClaim>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();
    public DbSet<ReviewItem> ReviewItems => Set<ReviewItem>();
    public DbSet<BenchmarkScore> BenchmarkScores => Set<BenchmarkScore>();
    public DbSet<ClassificationSnapshot> ClassificationSnapshots => Set<ClassificationSnapshot>();
    public DbSet<CorrectionReport> CorrectionReports => Set<CorrectionReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>(entity =>
        {
            entity.ToTable("brands");
            entity.HasIndex(brand => brand.Slug).IsUnique();
            entity.Property(brand => brand.Name).HasMaxLength(120);
            entity.Property(brand => brand.Slug).HasMaxLength(140);
            entity.Property(brand => brand.OfficialDomain).HasMaxLength(240);
        });

        modelBuilder.Entity<PhoneModel>(entity =>
        {
            entity.ToTable("phone_models");
            entity.HasIndex(phone => new { phone.BrandId, phone.Slug }).IsUnique();
            entity.Property(phone => phone.Name).HasMaxLength(180);
            entity.Property(phone => phone.Slug).HasMaxLength(220);
            entity.Property(phone => phone.Summary).HasMaxLength(500);
            entity.Property(phone => phone.LaunchPriceUsd).HasPrecision(10, 2);
            entity.HasOne(phone => phone.Brand)
                .WithMany(brand => brand.Models)
                .HasForeignKey(phone => phone.BrandId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PhoneVariant>(entity =>
        {
            entity.ToTable("phone_variants");
            entity.HasIndex(variant => new { variant.PhoneModelId, variant.Name }).IsUnique();
            entity.Property(variant => variant.Name).HasMaxLength(160);
            entity.Property(variant => variant.Color).HasMaxLength(80);
        });

        modelBuilder.Entity<SpecFact>(entity =>
        {
            entity.ToTable("spec_facts");
            entity.HasIndex(spec => new { spec.PhoneModelId, spec.Key }).IsUnique();
            entity.HasIndex(spec => spec.Status);
            entity.Property(spec => spec.Group).HasMaxLength(80);
            entity.Property(spec => spec.Key).HasMaxLength(120);
            entity.Property(spec => spec.DisplayName).HasMaxLength(160);
            entity.Property(spec => spec.NormalizedValue).HasMaxLength(500);
            entity.Property(spec => spec.DisplayValue).HasMaxLength(500);
            entity.Property(spec => spec.Unit).HasMaxLength(60);
            entity.Property(spec => spec.SourceName).HasMaxLength(120);
            entity.Property(spec => spec.Status).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<SourceDocument>(entity =>
        {
            entity.ToTable("source_documents");
            entity.HasIndex(document => new { document.SourceName, document.SourceUrl, document.ContentHash });
            entity.Property(document => document.SourceName).HasMaxLength(120);
            entity.Property(document => document.SourceUrl).HasMaxLength(600);
            entity.Property(document => document.ContentHash).HasMaxLength(128);
            entity.Property(document => document.PolicyStatus).HasMaxLength(80);
            entity.Property(document => document.Notes).HasMaxLength(500);
        });

        modelBuilder.Entity<SourceClaim>(entity =>
        {
            entity.ToTable("source_claims");
            entity.HasIndex(claim => new { claim.PhoneModelId, claim.FieldKey });
            entity.Property(claim => claim.FieldKey).HasMaxLength(120);
            entity.Property(claim => claim.RawValue).HasMaxLength(500);
            entity.Property(claim => claim.NormalizedValue).HasMaxLength(500);
        });

        modelBuilder.Entity<ImportRun>(entity =>
        {
            entity.ToTable("import_runs");
            entity.HasIndex(run => run.StartedAt);
            entity.Property(run => run.Trigger).HasMaxLength(80);
            entity.Property(run => run.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(run => run.Message).HasMaxLength(1000);
        });

        modelBuilder.Entity<ReviewItem>(entity =>
        {
            entity.ToTable("review_items");
            entity.HasIndex(item => item.Status);
            entity.Property(item => item.FieldKey).HasMaxLength(120);
            entity.Property(item => item.Title).HasMaxLength(220);
            entity.Property(item => item.Description).HasMaxLength(1000);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.Resolution).HasMaxLength(1000);
        });

        modelBuilder.Entity<BenchmarkScore>(entity =>
        {
            entity.ToTable("benchmark_scores");
            entity.HasIndex(score => new { score.PhoneModelId, score.BenchmarkName, score.SourceName }).IsUnique();
            entity.Property(score => score.BenchmarkName).HasMaxLength(80);
            entity.Property(score => score.SourceName).HasMaxLength(120);
        });

        modelBuilder.Entity<ClassificationSnapshot>(entity =>
        {
            entity.ToTable("classification_snapshots");
            entity.HasIndex(snapshot => new { snapshot.PhoneModelId, snapshot.CreatedAt });
            entity.Property(snapshot => snapshot.Tier).HasConversion<string>().HasMaxLength(40);
            entity.Property(snapshot => snapshot.Basis).HasMaxLength(120);
            entity.Property(snapshot => snapshot.Explanation).HasMaxLength(500);
        });

        modelBuilder.Entity<CorrectionReport>(entity =>
        {
            entity.ToTable("correction_reports");
            entity.HasIndex(report => report.Status);
            entity.Property(report => report.FieldKey).HasMaxLength(120);
            entity.Property(report => report.ReporterEmail).HasMaxLength(240);
            entity.Property(report => report.Message).HasMaxLength(1000);
            entity.Property(report => report.Status).HasConversion<string>().HasMaxLength(40);
        });
    }
}
