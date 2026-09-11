using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace San9.Headless.Tests;

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

    [Fact]
    public async Task ExecutableRunsCommandsSavesResumesAndSkipsRepeatedPrefix()
    {
        var verification = await Run("verify", "--fixture", "--xuns", "2", "--output", _directory);
        Assert.Equal(0, verification.Code);
        Assert.True(verification.Report.GetProperty("success").GetBoolean());
        Assert.Equal(2, verification.Report.GetProperty("completedXun").GetInt32());
        Assert.Equal(4, verification.Report.GetProperty("developmentCommands").GetInt32());
        string finalHash = verification.Report.GetProperty("finalHash").GetString()!;
        var replay = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--load", FilePath("initial.save.json"),
            "--save", FilePath("replay.save.json"), "--report", FilePath("replay-report.json"));
        Assert.Equal(0, replay.Code);
        Assert.Equal(finalHash, replay.Report.GetProperty("finalHash").GetString());
        Assert.Equal(File.ReadAllBytes(FilePath("events.jsonl")), File.ReadAllBytes(FilePath("replay-report.events.jsonl")));
        var repeated = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--load", FilePath("replay.save.json"),
            "--save", FilePath("replay.save.json"), "--report", FilePath("duplicate-report.json"));
        Assert.Equal(0, repeated.Code);
        Assert.Equal(0, repeated.Report.GetProperty("acceptedOperations").GetInt32());
        Assert.Equal(11, repeated.Report.GetProperty("alreadyProcessedOperations").GetInt32());
        Assert.Equal(finalHash, repeated.Report.GetProperty("finalHash").GetString());
        Assert.Equal(0, new FileInfo(FilePath("duplicate-report.events.jsonl")).Length);
    }

    [Fact]
    public async Task ExplicitRecordingAndPlaybackUseSeparateArtifacts()
    {
        Assert.Equal(0, (await Run("verify", "--fixture", "--xuns", "2", "--output", _directory)).Code);
        var run = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--report", FilePath("record-report.json"),
            "--save", FilePath("player.save.json"), "--record", FilePath("debug.recording.json"));
        Assert.Equal(0, run.Code);
        var playback = await Run("replay", "--fixture", "--recording", FilePath("debug.recording.json"),
            "--report", FilePath("playback-report.json"), "--save", FilePath("playback.save.json"));
        Assert.Equal(0, playback.Code);
        Assert.Equal(2, playback.Report.GetProperty("completedXun").GetInt32());
        Assert.Equal(run.Report.GetProperty("finalHash").GetString(), playback.Report.GetProperty("finalHash").GetString());
        Assert.Equal(File.ReadAllBytes(FilePath("player.save.json")), File.ReadAllBytes(FilePath("playback.save.json")));
        Assert.Equal(File.ReadAllBytes(FilePath("record-report.events.jsonl")), File.ReadAllBytes(FilePath("playback-report.events.jsonl")));
        Assert.DoesNotContain("operations", File.ReadAllText(FilePath("player.save.json")));
        var midway = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--load", FilePath("checkpoint.save.json"),
            "--report", FilePath("midway-report.json"), "--record", FilePath("midway.recording.json"));
        Assert.Equal(0, midway.Code);
        Assert.Equal(1, midway.Report.GetProperty("completedXun").GetInt32());
        Assert.True(midway.Report.GetProperty("alreadyProcessedOperations").GetInt32() > 0);
        var midPlayback = await Run("replay", "--fixture", "--recording", FilePath("midway.recording.json"), "--report", FilePath("mid-playback-report.json"));
        Assert.Equal(0, midPlayback.Code);
        Assert.Equal(1, midPlayback.Report.GetProperty("completedXun").GetInt32());
        Assert.Equal(run.Report.GetProperty("finalHash").GetString(), midPlayback.Report.GetProperty("finalHash").GetString());
        var conflict = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--report", FilePath("conflict.json"), "--record", FilePath("commands.jsonl"));
        Assert.Equal(2, conflict.Code);
        var node = JsonNode.Parse(File.ReadAllBytes(FilePath("debug.recording.json")))!;
        node["formatVersion"] = 999;
        File.WriteAllText(FilePath("bad.recording.json"), node.ToJsonString());
        var bad = await Run("replay", "--fixture", "--recording", FilePath("bad.recording.json"), "--report", FilePath("bad-record-report.json"));
        Assert.Equal(5, bad.Code);
        Assert.Equal("recording", bad.Report.GetProperty("firstFailure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MalformedLogReportsFailureLineAndKeepsAcceptedProgress()
    {
        Assert.Equal(0, (await Run("verify", "--fixture", "--xuns", "1", "--output", _directory)).Code);
        string[] lines = File.ReadAllLines(FilePath("commands.jsonl"));
        File.WriteAllLines(FilePath("bad.jsonl"), [lines[0], lines[1], "{broken}"]);
        var result = await Run("run", "--fixture", "--log", FilePath("bad.jsonl"), "--save", FilePath("partial.save.json"),
            "--report", FilePath("bad-report.json"));
        Assert.Equal(5, result.Code);
        Assert.False(result.Report.GetProperty("success").GetBoolean());
        Assert.Equal(3, result.Report.GetProperty("firstFailure").GetProperty("line").GetInt32());
        Assert.Equal(1, result.Report.GetProperty("acceptedOperations").GetInt32());
        using var save = JsonDocument.Parse(File.ReadAllBytes(FilePath("partial.save.json")));
        Assert.Equal(1, save.RootElement.GetProperty("session").GetProperty("chronicle").GetArrayLength());
        Assert.True(File.Exists(FilePath("bad-report.json")));
    }

    [Fact]
    public async Task UnknownSaveVersionHasSpecificFailureCode()
    {
        Assert.Equal(0, (await Run("verify", "--fixture", "--xuns", "1", "--output", _directory)).Code);
        var save = JsonNode.Parse(File.ReadAllBytes(FilePath("initial.save.json")))!;
        save["formatVersion"] = 999;
        File.WriteAllText(FilePath("old.save.json"), save.ToJsonString());
        var result = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--load", FilePath("old.save.json"),
            "--report", FilePath("old-report.json"));
        Assert.Equal(4, result.Code);
        Assert.Equal("save-UnsupportedVersion", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownCommandAndConflictingPathsAreRejected()
    {
        Assert.Equal(0, (await Run("verify", "--fixture", "--xuns", "1", "--output", _directory)).Code);
        string[] lines = File.ReadAllLines(FilePath("commands.jsonl"));
        var request = JsonNode.Parse(lines[1])!;
        request["operation"]!["type"] = "teleport";
        File.WriteAllLines(FilePath("unknown.jsonl"), [lines[0], request.ToJsonString()]);
        var unknown = await Run("run", "--fixture", "--log", FilePath("unknown.jsonl"), "--report", FilePath("unknown-report.json"));
        Assert.Equal(5, unknown.Code);
        Assert.Equal(2, unknown.Report.GetProperty("firstFailure").GetProperty("line").GetInt32());
        byte[] before = File.ReadAllBytes(FilePath("commands.jsonl"));
        var conflict = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--report", FilePath("commands.jsonl"));
        Assert.Equal(2, conflict.Code);
        Assert.Equal(before, File.ReadAllBytes(FilePath("commands.jsonl")));
    }

    [Fact]
    public async Task UsageFailureStillWritesMachineReadableStandardOutput()
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
        Assert.True(string.IsNullOrWhiteSpace(errors), errors);
        using var document = JsonDocument.Parse(output);
        return (process.ExitCode, document.RootElement.Clone());
    }
}
