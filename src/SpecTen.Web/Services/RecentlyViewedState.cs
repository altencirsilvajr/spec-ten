using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace SpecTen.Web.Services;

public sealed class RecentlyViewedState
{
    private const int MaxItems = 8;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IJSRuntime? _jsRuntime;
    private readonly ILogger<RecentlyViewedState>? _logger;
    private readonly List<RecentlyViewedItem> _items = [];
    private Task<bool>? _pendingHydration;

    private bool IsHydrated { get; set; }

    public RecentlyViewedState(IJSRuntime? jsRuntime = null, ILogger<RecentlyViewedState>? logger = null)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public event Action? Changed;

    public IReadOnlyList<RecentlyViewedItem> Items => _items;

    public int Count => _items.Count;

    public ValueTask<bool> EnsureHydratedAsync()
    {
        if (IsHydrated)
        {
            return ValueTask.FromResult(false);
        }

        if (_pendingHydration is not null)
        {
            return new ValueTask<bool>(_pendingHydration);
        }

        _pendingHydration = HydrateCoreAsync();
        return new ValueTask<bool>(_pendingHydration);
    }

    public bool AddOrUpdate(RecentlyViewedItem item)
    {
        if (item.Id <= 0)
        {
            return false;
        }

        var existingIndex = _items.FindIndex(existing => existing.Id == item.Id);
        if (existingIndex >= 0)
        {
            _items.RemoveAt(existingIndex);
        }

        _items.Insert(0, item);
        if (_items.Count > MaxItems)
        {
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        }

        NotifyChanged();
        return true;
    }

    public bool Replace(IEnumerable<RecentlyViewedItem> items)
    {
        var normalized = items
            .Where(item => item.Id > 0)
            .DistinctBy(item => item.Id)
            .OrderByDescending(item => item.ViewedAt)
            .Take(MaxItems)
            .ToList();

        if (_items.SequenceEqual(normalized))
        {
            return false;
        }

        _items.Clear();
        _items.AddRange(normalized);
        NotifyChanged();
        return true;
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
        _ = PersistAsync();
    }

    private async Task<bool> HydrateCoreAsync()
    {
        try
        {
            if (_jsRuntime is null)
            {
                IsHydrated = true;
                return false;
            }

            var serialized = await _jsRuntime.InvokeAsync<string?>("spectenRecentlyViewed.load");
            IsHydrated = true;
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return false;
            }

            var restored = JsonSerializer.Deserialize<List<RecentlyViewedItem>>(serialized, JsonOptions) ?? [];
            return Replace(restored);
        }
        catch (InvalidOperationException exception)
        {
            _logger?.LogDebug(exception, "Recently viewed hydration is waiting for interactive rendering.");
            return false;
        }
        catch (JSDisconnectedException exception)
        {
            _logger?.LogDebug(exception, "Recently viewed hydration was interrupted because the circuit disconnected.");
            IsHydrated = true;
            return false;
        }
        catch (JSException exception)
        {
            _logger?.LogDebug(exception, "Recently viewed hydration from local storage failed.");
            IsHydrated = true;
            return false;
        }
        catch (JsonException exception)
        {
            _logger?.LogDebug(exception, "Recently viewed hydration ignored an invalid local storage payload.");
            IsHydrated = true;
            return false;
        }
        finally
        {
            _pendingHydration = null;
        }
    }

    private async Task PersistAsync()
    {
        if (!IsHydrated || _jsRuntime is null)
        {
            return;
        }

        try
        {
            var serialized = _items.Count == 0
                ? string.Empty
                : JsonSerializer.Serialize(_items, JsonOptions);
            await _jsRuntime.InvokeVoidAsync("spectenRecentlyViewed.save", serialized);
        }
        catch (InvalidOperationException exception)
        {
            _logger?.LogDebug(exception, "Recently viewed persistence is waiting for interactive rendering.");
        }
        catch (JSDisconnectedException exception)
        {
            _logger?.LogDebug(exception, "Recently viewed persistence was interrupted because the circuit disconnected.");
        }
        catch (JSException exception)
        {
            _logger?.LogDebug(exception, "Recently viewed persistence to local storage failed.");
        }
    }
}

public sealed record RecentlyViewedItem(
    int Id,
    string Brand,
    string BrandSlug,
    string Name,
    string Slug,
    string BadgeLabel,
    bool HasFullCatalogEntry,
    DateTimeOffset ViewedAt)
{
    public string DisplayBrand => PhoneNameFormatter.DisplayBrand(Brand, Name);
    public string FullName => PhoneNameFormatter.FullName(Brand, Name);
    public string ModelName => PhoneNameFormatter.ModelName(Brand, Name);
}
