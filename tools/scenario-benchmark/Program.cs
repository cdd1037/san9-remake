using San9.Application.Diagnostics;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using San9.Application;
using San9.Application.Persistence;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

if (args.Length < 5) throw new ArgumentException("Usage: scenario.json unit-count xun-count march|combat report-path [verify|navigation|quality|queries|record]");
var original = JsonSerializer.Deserialize<ScenarioInitialState>(File.ReadAllText(args[0], Encoding.UTF8), ScenarioContentFiles.DataJsonOptions)!;
int count = int.Parse(args[1]);
int xuns = int.Parse(args[2]);
if (count < 0 || xuns <= 0 || args[3] is not ("march" or "combat")) throw new ArgumentException("Invalid count, duration or mode.");
bool combat = args[3] == "combat";
string report = args[4];
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
File.WriteAllText(report, "", Encoding.UTF8);
var definition = count == 0 ? original : Populate(original, count, combat);
if (args.Contains("quality"))
{
    NavigationBenchmark.Quality(definition, Log);
    return;
}
if (args.Contains("navigation"))
{
    NavigationBenchmark.Run(definition, xuns, Log, args.Contains("full-route"));
    return;
}
var startup = Stopwatch.StartNew();
var session = GameSession.Start(definition, 42, playerForceController: ControllerKind.Automatic);
if (args.Contains("queries"))
{
    QueryBenchmark.Run(session.Snapshot, new MilitaryRulebook(definition.Military!), Log);
    return;
}
if (args.Contains("record")) session.BeginRecording();
Log($"rules={SessionProtocol.RulesVersion} requested={count} actual={session.Snapshot.Units.Count} mode={args[3]} player_controller={session.PlayerForceController} startup_ms={startup.Elapsed.TotalMilliseconds:F2}");
GameSession? restored = null;
for (int i = 0; i < xuns; i++)
{
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    using var diagnostics = new PathSearchDiagnostics();
    var clock = Stopwatch.StartNew();
    var result = session.AdvanceXun();
    double ms = clock.Elapsed.TotalMilliseconds;
    double mib = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 1048576.0;
    var moves = result.Events.OfType<UnitMovedEvent>().ToArray();
    Log($"xun={i + 1} ms={ms:F2} allocated_MiB={mib:F2} units={session.Snapshot.Units.Count} " +
        $"moved={moves.Count(e => e.From != e.To)} blocked={moves.Count(e => e.Status == NavigationStatus.Unreachable)} waiting={moves.Count(e => e.Status == NavigationStatus.Waiting)} " +
        $"damages={result.Events.OfType<BattleDamageEvent>().Count()} events={result.Events.Count}");
    Log($"searches={diagnostics.Queries} expanded={diagnostics.Expanded} maximum_expanded={diagnostics.MaximumExpanded} workspace_nodes={diagnostics.WorkspaceNodes}");
    if (restored is not null)
    {
        restored.AdvanceXun();
        if (SessionDiagnostics.Compare(session.Capture(), restored.Capture()) is { } difference)
            throw new InvalidOperationException($"Restore diverged at {difference.Path}: {difference.Expected} != {difference.Actual}.");
    }
    if (i == 0 && args.Contains("verify")) restored = SaveService.Decode(SaveService.Encode(session), definition);
}
Log($"completed restore_verified={restored is not null && xuns > 1} revision={session.Snapshot.Revision}");
if (args.Contains("record"))
{
    string recordingPath = report + ".recording.json";
    RecordingService.Write(recordingPath, session.EndRecording());
    var replay = RecordingService.Replay(recordingPath, definition, verifyEvents: true);
    if (SessionDiagnostics.Compare(session.Capture(), replay.Session.Capture()) is { } difference)
        throw new InvalidOperationException($"Recording diverged at {difference.Path}: {difference.Expected} != {difference.Actual}.");
    Log("recording_verified=True");
}

void Log(string line)
{
    Console.WriteLine(line);
    File.AppendAllText(report, line + Environment.NewLine, Encoding.UTF8);
}

ScenarioInitialState Populate(ScenarioInitialState source, int number, bool battle)
{
    if (source.Units.Length != 0) throw new ArgumentException("Stress construction requires an opening without field units.");
    var rules = new MilitaryRulebook(source.Military ?? throw new ArgumentException("Missing military map."));
    var formation = rules.Formations.Values.Where(f => f.TravelMode == TravelMode.Land && f.RequiredTacticId is null)
        .OrderBy(f => f.Id).First();
    var cities = source.Settlements.Where(s => !s.IsDormant && s.OwnerId is not null && s.Kind == SettlementKind.City).OrderBy(s => s.Id).ToArray();
    var people = source.Officers.ToArray();
    var candidates = Enumerable.Range(0, people.Length).Where(i => !people[i].State.IsDormant && people[i].State.Status is not (OfficerStatus.Dead or OfficerStatus.Special))
        .OrderBy(i => people[i].State.IsServing ? 0 : 1).ThenBy(i => people[i].State.Id).Take(number).ToArray();
    if (candidates.Length != number) throw new ArgumentException("Not enough authored officers for one officer per unit.");
    var occupancy = rules.Places.Values.Select(p => new MapOccupancy(p.Position, p.Footprint)).ToList();
    var random = new Random(42);
    var units = new List<UnitState>();
    int activated = 0;
    foreach (int index in candidates)
    {
        var person = people[index].State;
        var homes = cities.Where(c => c.OwnerId == person.OwnerId).ToArray();
        var home = homes.Length == 0 ? cities[units.Count % cities.Length] : homes[units.Count % homes.Length];
        int owner = home.OwnerId!.Value;
        var enemies = cities.Where(c => c.OwnerId != owner && source.Information.Relations.Any(r =>
            r.FirstId == Math.Min(owner, c.OwnerId!.Value) && r.SecondId == Math.Max(owner, c.OwnerId.Value) && r.Value < 40)).ToArray();
        var target = enemies.OrderBy(c => Distance(c.Position, home.Position)).FirstOrDefault() ?? cities.Where(c => c.OwnerId != owner)
            .OrderBy(c => Distance(c.Position, home.Position)).First();
        var center = battle ? target.Position : home.Position;
        WorldPosition? position = null;
        for (int attempt = 0; attempt < 20000; attempt++)
        {
            int radius = battle ? 20 + attempt / 200 : 80 + attempt / 200;
            var candidate = new WorldPosition(center.X + random.Next(-radius, radius + 1), center.Y + random.Next(-radius, radius + 1));
            if (rules.Map.PassageCost(candidate, formation.Footprint, TravelMode.Land, occupancy) is not null)
            { position = candidate; break; }
        }
        if (position is null) throw new InvalidOperationException("Could not place stress unit without overlap.");
        int id = units.Count;
        units.Add(new(id, owner, position.Value, 5000)
        {
            FormationId = formation.Id, NavalFormationId = rules.NavalFormationForDeployment(formation.Id), Morale = 90, CommanderId = person.Id,
            MemberRanks = System.Collections.Immutable.ImmutableDictionary<int, BattleRank>.Empty.Add(person.Id, formation.CaptureRanks[0]),
            ServicePlaceId = home.Id, CorpsId = home.CorpsId, OrderClass = UnitOrderClass.Military,
            Order = new(MilitaryOrderKind.AttackSettlement, null, target.Id)
        });
        if (!person.IsServing) activated++;
        people[index] = people[index] with { State = person with
        {
            OwnerId = owner, SettlementId = null, UnitId = id, RegionId = null, IsCommander = false,
            Status = person.IsServing ? person.Status : OfficerStatus.Serving, StatusStartYear = null
        } };
        occupancy.Add(new(position.Value, formation.Footprint));
    }
    Log($"fixture units={number} activated_officers={activated} soldiers_added={number * 5000L} " +
        $"targets={units.Select(u => u.Order!.SettlementId).Distinct().Count()} formation={formation.Id} naval={rules.NavalFormationForDeployment(formation.Id)}");
    return source with { Units = units.ToArray(), Officers = people };
}

static long Distance(WorldPosition a, WorldPosition b) => Math.Abs((long)a.X - b.X) + Math.Abs((long)a.Y - b.Y);
