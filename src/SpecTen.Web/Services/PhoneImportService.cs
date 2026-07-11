using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SpecTen.Web.Data;
using SpecTen.Web.Options;
using SpecTen.Web.Scraping;

namespace SpecTen.Web.Services;

public sealed partial class PhoneImportService(
    IDbContextFactory<CatalogDbContext> dbContextFactory,
    IEnumerable<IPhoneSourceAdapter> adapters,
    SpecFactResolver resolver,
    PhoneClassifier classifier,
    IOptions<ScrapingOptions> options,
    IMemoryCache cache,
    ILogger<PhoneImportService> logger)
{
    public async Task<int> RefreshAllClassificationsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var phones = await db.PhoneModels
            .Include(model => model.Specs)
            .Include(model => model.Benchmarks)
            .Include(model => model.Classifications)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var updatedCount = 0;
        foreach (var phone in phones)
        {
            if (TryQueueClassificationSnapshot(db, phone))
            {
                updatedCount++;
            }
        }

        if (updatedCount == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (cache is MemoryCache concreteCache)
        {
            concreteCache.Compact(1.0);
        }

        return updatedCount;
    }

    public async Task<int> NormalizeCatalogAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var updatedCount = await NormalizeCatalogAsync(db, cancellationToken);
        if (updatedCount == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (cache is MemoryCache concreteCache)
        {
            concreteCache.Compact(1.0);
        }

        return updatedCount;
    }

    public async Task<ImportRun> RunImportAsync(string trigger, CancellationToken cancellationToken)
    {
        var records = new List<SourcePhoneRecord>();
        foreach (var adapter in adapters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            records.AddRange(await adapter.FetchAsync(cancellationToken));

            if (options.Value.PerDomainDelayMilliseconds > 0)
            {
                await Task.Delay(options.Value.PerDomainDelayMilliseconds, cancellationToken);
            }
        }

        var result = await ImportRecordsAsync(trigger, records, cancellationToken);
        return result.Run;
    }

    public async Task SeedIfEmptyAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.PhoneModels.AnyAsync(cancellationToken))
        {
            return;
        }

        await RunImportAsync("startup-seed", cancellationToken);
    }

    public async Task<ImportedPhoneRecordResult?> ImportRecordAsync(
        SourcePhoneRecord record,
        string trigger,
        CancellationToken cancellationToken)
    {
        var result = await ImportRecordsAsync(trigger, [record], cancellationToken);
        return result.ImportedPhones.FirstOrDefault();
    }

    private async Task<ImportExecutionResult> ImportRecordsAsync(
        string trigger,
        IReadOnlyList<SourcePhoneRecord> records,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73002026);", cancellationToken);
        }

        var run = new ImportRun
        {
            Trigger = trigger,
            StartedAt = DateTimeOffset.UtcNow,
            Status = ImportRunStatus.Running,
        };

        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var importedPhones = new List<ImportedPhoneRecordResult>();

        try
        {
            if (records.Count == 0)
            {
                run.Status = ImportRunStatus.Skipped;
                run.Message = "No active source adapters returned records.";
                run.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new ImportExecutionResult(run, importedPhones);
            }

            foreach (var modelGroup in records.GroupBy(record => new
                     {
                         BrandSlug = Slugger.Slugify(record.BrandName),
                         ModelSlug = CanonicalizePhoneIdentity(record.BrandName, record.ModelName).Slug,
                     }))
            {
                var groupedRecords = modelGroup.ToList();
                var primary = groupedRecords.FirstOrDefault(record => record.IsOfficial) ?? groupedRecords[0];
                var brand = await UpsertBrandAsync(db, primary, cancellationToken);
                var phone = await UpsertPhoneAsync(db, brand, primary, cancellationToken);

                if (phone.Id == 0)
                {
                    run.AddedModels++;
                }

                await db.SaveChangesAsync(cancellationToken);

                foreach (var record in groupedRecords)
                {
                    await StoreSourceDocumentAsync(db, phone, brand, record, cancellationToken);
                    UpsertBenchmarks(phone, record);
                }

                SyncVariants(phone, groupedRecords);

                var existingSpecs = await db.SpecFacts
                    .Where(spec => spec.PhoneModelId == phone.Id)
                    .ToDictionaryAsync(spec => spec.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);

                foreach (var specGroup in groupedRecords
                             .SelectMany(record => record.Specs)
                             .GroupBy(claim => claim.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var resolved = resolver.Resolve(specGroup.ToList());
                    if (!existingSpecs.TryGetValue(resolved.Key, out var spec))
                    {
                        spec = new SpecFact
                        {
                            PhoneModelId = phone.Id,
                            Key = resolved.Key,
                        };
                        db.SpecFacts.Add(spec);
                    }

                    ApplyResolvedSpec(spec, resolved);
                    run.UpdatedSpecs++;

                    if (resolved.Status == SpecStatus.NeedsReview)
                    {
                        var created = await EnsureReviewItemAsync(db, phone, resolved, specGroup.ToList(), cancellationToken);
                        if (created)
                        {
                            run.ReviewItemsCreated++;
                        }
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
                await RefreshClassificationAsync(db, phone.Id, cancellationToken);
                importedPhones.Add(new ImportedPhoneRecordResult(phone.Id, phone.Brand.Slug, phone.Slug));
            }

            run.UpdatedSpecs += await NormalizeCatalogAsync(db, cancellationToken);

            run.Status = ImportRunStatus.Completed;
            run.Message = $"Imported {records.Count} source records.";
            run.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            if (importedPhones.Count > 0)
            {
                if (cache is MemoryCache concreteCache)
                {
                    concreteCache.Compact(1.0);
                }
            }

            return new ImportExecutionResult(run, importedPhones);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Phone import failed.");
            run.Status = ImportRunStatus.Failed;
            run.Message = exception.Message;
            run.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            return new ImportExecutionResult(run, importedPhones);
        }
    }

    private static async Task<Brand> UpsertBrandAsync(
        CatalogDbContext db,
        SourcePhoneRecord record,
        CancellationToken cancellationToken)
    {
        var slug = Slugger.Slugify(record.BrandName);
        var brand = await db.Brands.FirstOrDefaultAsync(item => item.Slug == slug, cancellationToken);
        if (brand is null)
        {
            brand = new Brand
            {
                Name = record.BrandName,
                Slug = slug,
                OfficialDomain = record.OfficialDomain,
            };
            db.Brands.Add(brand);
        }
        else if (!string.IsNullOrWhiteSpace(record.OfficialDomain))
        {
            brand.OfficialDomain = record.OfficialDomain;
        }

        return brand;
    }

    private static async Task<PhoneModel> UpsertPhoneAsync(
        CatalogDbContext db,
        Brand brand,
        SourcePhoneRecord record,
        CancellationToken cancellationToken)
    {
        var identity = CanonicalizePhoneIdentity(record.BrandName, record.ModelName);
        var slug = identity.Slug;
        var phone = await db.PhoneModels
            .Include(model => model.Variants)
            .Include(model => model.Benchmarks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(model => model.BrandId == brand.Id && model.Slug == slug, cancellationToken);

        if (phone is null && !string.IsNullOrWhiteSpace(record.SourceUrl))
        {
            var existingPhoneId = await db.SourceDocuments
                .Where(document => document.SourceUrl == record.SourceUrl && document.PhoneModelId != null)
                .Select(document => document.PhoneModelId!.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingPhoneId != 0)
            {
                phone = await db.PhoneModels
                    .Include(model => model.Variants)
                    .Include(model => model.Benchmarks)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(model => model.Id == existingPhoneId, cancellationToken);
            }
        }

        if (phone is null)
        {
            phone = new PhoneModel
            {
                Brand = brand,
                BrandId = brand.Id,
                Name = identity.Name,
                Slug = slug,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.PhoneModels.Add(phone);
        }

        if (record.IsOfficial)
        {
            phone.Name = identity.Name;
            phone.Slug = slug;
        }

        phone.Summary = record.Summary ?? phone.Summary;
        phone.ReleasedAt = record.ReleasedAt ?? phone.ReleasedAt;
        phone.LaunchPriceUsd = record.LaunchPriceUsd ?? phone.LaunchPriceUsd;
        if (record.ImageUrl is not null)
        {
            var shouldReplaceImage = record.IsOfficial ||
                                     string.IsNullOrWhiteSpace(phone.ImageUrl) ||
                                     string.IsNullOrWhiteSpace(phone.ImageSourceUrl) ||
                                     CatalogReadiness.IsPlaceholderImage(phone.ImageUrl);

            if (shouldReplaceImage)
            {
                phone.ImageUrl = record.ImageUrl;
                phone.ImageSourceUrl = record.ImageSourceUrl;
            }
        }
        else if (record.IsOfficial && phone.ImageSourceUrl == record.SourceUrl)
        {
            phone.ImageUrl = null;
            phone.ImageSourceUrl = null;
        }

        phone.UpdatedAt = DateTimeOffset.UtcNow;
        return phone;
    }

    private static async Task StoreSourceDocumentAsync(
        CatalogDbContext db,
        PhoneModel phone,
        Brand brand,
        SourcePhoneRecord record,
        CancellationToken cancellationToken)
    {
        var hash = Hash($"{record.SourceName}|{record.SourceUrl}|{record.ModelName}|{string.Join('|', record.Specs.Select(spec => spec.RawValue))}");
        var existingDocument = await db.SourceDocuments
            .FirstOrDefaultAsync(document =>
                    document.SourceName == record.SourceName &&
                    document.SourceUrl == record.SourceUrl &&
                    document.ContentHash == hash,
                cancellationToken);

        if (existingDocument is not null)
        {
            return;
        }

        var document = new SourceDocument
        {
            SourceName = record.SourceName,
            SourceUrl = record.SourceUrl,
            BrandId = brand.Id,
            PhoneModelId = phone.Id,
            ContentHash = hash,
            PolicyStatus = record.PolicyStatus,
            RobotsAllowed = record.RobotsAllowed,
            RetrievedAt = DateTimeOffset.UtcNow,
            Notes = record.PolicyStatus == "FixtureOnly"
                ? "Fixture import. Live scraping must be explicitly reviewed before activation."
                : null,
        };

        foreach (var spec in record.Specs)
        {
            document.Claims.Add(new SourceClaim
            {
                PhoneModelId = phone.Id,
                FieldKey = spec.Key,
                RawValue = spec.RawValue,
                NormalizedValue = spec.NormalizedValue,
                Confidence = spec.Confidence,
            });
        }

        db.SourceDocuments.Add(document);
    }

    private static void SyncVariants(PhoneModel phone, IReadOnlyList<SourcePhoneRecord> records)
    {
        var desiredVariants = records
            .SelectMany(record => record.Variants)
            .Where(variant => !IsSuspiciousVariant(variant.Name, variant.RamGb, variant.StorageGb, variant.Color))
            .GroupBy(
                variant => $"{NormalizeVariantName(variant.Name)}|{variant.RamGb}|{variant.StorageGb}|{NormalizeVariantValue(variant.Color)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(variant => variant.StorageGb ?? int.MaxValue)
            .ThenBy(variant => variant.RamGb ?? int.MaxValue)
            .ThenBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var preferStructuredVariants = desiredVariants.Any(variant => variant.RamGb is not null || variant.StorageGb is not null);

        if (desiredVariants.Count == 0)
        {
            PruneVariants(phone);
            return;
        }

        var existingByKey = phone.Variants.ToDictionary(
            variant => $"{NormalizeVariantName(variant.Name)}|{variant.RamGb}|{variant.StorageGb}|{NormalizeVariantValue(variant.Color)}",
            StringComparer.OrdinalIgnoreCase);

        foreach (var variant in desiredVariants)
        {
            var key = $"{NormalizeVariantName(variant.Name)}|{variant.RamGb}|{variant.StorageGb}|{NormalizeVariantValue(variant.Color)}";
            if (existingByKey.TryGetValue(key, out var existing))
            {
                existing.Name = variant.Name;
                existing.RamGb = variant.RamGb;
                existing.StorageGb = variant.StorageGb;
                existing.Color = variant.Color;
                continue;
            }

            phone.Variants.Add(new PhoneVariant
            {
                Name = variant.Name,
                RamGb = variant.RamGb,
                StorageGb = variant.StorageGb,
                Color = variant.Color,
            });
        }

        PruneVariants(phone, preferStructuredVariants);
    }

    private static void UpsertBenchmarks(PhoneModel phone, SourcePhoneRecord record)
    {
        foreach (var benchmark in record.Benchmarks)
        {
            var existing = phone.Benchmarks.FirstOrDefault(score =>
                score.BenchmarkName.Equals(benchmark.BenchmarkName, StringComparison.OrdinalIgnoreCase) &&
                score.SourceName.Equals(benchmark.SourceName, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                phone.Benchmarks.Add(new BenchmarkScore
                {
                    BenchmarkName = benchmark.BenchmarkName,
                    SourceName = benchmark.SourceName,
                    SourceUrl = benchmark.SourceUrl,
                    Score = benchmark.Score,
                    RecordedAt = benchmark.RecordedAt,
                });
                continue;
            }

            existing.Score = benchmark.Score;
            existing.SourceUrl = benchmark.SourceUrl;
            existing.RecordedAt = benchmark.RecordedAt;
        }
    }

    private static void ApplyResolvedSpec(SpecFact spec, ResolvedSpec resolved)
    {
        if (spec.Status == SpecStatus.ManualOverride)
        {
            return;
        }

        spec.Group = resolved.Group;
        spec.DisplayName = resolved.DisplayName;
        spec.NormalizedValue = resolved.NormalizedValue;
        spec.DisplayValue = resolved.DisplayValue;
        spec.Unit = resolved.Unit;
        spec.SourceName = resolved.SourceName;
        spec.SourceUrl = resolved.SourceUrl;
        spec.Confidence = resolved.Confidence;
        spec.Status = resolved.Status;
        spec.IsCritical = resolved.IsCritical;
        spec.CollectedAt = resolved.CollectedAt;
    }

    private static async Task<bool> EnsureReviewItemAsync(
        CatalogDbContext db,
        PhoneModel phone,
        ResolvedSpec resolved,
        IReadOnlyList<SourceSpecClaim> claims,
        CancellationToken cancellationToken)
    {
        var exists = await db.ReviewItems.AnyAsync(item =>
                item.PhoneModelId == phone.Id &&
                item.FieldKey == resolved.Key &&
                item.Status == ReviewStatus.Open,
            cancellationToken);

        if (exists)
        {
            return false;
        }

        var values = string.Join("; ", claims.Select(claim => $"{claim.SourceName}: {claim.DisplayValue}").Distinct());
        db.ReviewItems.Add(new ReviewItem
        {
            PhoneModelId = phone.Id,
            FieldKey = resolved.Key,
            Title = $"Conflito em {resolved.DisplayName}",
            Description = $"Fontes discordam para {phone.Name}. Valores: {values}.",
            Status = ReviewStatus.Open,
        });

        return true;
    }

    private async Task RefreshClassificationAsync(CatalogDbContext db, int phoneId, CancellationToken cancellationToken)
    {
        var phone = await db.PhoneModels
            .Include(model => model.Specs)
            .Include(model => model.Benchmarks)
            .Include(model => model.Classifications)
            .AsSplitQuery()
            .FirstAsync(model => model.Id == phoneId, cancellationToken);

        if (!TryQueueClassificationSnapshot(db, phone))
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private bool TryQueueClassificationSnapshot(CatalogDbContext db, PhoneModel phone)
    {
        var chipset = phone.Specs.FirstOrDefault(spec => spec.Key == "chipset")?.DisplayValue;
        IReadOnlyDictionary<string, string?> profileValues = phone.Specs
            .Where(spec => !string.IsNullOrWhiteSpace(spec.Key) && !string.IsNullOrWhiteSpace(spec.DisplayValue))
            .GroupBy(spec => spec.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (string?)group.Select(spec => spec.DisplayValue).FirstOrDefault(),
                StringComparer.OrdinalIgnoreCase);
        var result = classifier.Classify(
            chipset,
            phone.Benchmarks.Select(score => new BenchmarkInput(score.BenchmarkName, score.Score)),
            phone.ReleasedAt,
            profileValues,
            phone.LaunchPriceUsd);

        var last = phone.Classifications
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .FirstOrDefault();

        if (last is not null &&
            last.Tier == result.Tier &&
            last.Score == result.Score &&
            last.Basis == result.Basis &&
            string.Equals(last.Explanation, result.Explanation, StringComparison.Ordinal))
        {
            return false;
        }

        var snapshot = new ClassificationSnapshot
        {
            PhoneModelId = phone.Id,
            Tier = result.Tier,
            Score = result.Score,
            Basis = result.Basis,
            Explanation = result.Explanation,
        };

        db.ClassificationSnapshots.Add(snapshot);
        phone.Classifications.Add(snapshot);
        return true;
    }

    private static async Task<int> NormalizeCatalogAsync(CatalogDbContext db, CancellationToken cancellationToken)
    {
        var phones = await db.PhoneModels
            .Include(phone => phone.Brand)
            .Include(phone => phone.Variants)
            .Include(phone => phone.Specs)
            .Include(phone => phone.Benchmarks)
            .Include(phone => phone.Classifications)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var identities = phones.ToDictionary(
            phone => phone.Id,
            phone => CanonicalizePhoneIdentity(phone.Brand.Name, phone.Name));
        var updatedCount = 0;

        foreach (var group in phones.GroupBy(phone => new { phone.BrandId, identities[phone.Id].Slug }))
        {
            var candidates = group
                .Select(phone => new
                {
                    Phone = phone,
                    Identity = identities[phone.Id],
                    HasVariantNoise = identities[phone.Id].HadVariantNoise,
                    Ready = IsPublicReady(phone),
                    Official = HasOfficialSource(phone),
                })
                .OrderBy(item => item.HasVariantNoise ? 1 : 0)
                .ThenByDescending(item => item.Ready)
                .ThenByDescending(item => item.Official)
                .ThenByDescending(item => item.Phone.Specs.Count)
                .ThenBy(item => item.Phone.Name.Length)
                .ThenBy(item => item.Phone.Id)
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var keeper = candidates[0].Phone;
            var canonical = candidates[0].Identity;
            if (!keeper.Name.Equals(canonical.Name, StringComparison.OrdinalIgnoreCase) ||
                !keeper.Slug.Equals(canonical.Slug, StringComparison.OrdinalIgnoreCase))
            {
                keeper.Name = canonical.Name;
                keeper.Slug = canonical.Slug;
                keeper.UpdatedAt = DateTimeOffset.UtcNow;
                updatedCount++;
            }

            PruneVariants(keeper, HasStructuredVariants(keeper.Variants));
            updatedCount += SanitizeSpecs(db, keeper);

            foreach (var duplicate in candidates.Skip(1).Select(item => item.Phone).ToList())
            {
                MergePhoneIntoKeeper(db, keeper, duplicate);
                db.PhoneModels.Remove(duplicate);
                updatedCount++;
            }
        }

        return updatedCount;
    }

    private static void MergePhoneIntoKeeper(CatalogDbContext db, PhoneModel keeper, PhoneModel duplicate)
    {
        if (string.IsNullOrWhiteSpace(keeper.Summary) && !string.IsNullOrWhiteSpace(duplicate.Summary))
        {
            keeper.Summary = duplicate.Summary;
        }

        keeper.ReleasedAt ??= duplicate.ReleasedAt;
        keeper.LaunchPriceUsd ??= duplicate.LaunchPriceUsd;

        if (CatalogReadiness.IsPlaceholderImage(keeper.ImageUrl) && !CatalogReadiness.IsPlaceholderImage(duplicate.ImageUrl))
        {
            keeper.ImageUrl = duplicate.ImageUrl;
            keeper.ImageSourceUrl = duplicate.ImageSourceUrl;
        }

        foreach (var variant in duplicate.Variants.ToList())
        {
            if (IsSuspiciousVariant(variant.Name, variant.RamGb, variant.StorageGb, variant.Color))
            {
                continue;
            }

            var exists = keeper.Variants.Any(existing =>
                NormalizeVariantName(existing.Name).Equals(NormalizeVariantName(variant.Name), StringComparison.OrdinalIgnoreCase) &&
                existing.RamGb == variant.RamGb &&
                existing.StorageGb == variant.StorageGb &&
                NormalizeVariantValue(existing.Color).Equals(NormalizeVariantValue(variant.Color), StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                keeper.Variants.Add(new PhoneVariant
                {
                    Name = variant.Name,
                    RamGb = variant.RamGb,
                    StorageGb = variant.StorageGb,
                    Color = variant.Color,
                });
            }
        }

        PruneVariants(keeper, HasStructuredVariants(keeper.Variants));

        foreach (var spec in duplicate.Specs.ToList())
        {
            var existing = keeper.Specs.FirstOrDefault(item => item.Key.Equals(spec.Key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                spec.PhoneModelId = keeper.Id;
                spec.PhoneModel = keeper;
                keeper.Specs.Add(spec);
                continue;
            }

            if (ShouldReplaceSpec(existing, spec))
            {
                existing.Group = spec.Group;
                existing.DisplayName = spec.DisplayName;
                existing.NormalizedValue = spec.NormalizedValue;
                existing.DisplayValue = spec.DisplayValue;
                existing.Unit = spec.Unit;
                existing.SourceName = spec.SourceName;
                existing.SourceUrl = spec.SourceUrl;
                existing.Confidence = spec.Confidence;
                existing.Status = spec.Status;
                existing.IsCritical = spec.IsCritical;
                existing.CollectedAt = spec.CollectedAt;
            }
        }

        foreach (var benchmark in duplicate.Benchmarks.ToList())
        {
            var existing = keeper.Benchmarks.FirstOrDefault(item =>
                item.BenchmarkName.Equals(benchmark.BenchmarkName, StringComparison.OrdinalIgnoreCase) &&
                item.SourceName.Equals(benchmark.SourceName, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                benchmark.PhoneModelId = keeper.Id;
                benchmark.PhoneModel = keeper;
                keeper.Benchmarks.Add(benchmark);
                continue;
            }

            if (benchmark.Score > existing.Score || benchmark.RecordedAt > existing.RecordedAt)
            {
                existing.Score = benchmark.Score;
                existing.SourceUrl = benchmark.SourceUrl;
                existing.RecordedAt = benchmark.RecordedAt;
            }
        }

        foreach (var snapshot in duplicate.Classifications.ToList())
        {
            var exists = keeper.Classifications.Any(existing =>
                existing.Tier == snapshot.Tier &&
                existing.Score == snapshot.Score &&
                existing.Basis.Equals(snapshot.Basis, StringComparison.OrdinalIgnoreCase) &&
                existing.Explanation.Equals(snapshot.Explanation, StringComparison.OrdinalIgnoreCase) &&
                existing.CreatedAt == snapshot.CreatedAt);

            if (!exists)
            {
                snapshot.PhoneModelId = keeper.Id;
                snapshot.PhoneModel = keeper;
                keeper.Classifications.Add(snapshot);
            }
        }

        foreach (var document in db.SourceDocuments.Where(item => item.PhoneModelId == duplicate.Id))
        {
            document.PhoneModelId = keeper.Id;
        }

        foreach (var claim in db.SourceClaims.Where(item => item.PhoneModelId == duplicate.Id))
        {
            claim.PhoneModelId = keeper.Id;
        }

        foreach (var reviewItem in db.ReviewItems.Where(item => item.PhoneModelId == duplicate.Id))
        {
            reviewItem.PhoneModelId = keeper.Id;
        }

        foreach (var report in db.CorrectionReports.Where(item => item.PhoneModelId == duplicate.Id))
        {
            report.PhoneModelId = keeper.Id;
        }

        keeper.UpdatedAt = keeper.UpdatedAt >= duplicate.UpdatedAt ? keeper.UpdatedAt : duplicate.UpdatedAt;
    }

    private static bool ShouldReplaceSpec(SpecFact current, SpecFact candidate)
    {
        if (string.IsNullOrWhiteSpace(current.DisplayValue))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(candidate.DisplayValue))
        {
            return false;
        }

        var currentOfficial = IsOfficialSource(current.SourceName);
        var candidateOfficial = IsOfficialSource(candidate.SourceName);
        if (candidateOfficial != currentOfficial)
        {
            return candidateOfficial;
        }

        if (Math.Abs(candidate.Confidence - current.Confidence) > 0.001d)
        {
            return candidate.Confidence > current.Confidence;
        }

        return candidate.CollectedAt > current.CollectedAt;
    }

    private static CanonicalPhoneIdentity CanonicalizePhoneIdentity(string brandName, string modelName)
    {
        var cleaned = NormalizePhoneName(modelName);
        if (cleaned.Length == 0)
        {
            return new CanonicalPhoneIdentity(modelName.Trim(), Slugger.Slugify(modelName), false);
        }

        var original = cleaned;
        cleaned = BundleSuffixRegex().Replace(cleaned, string.Empty).Trim();
        cleaned = ExclusiveSuffixRegex().Replace(cleaned, string.Empty).Trim();
        cleaned = EnterpriseEditionRegex().Replace(cleaned, string.Empty).Trim();
        cleaned = UnlockedSuffixRegex().Replace(cleaned, string.Empty).Trim();
        cleaned = StorageSuffixRegex().Replace(cleaned, string.Empty).Trim();

        if (Slugger.Slugify(brandName).Equals("samsung", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = ExtractSamsungCanonicalName(cleaned);
        }

        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim(' ', '-', '/');
        cleaned = BalanceParentheses(cleaned);
        if (cleaned.Length == 0)
        {
            cleaned = original;
        }

        return new CanonicalPhoneIdentity(
            cleaned,
            Slugger.Slugify(cleaned),
            !cleaned.Equals(original, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractSamsungCanonicalName(string value)
    {
        var tokens = value
            .Split([' ', '-', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (tokens.Count < 2 || !tokens[0].Equals("Galaxy", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var canonicalTokens = new List<string> { tokens[0] };
        for (var index = 1; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (IsSamsungVariantStopToken(token))
            {
                break;
            }

            if (canonicalTokens.Count >= 2 && !IsSamsungModelToken(token))
            {
                break;
            }

            canonicalTokens.Add(token);
        }

        return canonicalTokens.Count >= 2
            ? string.Join(' ', canonicalTokens)
            : value;
    }

    private static bool IsSamsungModelToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (PhoneSearchText.IsCompactModelToken(token) ||
            token.Length == 1 && char.IsLetter(token[0]) ||
            token.Length <= 3 && token.All(char.IsDigit))
        {
            return true;
        }

        return SamsungDescriptorTokens.Contains(PhoneSearchText.Normalize(token));
    }

    private static bool IsSamsungVariantStopToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        var normalized = PhoneSearchText.Normalize(token);
        return normalized.Length == 0 ||
               normalized is "bundle" or "unlocked" or "exclusive" or "samsungcom" or "enterprise" or "edition" or "with" or "and" ||
               token.EndsWith("GB", StringComparison.OrdinalIgnoreCase) ||
               LooksLikeModelCode(token);
    }

    private static bool LooksLikeModelCode(string token)
    {
        var compact = new string(token.Where(char.IsLetterOrDigit).ToArray());
        return compact.Length >= 7 &&
               compact.Any(char.IsLetter) &&
               compact.Any(char.IsDigit) &&
               compact.All(char.IsLetterOrDigit);
    }

    private static bool IsPublicReady(PhoneModel phone)
    {
        return CatalogReadiness.Evaluate(
            phone.ImageUrl,
            phone.ReleasedAt,
            phone.Specs.Select(spec => new CatalogSpecSnapshot(
                spec.Key,
                spec.DisplayValue,
                spec.SourceName,
                spec.Confidence,
                spec.Status,
                spec.IsCritical))).IsPublicReady;
    }

    private static bool HasOfficialSource(PhoneModel phone)
    {
        return phone.Specs.Any(spec => IsOfficialSource(spec.SourceName));
    }

    private static bool IsOfficialSource(string? sourceName)
    {
        return !string.IsNullOrWhiteSpace(sourceName) &&
               sourceName.Contains("official", StringComparison.OrdinalIgnoreCase);
    }

    private static void PruneVariants(PhoneModel phone, bool preferStructuredVariants = false)
    {
        var keptKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variant in phone.Variants.ToList())
        {
            var key = $"{NormalizeVariantName(variant.Name)}|{variant.RamGb}|{variant.StorageGb}|{NormalizeVariantValue(variant.Color)}";
            if (IsWeakVariant(variant.Name, variant.RamGb, variant.StorageGb, variant.Color, preferStructuredVariants) ||
                !keptKeys.Add(key))
            {
                phone.Variants.Remove(variant);
            }
        }
    }

    private static bool HasStructuredVariants(IEnumerable<PhoneVariant> variants)
    {
        return variants.Any(variant => variant.RamGb is not null || variant.StorageGb is not null);
    }

    private static bool IsWeakVariant(string? name, int? ramGb, int? storageGb, string? color, bool preferStructuredVariants)
    {
        if (IsSuspiciousVariant(name, ramGb, storageGb, color))
        {
            return true;
        }

        return preferStructuredVariants &&
               ramGb is null &&
               storageGb is null;
    }

    private static bool IsSuspiciousVariant(string? name, int? ramGb, int? storageGb, string? color)
    {
        if (storageGb is > 0 and < 16)
        {
            return true;
        }

        var normalizedName = NormalizeVariantName(name);
        if (normalizedName.Length == 0)
        {
            return true;
        }

        return normalizedName.Equals("variante padrao", StringComparison.OrdinalIgnoreCase) &&
               ramGb is null &&
               storageGb is null &&
               string.IsNullOrWhiteSpace(color);
    }

    private static int SanitizeSpecs(CatalogDbContext db, PhoneModel phone)
    {
        var updates = 0;

        var buildSpec = phone.Specs.FirstOrDefault(spec => spec.Key.Equals("build", StringComparison.OrdinalIgnoreCase));
        if (buildSpec is not null)
        {
            var cleanedBuild = NormalizeBuildValue(buildSpec.DisplayValue);
            if (!string.Equals(cleanedBuild, buildSpec.DisplayValue, StringComparison.Ordinal))
            {
                buildSpec.DisplayValue = cleanedBuild;
                buildSpec.NormalizedValue = PhoneSearchText.Normalize(cleanedBuild);
                updates++;
            }
        }

        var chargingSpec = phone.Specs.FirstOrDefault(spec => spec.Key.Equals("charging", StringComparison.OrdinalIgnoreCase));
        if (chargingSpec is not null && IsSuspiciousChargingSpec(phone, chargingSpec))
        {
            phone.Specs.Remove(chargingSpec);
            db.SpecFacts.Remove(chargingSpec);
            updates++;
        }

        return updates;
    }

    private static string NormalizeBuildValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var parts = new List<string>();
        if (value.Contains("estrutura de metal", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("metal frame", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("Estrutura de metal");
        }

        var match = GorillaGlassValueRegex().Match(value);
        if (match.Success)
        {
            parts.Add(match.Groups["value"].Value.Trim());
        }

        if (parts.Count == 0)
        {
            return value;
        }

        return string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsSuspiciousChargingSpec(PhoneModel phone, SpecFact spec)
    {
        if (!phone.Brand.Slug.Equals("samsung", StringComparison.OrdinalIgnoreCase) ||
            !IsOfficialSource(spec.SourceName) ||
            !TryParseWatts(spec.DisplayValue, out var watts))
        {
            return false;
        }

        return SamsungLowerMidRangeModelRegex().IsMatch(phone.Name) && watts > 45;
    }

    private static bool TryParseWatts(string? value, out int watts)
    {
        watts = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = ChargingValueRegex().Match(value);
        return match.Success &&
               int.TryParse(match.Groups["value"].Value, out watts);
    }

    private static string NormalizeVariantName(string? value)
    {
        return NormalizeVariantValue(value);
    }

    private static string NormalizeVariantValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : WhitespaceRegex().Replace(value.Trim(), " ");
    }

    private static string NormalizePhoneName(string value)
    {
        return WhitespaceRegex().Replace(value.Trim(), " ");
    }

    private static string BalanceParentheses(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var openCount = value.Count(character => character == '(');
        var closeCount = value.Count(character => character == ')');
        if (openCount <= closeCount)
        {
            return value;
        }

        return value + new string(')', openCount - closeCount);
    }

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static readonly HashSet<string> SamsungDescriptorTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "5g",
        "4g",
        "ultra",
        "plus",
        "edge",
        "lite",
        "note",
        "core",
        "prime",
        "active",
        "mini",
        "max",
        "pro",
        "neo",
        "zoom",
        "fe",
        "fold",
        "flip",
        "fan",
        "edition",
        "xcover",
    };

    [GeneratedRegex("""\s+(?:with|and)\s+.*\bbundle\b.*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BundleSuffixRegex();

    [GeneratedRegex("""\s*(?:\(|-|/)?\s*(?:exclusive to samsung\.com|samsung\.com exclusive|exclusiva samsung\.com)\)?\s*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExclusiveSuffixRegex();

    [GeneratedRegex("""\s*enterprise edition\s*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnterpriseEditionRegex();

    [GeneratedRegex("""\s*\(?unlocked\)?\s*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnlockedSuffixRegex();

    [GeneratedRegex("""\s+(?:\d{2,4}\s*gb)(?:\s*/\s*\d{2,4}\s*gb)?\s*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StorageSuffixRegex();

    [GeneratedRegex("""\s+""", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("""(?<value>Gorilla Glass(?:\s+(?:Victus(?:\s*2|\+)?|Armor(?:\s+[A-Za-z0-9+]+)?|DX(?:\+)?|Ceramic(?:\s+[A-Za-z0-9+]+)?|\d{1,2}[A-Za-z+]*)))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GorillaGlassValueRegex();

    [GeneratedRegex("""(?<value>\d{2,3})\s*W""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChargingValueRegex();

    [GeneratedRegex("""Galaxy\s+A(?:0|1|2|3|4)\d""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SamsungLowerMidRangeModelRegex();

    private sealed record ImportExecutionResult(ImportRun Run, IReadOnlyList<ImportedPhoneRecordResult> ImportedPhones);

    private sealed record CanonicalPhoneIdentity(string Name, string Slug, bool HadVariantNoise);
}

public sealed record ImportedPhoneRecordResult(int PhoneId, string BrandSlug, string Slug);
