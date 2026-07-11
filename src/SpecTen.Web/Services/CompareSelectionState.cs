using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace SpecTen.Web.Services;

public sealed class CompareSelectionState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IJSRuntime? _jsRuntime;
    private readonly ILogger<CompareSelectionState>? _logger;
    private readonly List<CompareSelectionItem> _items = [];
    private Task<bool>? _pendingHydration;

    private bool IsHydrated { get; set; }

    public CompareSelectionState(IJSRuntime? jsRuntime = null, ILogger<CompareSelectionState>? logger = null)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public event Action? Changed;

    public IReadOnlyList<CompareSelectionItem> Items => _items;

    public int Count => _items.Count;

    public IReadOnlyList<int> Ids => _items.Select(item => item.Id).ToList();

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

    public bool Contains(int id)
    {
        return _items.Any(item => item.Id == id);
    }

    public bool AddOrUpdate(CompareSelectionItem item, bool notify = true)
    {
        var existingIndex = _items.FindIndex(existing => existing.Id == item.Id);
        if (existingIndex >= 0)
        {
            if (_items[existingIndex] == item)
            {
                return false;
            }

            _items[existingIndex] = item;
            if (notify)
            {
                NotifyChanged();
            }

            return true;
        }

        if (_items.Count >= 4)
        {
            return false;
        }

        _items.Add(item);
        if (notify)
        {
            NotifyChanged();
        }

        return true;
    }

    public bool Remove(int id)
    {
        var removed = _items.RemoveAll(item => item.Id == id) > 0;
        if (removed)
        {
            NotifyChanged();
        }

        return removed;
    }

    public bool Replace(IEnumerable<CompareSelectionItem> items)
    {
        var normalized = items
            .Where(item => item.Id > 0)
            .DistinctBy(item => item.Id)
            .Take(4)
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

    public string BuildCompareUrl()
    {
        return _items.Count == 0
            ? "/comparar"
            : $"/comparar?ids={string.Join(",", _items.Select(item => item.Id))}";
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

            if (_items.Count > 0)
            {
                IsHydrated = true;
                await PersistAsync();
                return false;
            }

            var serialized = await _jsRuntime.InvokeAsync<string?>("spectenCompareSelection.load");
            IsHydrated = true;
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return false;
            }

            var restored = JsonSerializer.Deserialize<List<CompareSelectionItem>>(serialized, JsonOptions) ?? [];
            return Replace(restored);
        }
        catch (InvalidOperationException exception)
        {
            _logger?.LogDebug(exception, "Compare selection hydration is waiting for interactive rendering.");
            return false;
        }
        catch (JSDisconnectedException exception)
        {
            _logger?.LogDebug(exception, "Compare selection hydration was interrupted because the circuit disconnected.");
            IsHydrated = true;
            return false;
        }
        catch (JSException exception)
        {
            _logger?.LogDebug(exception, "Compare selection hydration from local storage failed.");
            IsHydrated = true;
            return false;
        }
        catch (JsonException exception)
        {
            _logger?.LogDebug(exception, "Compare selection hydration ignored an invalid local storage payload.");
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
            await _jsRuntime.InvokeVoidAsync("spectenCompareSelection.save", serialized);
        }
        catch (InvalidOperationException exception)
        {
            _logger?.LogDebug(exception, "Compare selection persistence is waiting for interactive rendering.");
        }
        catch (JSDisconnectedException exception)
        {
            _logger?.LogDebug(exception, "Compare selection persistence was interrupted because the circuit disconnected.");
        }
        catch (JSException exception)
        {
            _logger?.LogDebug(exception, "Compare selection persistence to local storage failed.");
        }
    }
}

public sealed record CompareSelectionItem(
    int Id,
    string Brand,
    string BrandSlug,
    string Name,
    string Slug,
    string Tier,
    bool HasFullCatalogEntry)
{
    public string DisplayBrand => PhoneNameFormatter.DisplayBrand(Brand, Name);
    public string FullName => PhoneNameFormatter.FullName(Brand, Name);
    public string ModelName => PhoneNameFormatter.ModelName(Brand, Name);
}
