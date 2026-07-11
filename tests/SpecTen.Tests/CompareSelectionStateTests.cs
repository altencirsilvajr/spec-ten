using Microsoft.JSInterop;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class CompareSelectionStateTests
{
    [Fact]
    public void AddOrUpdate_BuildsStableCompareUrl_AndKeepsInsertionOrder()
    {
        var state = new CompareSelectionState();

        state.AddOrUpdate(new CompareSelectionItem(4, "Xiaomi", "xiaomi", "Xiaomi 15T Pro", "xiaomi-15t-pro", "Top de linha", true));
        state.AddOrUpdate(new CompareSelectionItem(10, "Samsung", "samsung", "Galaxy S25", "galaxy-s25", "Top de linha", true));

        Assert.Equal(2, state.Count);
        Assert.Equal("/comparar?ids=4,10", state.BuildCompareUrl());
        Assert.Equal([4, 10], state.Ids);
    }

    [Fact]
    public void Replace_DeduplicatesAndCapsSelectionAtFour()
    {
        var state = new CompareSelectionState();

        state.Replace([
            new CompareSelectionItem(4, "Xiaomi", "xiaomi", "Xiaomi 15T Pro", "xiaomi-15t-pro", "Top de linha", true),
            new CompareSelectionItem(4, "Xiaomi", "xiaomi", "Xiaomi 15T Pro", "xiaomi-15t-pro", "Top de linha", true),
            new CompareSelectionItem(10, "Samsung", "samsung", "Galaxy S25", "galaxy-s25", "Top de linha", true),
            new CompareSelectionItem(9, "Samsung", "samsung", "Galaxy S25 Plus", "galaxy-s25-plus", "Top de linha", true),
            new CompareSelectionItem(2, "Apple", "apple", "iPhone 16 Pro", "iphone-16-pro", "Top de linha", true),
            new CompareSelectionItem(1, "Samsung", "samsung", "Galaxy S25 Ultra", "galaxy-s25-ultra", "Top de linha", true)
        ]);

        Assert.Equal(4, state.Count);
        Assert.Equal([4, 10, 9, 2], state.Ids);
        Assert.DoesNotContain(state.Items, item => item.Id == 1);
    }

    [Fact]
    public void RemoveAndClear_UpdateSelectionState()
    {
        var state = new CompareSelectionState();

        state.AddOrUpdate(new CompareSelectionItem(4, "Xiaomi", "xiaomi", "Xiaomi 15T Pro", "xiaomi-15t-pro", "Top de linha", true));
        state.AddOrUpdate(new CompareSelectionItem(10, "Samsung", "samsung", "Galaxy S25", "galaxy-s25", "Top de linha", true));

        var removed = state.Remove(4);

        Assert.True(removed);
        Assert.Equal("/comparar?ids=10", state.BuildCompareUrl());

        state.Clear();

        Assert.Equal(0, state.Count);
        Assert.Equal("/comparar", state.BuildCompareUrl());
    }

    [Fact]
    public async Task EnsureHydratedAsync_LoadsStoredSelection_WhenStateStartsEmpty()
    {
        var jsRuntime = new TestJsRuntime(
            """[{"id":4,"brand":"Xiaomi","brandSlug":"xiaomi","name":"Xiaomi 15T Pro","slug":"xiaomi-15t-pro","tier":"Top de linha","hasFullCatalogEntry":true}]""");
        var state = new CompareSelectionState(jsRuntime);

        var changed = await state.EnsureHydratedAsync();

        Assert.True(changed);
        Assert.Equal([4], state.Ids);
        Assert.Equal("/comparar?ids=4", state.BuildCompareUrl());
    }

    [Fact]
    public async Task EnsureHydratedAsync_PrefersCurrentSelection_OverStoredSelection()
    {
        var jsRuntime = new TestJsRuntime(
            """[{"id":10,"brand":"Samsung","brandSlug":"samsung","name":"Galaxy S25","slug":"galaxy-s25","tier":"Top de linha","hasFullCatalogEntry":true}]""");
        var state = new CompareSelectionState(jsRuntime);
        state.AddOrUpdate(new CompareSelectionItem(4, "Xiaomi", "xiaomi", "Xiaomi 15T Pro", "xiaomi-15t-pro", "Top de linha", true));

        var changed = await state.EnsureHydratedAsync();

        Assert.False(changed);
        Assert.Equal([4], state.Ids);
        Assert.Contains(jsRuntime.SavedPayloads, payload => payload.Contains("\"id\":4", StringComparison.Ordinal));
    }

    private sealed class TestJsRuntime(string initialPayload) : IJSRuntime
    {
        private string _storedPayload = initialPayload;

        public List<string> SavedPayloads { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return identifier switch
            {
                "spectenCompareSelection.load" => ValueTask.FromResult((TValue)(object)_storedPayload),
                "spectenCompareSelection.save" => Persist<TValue>(args),
                _ => throw new InvalidOperationException($"Unexpected JS interop call: {identifier}")
            };
        }

        private ValueTask<TValue> Persist<TValue>(object?[]? args)
        {
            var payload = args?.FirstOrDefault()?.ToString() ?? "";
            _storedPayload = payload;
            SavedPayloads.Add(payload);
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
