using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace San9.Content.Packages;

/// <summary>Validates reference nullability, including collection elements not checked by System.Text.Json.</summary>
public static class ContentStructure
{
    private static readonly ConcurrentDictionary<Type, (PropertyInfo Property, NullabilityInfo Nullability)[]> Properties = new();

    public static void Validate(object value)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(value, null, [], visited);
    }

    private readonly record struct PathPart(string? Property = null, object? Key = null, int? Index = null);

    // Successful validation should not allocate a full path for every node. Only
    // materialize the current traversal path when reporting an invalid value.
    private static string DescribePath(List<PathPart> path)
    {
        var text = new StringBuilder("$");
        foreach (var part in path)
            if (part.Property is { } property) text.Append('.').Append(property);
            else if (part.Index is int index) text.Append('[').Append(index).Append(']');
            else text.Append('[').Append(part.Key).Append(']');
        return text.ToString();
    }

    private static void Visit(object? value, NullabilityInfo? contract, List<PathPart> path, HashSet<object> visited)
    {
        if (value is null)
        {
            if (contract?.ReadState != NullabilityState.Nullable)
                throw new InvalidDataException($"Null content value at {DescribePath(path)}.");
            return;
        }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string or decimal or DateTime or DateTimeOffset or Guid) return;
        if (!type.IsValueType && !visited.Add(value)) return;
        if (value is IDictionary dictionary)
        {
            var itemContract = contract?.GenericTypeArguments.LastOrDefault();
            foreach (DictionaryEntry entry in dictionary)
            {
                path.Add(new(Key: entry.Key));
                Visit(entry.Value, itemContract, path, visited);
                path.RemoveAt(path.Count - 1);
            }
            return;
        }
        if (value is IEnumerable sequence)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Immutable.ImmutableArray<>) &&
                (bool)type.GetProperty("IsDefault")!.GetValue(value)!)
                throw new InvalidDataException($"Uninitialized content collection at {DescribePath(path)}.");
            var itemContract = contract?.ElementType ?? contract?.GenericTypeArguments.LastOrDefault();
            int index = 0;
            foreach (var item in sequence)
            {
                path.Add(new(Index: index++));
                Visit(item, itemContract, path, visited);
                path.RemoveAt(path.Count - 1);
            }
            return;
        }
        foreach (var (property, nullability) in Properties.GetOrAdd(type, static type =>
        {
            var context = new NullabilityInfoContext();
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetMethod is not null && p.GetIndexParameters().Length == 0 &&
                    p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always)
                .Select(p => (p, context.Create(p))).ToArray();
        }))
        {
            path.Add(new(Property: property.Name));
            Visit(property.GetValue(value), nullability, path, visited);
            path.RemoveAt(path.Count - 1);
        }
    }
}
