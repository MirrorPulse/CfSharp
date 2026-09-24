using System.Diagnostics;
using System.Text.Json;

using CfSharp;

internal static class SampleDiagnostics
{
    private const string EnableVariable = "CFSHARP_DIAGNOSTICS";

    internal static ActivityListener? TryEnable()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(EnableVariable),
                "1",
                StringComparison.Ordinal))
        {
            return null;
        }

        ActivityListener listener = new()
        {
            ShouldListenTo = static source => source.Name == CloudDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = WriteActivity,
        };
        ActivitySource.AddActivityListener(listener);
        Console.Error.WriteLine(
            $"CfSharp diagnostics enabled by {EnableVariable}=1; " +
            "only cfsharp.* activity tags are emitted.");
        return listener;
    }

    private static void WriteActivity(Activity activity)
    {
        try
        {
            Dictionary<string, object?> tags = new(StringComparer.Ordinal);
            foreach ((string key, object? value) in activity.TagObjects)
            {
                if (key.StartsWith("cfsharp.", StringComparison.Ordinal))
                {
                    tags[key] = value;
                }
            }

            if (tags.Count == 0)
            {
                return;
            }

            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                activity.OperationName,
                activity.Duration,
                Tags = tags,
            }));
        }
        catch
        {
            // Diagnostics must never change provider behavior or callback completion.
        }
    }
}
