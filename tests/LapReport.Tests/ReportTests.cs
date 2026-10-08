using LapReport;

namespace LapReport.Tests;

public class ReportTests
{
    private static readonly string Csv = Path.Combine(AppContext.BaseDirectory, "data", "indy-2026.csv");

    [Fact]
    public void Summarises_the_sample_race()
    {
        var summary = Report.Summarise("indy-2026", Report.Parse(File.ReadLines(Csv)));
        Assert.Equal(120, summary.Laps);
        Assert.Equal(4, summary.Cars.Count);
        Assert.Equal(summary.Cars[0].Car, summary.FastestCar);
        Assert.True(summary.Cars.Zip(summary.Cars.Skip(1)).All(p => p.First.BestLapMs <= p.Second.BestLapMs));
        Assert.Equal(1, summary.Cars.Single(c => c.Car == "24").PitLaps);     // the 21 s in-lap
        Assert.Equal(0, summary.Cars.Single(c => c.Car == "5").PitLaps);
    }

    [Theory]
    [InlineData("car,lap,lap_ms\n24,one,41000")]
    [InlineData("car,lap,lap_ms\n24,1")]
    [InlineData("car,lap,lap_ms\n24,1,-5")]
    [InlineData("car,lap,lap_ms\n")]
    public void Rejects_bad_files(string text) =>
        Assert.Throws<FormatException>(() => Report.Parse(text.Split('\n')));

    [Fact]
    public void Formats_lap_times() => Assert.Equal("1:23.456", Report.Format(83_456));
}

public class JobTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        k => vars.FirstOrDefault(v => v.Key == k).Value;

    [Fact]
    public async Task Missing_race_id_is_bad_input() =>
        Assert.Equal(ExitCodes.BadInput, await Job.RunAsync(Env(), CancellationToken.None));

    [Fact]
    public async Task Unknown_race_is_bad_input() =>
        Assert.Equal(ExitCodes.BadInput, await Job.RunAsync(Env(("RACE_ID", "nope")), CancellationToken.None));

    [Fact]
    public async Task Flaky_dependency_is_a_temporary_failure()
    {
        var env = Env(("RACE_ID", "indy-2026"), ("ATTEMPT", "1"), ("FAIL_FIRST_ATTEMPTS", "1"));
        Assert.Equal(ExitCodes.TempFail, await Job.RunAsync(env, CancellationToken.None));
    }

    [Fact]
    public async Task Success_writes_the_summary()
    {
        var outDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var code = await Job.RunAsync(Env(("RACE_ID", "indy-2026"), ("OUTPUT_DIR", outDir)), CancellationToken.None);
        Assert.Equal(ExitCodes.Success, code);
        Assert.True(File.Exists(Path.Combine(outDir, "indy-2026-summary.json")));
    }

    [Fact]
    public async Task Sigterm_mid_job_exits_for_retry()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var env = Env(("RACE_ID", "indy-2026"), ("WORK_DELAY_MS", "5000"));
        Assert.Equal(ExitCodes.TempFail, await Job.RunAsync(env, cts.Token));
    }
}
