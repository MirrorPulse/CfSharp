using System.Text.Json;

namespace CfSharp.Native.Tests;

public sealed class NativeCoverageInventoryTests
{
    private const int ExpectedSymbolCount = 116;
    private const int ExpectedFunctionCount = 36;
    private const int ExpectedMappedSymbolCount = 83;
    private const int ExpectedMappedFunctionCount = 18;

    [Fact]
    public void InventoryTracksPinnedHeaderAndCurrentCoverage()
    {
        string inventoryPath = Path.Combine(AppContext.BaseDirectory, "cfapi-coverage.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        JsonElement root = document.RootElement;
        JsonElement header = root.GetProperty("header");
        JsonElement symbols = root.GetProperty("symbols");

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("cfapi.h", header.GetProperty("name").GetString());
        Assert.Equal("10.0.26100.0", header.GetProperty("windowsSdkVersion").GetString());
        Assert.Equal(
            "c50fb112b31a38e91151b84c46fcfefeb395aa12faa60cdc270103cdce7c4ae7",
            header.GetProperty("sha256").GetString());
        Assert.Equal("10.0.16299", header.GetProperty("defaultMinimumWindowsVersion").GetString());
        Assert.Equal(
            "10.0.17763",
            root.GetProperty("versionOverrides").GetProperty("CfReportProviderProgress2").GetString());

        JsonElement[] entries = symbols.EnumerateArray().ToArray();
        Assert.Equal(ExpectedSymbolCount, entries.Length);
        Assert.Equal(ExpectedFunctionCount, entries.Count(IsFunction));
        Assert.Equal(ExpectedMappedSymbolCount, entries.Count(IsMapped));
        Assert.Equal(ExpectedMappedFunctionCount, entries.Count(entry => IsFunction(entry) && IsMapped(entry)));
        Assert.Equal(
            entries.Length,
            entries.Select(GetNativeName).Distinct(StringComparer.Ordinal).Count());

        foreach (JsonElement entry in entries)
        {
            string status = entry.GetProperty("status").GetString()!;
            Assert.True(status is "mapped" or "pending", GetNativeName(entry));

            JsonElement managedSymbol = entry.GetProperty("managedSymbol");
            if (status == "mapped")
            {
                Assert.False(string.IsNullOrWhiteSpace(managedSymbol.GetString()));
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, managedSymbol.ValueKind);
            }
        }
    }

    private static bool IsFunction(JsonElement entry) =>
        entry.GetProperty("kind").GetString() == "function";

    private static bool IsMapped(JsonElement entry) =>
        entry.GetProperty("status").GetString() == "mapped";

    private static string GetNativeName(JsonElement entry) =>
        entry.GetProperty("nativeName").GetString()!;
}
