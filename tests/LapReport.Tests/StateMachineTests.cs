using System.Text.Json.Nodes;
using Simulator;

namespace LapReport.Tests;

public class StateMachineTests
{
    private static JsonObject Definition() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "infra", "state-machine.asl.json")))!.AsObject();

    private static (string Status, string? Error, List<string> Path, List<int> Exits) Run(string input, string? failFirst = null)
    {
        var fargate = new FakeFargate(new() { ["FAIL_FIRST_ATTEMPTS"] = failFirst });
        var sm = new AslInterpreter(Definition(), fargate.Run);
        var (status, error, _) = sm.Execute(JsonNode.Parse(input)!);
        return (status, error, sm.Path, fargate.ExitCodes);
    }

    [Fact]
    public void Clean_run_succeeds_first_time()
    {
        var r = Run("""{"raceId":"indy-2026"}""");
        Assert.Equal("SUCCEEDED", r.Status);
        Assert.Equal(new[] { "Validate", "FirstAttempt", "RunReport", "Done" }, r.Path);
    }

    [Fact]
    public void Temporary_failures_back_off_and_retry()
    {
        var r = Run("""{"raceId":"indy-2026"}""", failFirst: "2");
        Assert.Equal("SUCCEEDED", r.Status);
        Assert.Equal(new[] { 75, 75, 0 }, r.Exits);
        Assert.Equal(2, r.Path.Count(s => s == "Backoff"));
    }

    [Fact]
    public void Gives_up_after_three_attempts()
    {
        var r = Run("""{"raceId":"indy-2026"}""", failFirst: "9");
        Assert.Equal(("FAILED", "LapReportFailed"), (r.Status, r.Error));
        Assert.Equal(new[] { 75, 75, 75 }, r.Exits);
    }

    [Fact]
    public void Bad_input_is_not_retried()
    {
        var r = Run("""{"raceId":"monaco-1929"}""");
        Assert.Equal(("FAILED", "LapReportFailed"), (r.Status, r.Error));
        Assert.Equal(new[] { 2 }, r.Exits);
    }

    [Fact]
    public void Missing_race_id_never_starts_a_task()
    {
        var r = Run("{}");
        Assert.Equal(("FAILED", "BadInput"), (r.Status, r.Error));
        Assert.Empty(r.Exits);
    }

    [Fact]
    public void Every_transition_target_exists_and_every_state_is_reachable()
    {
        var def = Definition();
        var states = def["States"]!.AsObject();
        IEnumerable<string> Targets(JsonNode s) =>
            new[] { (string?)s["Next"], (string?)s["Default"] }
                .Concat(s["Choices"]?.AsArray().Select(c => (string?)c!["Next"]) ?? [])
                .Concat(s["Catch"]?.AsArray().Select(c => (string?)c!["Next"]) ?? [])
                .Where(t => t is not null)!;
        foreach (var (name, s) in states)
            foreach (var t in Targets(s!))
                Assert.True(states.ContainsKey(t), $"{name} points at missing state {t}");

        var seen = new HashSet<string>();
        var todo = new Stack<string>([(string)def["StartAt"]!]);
        while (todo.TryPop(out var n))
            if (seen.Add(n)) foreach (var t in Targets(states[n]!)) todo.Push(t);
        Assert.Equal(states.Select(kv => kv.Key).Order(), seen.Order());
    }

    [Fact]
    public void Container_name_matches_the_task_definition()
    {
        var td = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "infra", "task-definition.json")))!;
        var container = (string)td["containerDefinitions"]![0]!["name"]!;
        var overrideName = (string)Definition()["States"]!["RunReport"]!["Parameters"]!["Overrides"]!["ContainerOverrides"]![0]!["Name"]!;
        Assert.Equal(container, overrideName);
    }
}
