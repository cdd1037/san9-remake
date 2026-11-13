using San9.Content.Interface;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Completes missing presentation roles while preserving the legacy package's authored bindings.</summary>
public static class LegacyPresentationUpgrade
{
    public static CommandPresentation Prepare(CommandPresentation source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Progress is not { } progress) return source;
        var defaults = ExecutionPresentationCompiler.Compile();
        static Dictionary<string, DecisionMessageDefinition> Merge(
            Dictionary<string, DecisionMessageDefinition> original, Dictionary<string, DecisionMessageDefinition> additions)
        {
            var result = new Dictionary<string, DecisionMessageDefinition>(original, StringComparer.Ordinal);
            foreach (var (key, value) in additions) result.TryAdd(key, value);
            return result;
        }
        return source with { Progress = progress with
        {
            Records = Merge(progress.Records, defaults.Records),
            Context = Merge(progress.Context, defaults.Context),
            Introduction = Merge(progress.Introduction, defaults.Introduction),
            MapSpeeches = Merge(progress.MapSpeeches, defaults.MapSpeeches),
            Feedback = Merge(progress.Feedback, defaults.Feedback)
        } };
    }
}
