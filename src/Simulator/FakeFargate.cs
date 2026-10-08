using System.Text.Json.Nodes;
using LapReport;

namespace Simulator;

/// <summary>
/// Stands in for ecs:runTask.sync: takes the Parameters the state machine built, applies the container
/// environment overrides, runs the real job, and reports the exit code the way the ECS integration does.
/// </summary>
public sealed class FakeFargate(Dictionary<string, string?> taskDefinitionEnv)
{
    private readonly string _outputDir = Path.Combine(Path.GetTempPath(), "lap-report-" + Guid.NewGuid().ToString("N"));

    public List<int> ExitCodes { get; } = [];

    public JsonNode Run(JsonNode parameters)
    {
        var env = new Dictionary<string, string?>(taskDefinitionEnv);
        env.TryAdd("OUTPUT_DIR", _outputDir);                       // each fake task gets its own scratch space
        var overrides = parameters["Overrides"]!["ContainerOverrides"]![0]!;
        foreach (var e in overrides["Environment"]!.AsArray())
            env[(string)e!["Name"]!] = (string?)e["Value"];

        var code = Job.RunAsync(k => env.GetValueOrDefault(k), CancellationToken.None).GetAwaiter().GetResult();
        ExitCodes.Add(code);
        var description = new JsonObject
        {
            ["StopCode"] = "EssentialContainerExited",
            ["Containers"] = new JsonArray(new JsonObject { ["Name"] = (string?)overrides["Name"], ["ExitCode"] = code }),
        };
        if (code != 0) throw new TaskFailedException(description.ToJsonString());
        return description;
    }
}
