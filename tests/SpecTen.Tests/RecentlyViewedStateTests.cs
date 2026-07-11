using Microsoft.JSInterop;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class RecentlyViewedStateTests
{
    [Fact]
    public void AddOrUpdate_MovesNewestItemToFront_AndCapsHistory()
    {
        var state = new RecentlyViewedState();

        for (var id = 1; id <= 9; id++)
        {
            state.AddOrUpdate(new RecentlyViewedItem(
                id,
                "Brand",
                "brand",
                $"Model {id}",
                $"model-{id}",
                "Top de linha",
                true,
                DateTimeOffset.UtcNow.AddMinutes(id)));
        }

        state.AddOrUpdate(new RecentlyViewedItem(
            4,
            "Brand",
            "brand",
            "Model 4",
            "model-4",
            "Top de linha",
            true,
            DateTimeOffset.UtcNow.AddHours(1)));

        Assert.Equal(8, state.Count);
        Assert.Equal([4, 9, 8, 7, 6, 5, 3, 2], state.Items.Select(item => item.Id).ToArray());
        Assert.DoesNotContain(state.Items, item => item.Id == 1);
    }

    [Fact]
    public async Task EnsureHydratedAsync_LoadsStoredHistory()
    {
        var jsRuntime = new TestJsRuntime(
            """[{"id":10,"brand":"Samsung","brandSlug":"samsung","name":"Galaxy S25","slug":"galaxy-s25","badgeLabel":"Top de linha","hasFullCatalogEntry":true,"viewedAt":"2026-07-07T12:00:00Z"}]""");
        var state = new RecentlyViewedState(jsRuntime);

        var changed = await state.EnsureHydratedAsync();

        Assert.True(changed);
        Assert.Single(state.Items);
        Assert.Equal(10, state.Items[0].Id);
        Assert.Equal("Galaxy S25", state.Items[0].Name);
    }

    private sealed class TestJsRuntime(string initialPayload) : IJSRuntime
    {
        private string _storedPayload = initialPayload;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return identifier switch
            {
                "spectenRecentlyViewed.load" => ValueTask.FromResult((TValue)(object)_storedPayload),
                "spectenRecentlyViewed.save" => Persist<TValue>(args),
                _ => throw new InvalidOperationException($"Unexpected JS interop call: {identifier}")
            };
        }

        private ValueTask<TValue> Persist<TValue>(object?[]? args)
        {
            _storedPayload = args?.FirstOrDefault()?.ToString() ?? "";
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
