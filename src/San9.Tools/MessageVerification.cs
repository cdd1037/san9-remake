using System.Text;
using System.Text.Json;
using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Content.Text;

internal static class MessageVerification
{
    public static void Run(string directory)
    {
        var results = new List<object>();
        foreach (string catalog in new[] { "messages", "biographies", "base-messages", "base-biographies" })
        {
            var messages = JsonSerializer.Deserialize<Dictionary<string, MessageDefinition>>(
                File.ReadAllText(Path.Combine(directory, catalog + ".json"), Encoding.UTF8), PackageFiles.JsonOptions)!;
            MessageGraph.Validate(messages);
            var evaluator = new MessageEvaluator(messages);
            int ready = 0, runs = 0;
            var failures = new List<object>();
            foreach (string key in messages.Keys)
            {
                var requirements = MessageGraph.Requirements(key, messages);
                if (!requirements.Executable) continue;
                ready++;
                foreach (int value in new[] { 0, 1, 100 })
                {
                    var bindings = requirements.Bindings.Where(b => !b.StartsWith("query:", StringComparison.Ordinal))
                        .ToDictionary(b => b, b => new MessageBinding(value,
                            b.StartsWith("string.", StringComparison.Ordinal) || b.EndsWith(".name", StringComparison.Ordinal) ? "测试[b]" : null));
                    var context = new MessageContext(bindings, (_, _) => new(value), bound => value == 0 ? 0 : bound - 1);
                    try { _ = evaluator.Evaluate(key, context); runs++; }
                    catch (InvalidDataException e) { failures.Add(new { Key = key, Context = value, Reason = e.Message }); }
                }
            }
            results.Add(new { Catalog = catalog, ConfirmedDependencies = ready, SyntheticRuns = runs, Failures = failures });
        }
        File.WriteAllText(Path.Combine(directory, "synthetic-evaluation.json"), JsonSerializer.Serialize(new
        {
            Scope = "Three explicit synthetic contexts per confirmed dependency graph; not gameplay bindings or exhaustive branch coverage.",
            Results = results
        }, PackageFiles.JsonOptions), Encoding.UTF8);
    }
}
