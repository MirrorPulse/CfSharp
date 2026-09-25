using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CfSharp.Native.Tests;

public sealed class NativeCoverageInventoryTests
{
    private const int ExpectedSymbolCount = 116;
    private const int ExpectedFunctionCount = 36;
    private const int ExpectedMappedSymbolCount = 116;
    private const int ExpectedMappedFunctionCount = 36;
    private const string ExpectedPinnedHeaderSymbolFingerprint =
        "086596e9d2d29e29ad58c7803cc02d0070ad8a31510fc6b605de453ef30fc70c";
    private static readonly string[] ValidRoutes =
        ["HighLevelPublic", "HighLevelInternal", "NativeOnlyDocumented"];

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
        Assert.Equal(
            "10.0.17134",
            root.GetProperty("versionOverrides").GetProperty("CfReportSyncStatus").GetString());
        Assert.Equal(
            "10.0.17134",
            root.GetProperty("versionOverrides").GetProperty("CF_SYNC_STATUS").GetString());
        Assert.Equal(4, root.GetProperty("capabilityGates").GetArrayLength());

        JsonElement routeMetadata = root.GetProperty("routeMetadata");
        Assert.Equal("HighLevelInternal", routeMetadata.GetProperty("defaultRoute").GetString());
        Assert.False(string.IsNullOrWhiteSpace(routeMetadata.GetProperty("documentation").GetString()));
        JsonElement nativeOnlyRoute = routeMetadata
            .GetProperty("routes")
            .GetProperty("CfGetPlaceholderRangeInfoForHydration");
        Assert.Equal("NativeOnlyDocumented", nativeOnlyRoute.GetProperty("route").GetString());
        Assert.False(string.IsNullOrWhiteSpace(nativeOnlyRoute.GetProperty("reason").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(nativeOnlyRoute.GetProperty("nativeDocumentation").GetString()));

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

            string nativeName = GetNativeName(entry);
            string route = routeMetadata.GetProperty("routes").TryGetProperty(nativeName, out JsonElement overrideRoute)
                ? overrideRoute.GetProperty("route").GetString()!
                : routeMetadata.GetProperty("defaultRoute").GetString()!;
            Assert.Contains(route, ValidRoutes);
        }

        Assert.All(entries, entry => Assert.Equal("mapped", entry.GetProperty("status").GetString()));
    }

    [Fact]
    public void CapabilityMatrixMatchesPinnedCloudFilesContract()
    {
        using JsonDocument document = LoadInventory();
        JsonElement root = document.RootElement;
        JsonElement header = root.GetProperty("header");

        Assert.Equal("10.0.16299", header.GetProperty("defaultMinimumWindowsVersion").GetString());

        Dictionary<string, uint> expectedGates = new(StringComparer.Ordinal)
        {
            ["CF_PLACEHOLDER_MANAGEMENT_POLICY non-default flags"] = 784,
            ["CF_HYDRATION_POLICY_MODIFIER_ALLOW_FULL_RESTART_HYDRATION"] = 1280,
            ["CF_CONVERT_FLAG_FORCE_CONVERT_TO_CLOUD_FILE"] = 1280,
            ["CfGetPlaceholderRangeInfoForHydration"] = 1536,
        };

        Dictionary<string, uint> actualGates = root
            .GetProperty("capabilityGates")
            .EnumerateArray()
            .ToDictionary(
                gate => gate.GetProperty("nativeName").GetString()!,
                gate => gate.GetProperty("minimumIntegrationNumber").GetUInt32(),
                StringComparer.Ordinal);

        Assert.Equal(expectedGates.Count, actualGates.Count);
        foreach ((string nativeName, uint minimumIntegrationNumber) in expectedGates)
        {
            Assert.True(actualGates.TryGetValue(nativeName, out uint actualMinimum), nativeName);
            Assert.Equal(minimumIntegrationNumber, actualMinimum);
        }

        Version defaultWindowsVersion = Version.Parse(header.GetProperty("defaultMinimumWindowsVersion").GetString()!);
        foreach (JsonProperty overrideVersion in root.GetProperty("versionOverrides").EnumerateObject())
        {
            Version parsedVersion = Version.Parse(overrideVersion.Value.GetString()!);
            Assert.True(parsedVersion >= defaultWindowsVersion, overrideVersion.Name);
        }

        JsonElement hydrationRoute = root
            .GetProperty("routeMetadata")
            .GetProperty("routes")
            .GetProperty("CfGetPlaceholderRangeInfoForHydration");
        Assert.Equal("10.0.16299", hydrationRoute.GetProperty("minimumWindowsVersion").GetString());
        Assert.Equal(1536u, hydrationRoute.GetProperty("minimumIntegrationNumber").GetUInt32());
    }

    [Fact]
    public void CurrentProcessArchitectureIsInStableSupportMatrix()
    {
        Assert.True(
            RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64,
            $"Unexpected process architecture: {RuntimeInformation.ProcessArchitecture}");
    }

    [Fact]
    public void InventoryExactlyMatchesPinnedHeaderContractAndManagedSymbols()
    {
        using JsonDocument document = LoadInventory();
        JsonElement[] entries = document.RootElement
            .GetProperty("symbols")
            .EnumerateArray()
            .ToArray();

        string canonicalSymbols = string.Join(
            '\n',
            entries
                .Select(entry => $"{entry.GetProperty("kind").GetString()}|{GetNativeName(entry)}")
                .OrderBy(value => value, StringComparer.Ordinal));
        string fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSymbols)))
            .ToLowerInvariant();
        Assert.Equal(ExpectedPinnedHeaderSymbolFingerprint, fingerprint);

        Assembly nativeAssembly = typeof(CfApi).Assembly;
        foreach (JsonElement entry in entries)
        {
            AssertManagedSymbolExists(
                nativeAssembly,
                entry.GetProperty("managedSymbol").GetString()!);
        }

        string[] inventoryFunctions = entries
            .Where(IsFunction)
            .Select(GetNativeName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] importedFunctions = typeof(CfApi)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.GetCustomAttribute<LibraryImportAttribute>() is not null)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(inventoryFunctions, importedFunctions);
    }

    [Fact]
    public void CldApiExportsEveryFunctionAvailableOnCurrentWindowsVersion()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        using JsonDocument document = LoadInventory();
        JsonElement root = document.RootElement;
        JsonElement versionOverrides = root.GetProperty("versionOverrides");
        string defaultVersion = root
            .GetProperty("header")
            .GetProperty("defaultMinimumWindowsVersion")
            .GetString()!;
        int platformInfoResult = CfApi.CfGetPlatformInfo(out CfPlatformInfo platformInfo);
        Assert.Equal(0, platformInfoResult);
        nint library = NativeLibrary.Load("CldApi.dll");

        try
        {
            foreach (JsonElement entry in root.GetProperty("symbols").EnumerateArray().Where(IsFunction))
            {
                string nativeName = GetNativeName(entry);
                string minimumVersion = versionOverrides.TryGetProperty(nativeName, out JsonElement version)
                    ? version.GetString()!
                    : defaultVersion;

                if (IsCurrentWindowsAtLeast(minimumVersion) &&
                    IsCapabilityAvailable(root, nativeName, platformInfo.IntegrationNumber))
                {
                    Assert.True(NativeLibrary.TryGetExport(library, nativeName, out _), nativeName);
                }
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static JsonDocument LoadInventory()
    {
        string inventoryPath = Path.Combine(AppContext.BaseDirectory, "cfapi-coverage.json");
        return JsonDocument.Parse(File.ReadAllText(inventoryPath));
    }

    private static void AssertManagedSymbolExists(Assembly assembly, string managedSymbol)
    {
        if (assembly.GetType(managedSymbol, throwOnError: false) is not null)
        {
            return;
        }

        int separator = managedSymbol.LastIndexOf('.');
        while (separator > 0)
        {
            string typeName = managedSymbol[..separator];
            Type? declaringType = assembly.GetType(typeName, throwOnError: false);
            if (declaringType is not null)
            {
                string memberName = managedSymbol[(separator + 1)..];
                MemberInfo[] members = declaringType.GetMember(
                    memberName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                Assert.True(members.Length > 0, managedSymbol);
                return;
            }

            separator = managedSymbol.LastIndexOf('.', separator - 1);
        }

        Assert.Fail($"Managed symbol '{managedSymbol}' could not be resolved.");
    }

    private static bool IsCurrentWindowsAtLeast(string versionText)
    {
        Version version = Version.Parse(versionText);
        return OperatingSystem.IsWindowsVersionAtLeast(
            version.Major,
            version.Minor,
            version.Build);
    }

    private static bool IsCapabilityAvailable(
        JsonElement root,
        string nativeName,
        uint currentIntegrationNumber)
    {
        foreach (JsonElement gate in root.GetProperty("capabilityGates").EnumerateArray())
        {
            if (gate.GetProperty("nativeName").GetString() == nativeName)
            {
                return currentIntegrationNumber >=
                    gate.GetProperty("minimumIntegrationNumber").GetUInt32();
            }
        }

        return true;
    }

    private static bool IsFunction(JsonElement entry) =>
        entry.GetProperty("kind").GetString() == "function";

    private static bool IsMapped(JsonElement entry) =>
        entry.GetProperty("status").GetString() == "mapped";

    private static string GetNativeName(JsonElement entry) =>
        entry.GetProperty("nativeName").GetString()!;
}
