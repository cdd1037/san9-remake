using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace San9.Content.Packages;

/// <summary>Validates reference nullability, including collection elements not checked by System.Text.Json.</summary>
public static class ContentStructure
{
    private static readonly ConcurrentDictionary<Type, (PropertyInfo Property, NullabilityInfo Nullability)[]> Properties = new();

    public static void Validate(object value)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(value, null, "$", visited);
    }

    private static void Visit(object? value, NullabilityInfo? contract, string path, HashSet<object> visited)
    {
        if (value is null)
        {
            if (contract?.ReadState != NullabilityState.Nullable)
                throw new InvalidDataException($"Null content value at {path}.");
            return;
        }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string or decimal or DateTime or DateTimeOffset or Guid) return;
        if (!type.IsValueType && !visited.Add(value)) return;
        if (value is IDictionary dictionary)
        {
            var itemContract = contract?.GenericTypeArguments.LastOrDefault();
            foreach (DictionaryEntry entry in dictionary)
                Visit(entry.Value, itemContract, $"{path}[{entry.Key}]", visited);
            return;
        }
        if (value is IEnumerable sequence)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Immutable.ImmutableArray<>) &&
                (bool)type.GetProperty("IsDefault")!.GetValue(value)!)
                throw new InvalidDataException($"Uninitialized content collection at {path}.");
            var itemContract = contract?.ElementType ?? contract?.GenericTypeArguments.LastOrDefault();
            int index = 0;
            foreach (var item in sequence) Visit(item, itemContract, $"{path}[{index++}]", visited);
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
            Visit(property.GetValue(value), nullability, $"{path}.{property.Name}", visited);
    }
}
