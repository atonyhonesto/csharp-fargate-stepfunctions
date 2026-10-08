using System.Text.Json;

namespace LapReport;

public static class Job
{
    public static async Task<int> RunAsync(Func<string, string?> env, CancellationToken ct)
    {
        var raceId = env("RACE_ID");
        var attempt = int.TryParse(env("ATTEMPT"), out var a) ? a : 1;
        var dataDir = env("DATA_DIR") ?? Path.Combine(AppContext.BaseDirectory, "data");
        var outDir = env("OUTPUT_DIR") ?? Path.Combine(Path.GetTempPath(), "lap-report");
        JsonLog.Info("job started", new { raceId, attempt, taskArn = env("ECS_CONTAINER_METADATA_URI_V4") is null ? "local" : "fargate" });

        if (string.IsNullOrWhiteSpace(raceId))
        {
            JsonLog.Error("RACE_ID is required");
            return ExitCodes.BadInput;
        }

        // Stand-in for a flaky dependency (an upstream API, a locked table): fails the first attempts.
        if (int.TryParse(env("FAIL_FIRST_ATTEMPTS"), out var failFirst) && attempt <= failFirst)
        {
            JsonLog.Error("timing service unavailable", new { attempt });
            return ExitCodes.TempFail;
        }

        var file = Path.Combine(dataDir, $"{raceId}.csv");
        if (!File.Exists(file))
        {
            JsonLog.Error("no data for race", new { file });
            return ExitCodes.BadInput;
        }

        try
        {
            var laps = Report.Parse(await File.ReadAllLinesAsync(file, ct));
            if (int.TryParse(env("WORK_DELAY_MS"), out var delay)) await Task.Delay(delay, ct);   // simulate a long job
            var summary = Report.Summarise(raceId, laps);
            Directory.CreateDirectory(outDir);
            var outFile = Path.Combine(outDir, $"{raceId}-summary.json");
            await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), ct);
            foreach (var c in summary.Cars)
                JsonLog.Info("car", new { c.Car, best = Report.Format(c.BestLapMs), c.BestLapNumber, c.PitLaps });
            JsonLog.Info("job finished", new { outFile, summary.FastestCar, summary.Laps });
            return ExitCodes.Success;
        }
        catch (FormatException e)
        {
            JsonLog.Error("bad input file", new { e.Message });
            return ExitCodes.BadInput;
        }
        catch (OperationCanceledException)
        {
            JsonLog.Info("stopped before finishing; safe to run again");
            return ExitCodes.TempFail;
        }
    }
}
