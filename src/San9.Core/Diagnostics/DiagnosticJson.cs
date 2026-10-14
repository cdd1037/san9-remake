using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using San9.Core.Simulation;

namespace San9.Core.Diagnostics;

public sealed record StateDifference(string Path, string Expected, string Actual);

/// <summary>Explicit diagnostic comparison. Never used to validate or identify a runtime object.</summary>
public static class DiagnosticJson
{
    public static string Text(JsonElement value) => System.Text.Encoding.UTF8.GetString(CanonicalJson.Encode(value));

    public static StateDifference? Compare(JsonElement expected, JsonElement actual, string path = "$") =>
        JsonElement.DeepEquals(expected, actual) ? null : FindDifference(expected, actual, path);

    internal static StateDifference? FindDifference(JsonElement expected, JsonElement actual, string path)
    {
        if (expected.ValueKind != actual.ValueKind)
            return new(path, expected.GetRawText(), actual.GetRawText());
        if (expected.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in expected.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (!actual.TryGetProperty(property.Name, out var value))
                    return new(path + "." + property.Name, property.Value.GetRawText(), "<missing>");
                if (FindDifference(property.Value, value, path + "." + property.Name) is { } difference) return difference;
            }
            foreach (var property in actual.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                if (!expected.TryGetProperty(property.Name, out _))
                    return new(path + "." + property.Name, "<missing>", property.Value.GetRawText());
            return null;
        }
        if (expected.ValueKind == JsonValueKind.Array)
        {
            int length = expected.GetArrayLength();
            if (length != actual.GetArrayLength())
                return new(path + ".length", length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    actual.GetArrayLength().ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (int i = 0; i < length; i++)
                if (FindDifference(expected[i], actual[i], $"{path}[{i}]") is { } difference) return difference;
            return null;
        }
        return JsonElement.DeepEquals(expected, actual) ? null : new(path, expected.GetRawText(), actual.GetRawText());
    }
}

/// <summary>Compares one state component at a time with generated metadata; explains only an unequal component.</summary>
public struct DiagnosticComparison(JsonSerializerContext context, bool describeDifference)
{
    public StateDifference? Difference { get; private set; }

    public bool Check<T>(T expected, T actual, string path)
    {
        // Record equality and immutable-array identity can prove equality, but inequality needs content comparison.
        if (EqualityComparer<T>.Default.Equals(expected, actual)) return true;
        var metadata = (JsonTypeInfo<T>)context.GetTypeInfo(typeof(T))!;
        var left = JsonSerializer.SerializeToElement(expected, metadata);
        var right = JsonSerializer.SerializeToElement(actual, metadata);
        if (JsonElement.DeepEquals(left, right)) return true;
        if (describeDifference) Difference = DiagnosticJson.FindDifference(left, right, path);
        return false;
    }
}
