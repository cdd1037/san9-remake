using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application;
using San9.Application.Persistence;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Headless.Tests;

// Process coverage is limited to entry-point wiring, standard streams and cross-process artifacts.
public sealed class HeadlessProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "san9-cli-" + Guid.NewGuid().ToString("N"));
    public HeadlessProcessTests() => Directory.CreateDirectory(_directory);
    public void Dispose()
    {
        if (Path.GetDirectoryName(Path.GetFullPath(_directory)) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        Directory.Delete(_directory, recursive: true);
    }
    private string FilePath(string name) => Path.Combine(_directory, name);
    private SessionSnapshot ReadSession(string name) => StrictJson.Deserialize<SaveEnvelope>(File.ReadAllBytes(FilePath(name))).Session;

    [Fact]
    public async Task EntryPointRunsAllModesAcrossProcessesAndEmitsRuleWarnings()
    {
        var verified = await Run("verify", "--fixture", "--xuns", "1", "--output", _directory);
        Assert.Equal(0, verified.Code);
        Assert.True(verified.Report.GetProperty("success").GetBoolean());
        Assert.Equal(1, verified.Report.GetProperty("completedXun").GetInt32());
        string[] lines = File.ReadAllLines(FilePath("commands.jsonl"), Encoding.UTF8);
        var header = JsonNode.Parse(lines[0])!;
        header["rulesVersion"] = "older-process-test-rules";
        lines[0] = header.ToJsonString();
        File.WriteAllLines(FilePath("commands.jsonl"), lines, new UTF8Encoding(false));
        var run = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--restore", FilePath("initial.save.json"),
            "--save", FilePath("run.save.json"), "--report", FilePath("run-report.json"));
        Assert.Equal(0, run.Code);
        Assert.Single(run.Report.GetProperty("rulesVersionWarnings").EnumerateArray());
        StateAssert.Equal(ReadSession("continuous.save.json"), ReadSession("run.save.json"));
        using var report = JsonDocument.Parse(File.ReadAllBytes(FilePath("run-report.json")));
        Assert.True(JsonElement.DeepEquals(run.Report, report.RootElement));
    }

    [Fact]
    public async Task InvalidModeReturnsUsageExitCodeAndMachineReadableOutput()
    {
        var result = await Run("unsupported");
        Assert.Equal(2, result.Code);
        Assert.Equal("usage", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
    }

    private static async Task<(int Code, JsonElement Report)> Run(params string[] args)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "San9Remake.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string assembly = Path.Combine(root.FullName, "src", "San9.Headless", "bin", configuration, "net10.0", "San9.Headless.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(assembly);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        string output = await stdout;
        string errors = await stderr;
        using var document = JsonDocument.Parse(output);
        string expectedErrors = string.Concat(document.RootElement.GetProperty("rulesVersionWarnings")
            .EnumerateArray().Select(w => w.GetProperty("message").GetString() + Environment.NewLine));
        Assert.Equal(expectedErrors, errors);
        return (process.ExitCode, document.RootElement.Clone());
    }
}
