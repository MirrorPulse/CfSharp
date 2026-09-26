using System.Diagnostics;
using System.Globalization;
using System.Text;
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

            Console.Error.WriteLine(SerializeActivity(activity, tags));
        }
        catch
        {
            // Diagnostics must never change provider behavior or callback completion.
        }
    }

    private static string SerializeActivity(
        Activity activity,
        IReadOnlyDictionary<string, object?> tags)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("OperationName", activity.OperationName);
            writer.WriteString(
                "Duration",
                activity.Duration.ToString("c", CultureInfo.InvariantCulture));
            writer.WritePropertyName("Tags");
            writer.WriteStartObject();
            foreach ((string key, object? value) in tags)
            {
                writer.WritePropertyName(key);
                WriteTagValue(writer, value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteTagValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case byte number:
                writer.WriteNumberValue(number);
                break;
            case sbyte number:
                writer.WriteNumberValue(number);
                break;
            case short number:
                writer.WriteNumberValue(number);
                break;
            case ushort number:
                writer.WriteNumberValue(number);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case uint number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case ulong number:
                writer.WriteNumberValue(number);
                break;
            case float number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime);
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset);
                break;
            case Guid guid:
                writer.WriteStringValue(guid);
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
