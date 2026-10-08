using System.Text.Json.Nodes;
using LapReport;
using Simulator;

// Walk the state machine through five situations, running the real job for every ECS task.
var definition = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "state-machine.asl.json")))!.AsObject();
var jobLog = new List<string>();
JsonLog.Write = jobLog.Add;

var scenarios = new (string Name, string Input, string? FailFirst)[]
{
    ("clean run", """{"raceId":"indy-2026"}""", null),
    ("dependency down for 2 attempts", """{"raceId":"indy-2026"}""", "2"),
    ("dependency still down", """{"raceId":"indy-2026"}""", "9"),
    ("race with no data", """{"raceId":"monaco-1929"}""", null),
    ("execution started without raceId", "{}", null),
};

foreach (var (name, input, failFirst) in scenarios)
{
    jobLog.Clear();
    var fargate = new FakeFargate(new() { ["FAIL_FIRST_ATTEMPTS"] = failFirst });
    var sm = new AslInterpreter(definition, fargate.Run);
    var (status, error, _) = sm.Execute(JsonNode.Parse(input)!);
    Console.WriteLine($"{name}");
    Console.WriteLine($"  path       {string.Join(" > ", sm.Path)}");
    Console.WriteLine($"  exit codes {(fargate.ExitCodes.Count == 0 ? "(no task started)" : string.Join(", ", fargate.ExitCodes))}");
    Console.WriteLine($"  result     {status}{(error is null ? "" : $" ({error})")}\n");
    if (name == "clean run")
    {
        Console.WriteLine("  job log (JSON lines, as CloudWatch would receive them):");
        foreach (var line in jobLog) Console.WriteLine($"    {line}");
        Console.WriteLine();
    }
}
