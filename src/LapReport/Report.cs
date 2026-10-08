using System.Globalization;

namespace LapReport;

public sealed record Lap(string Car, int Number, int LapMs);

public sealed record CarSummary(string Car, int Laps, int BestLapMs, int BestLapNumber, double MedianLapMs, int PitLaps);

public sealed record RaceSummary(string RaceId, int Laps, IReadOnlyList<CarSummary> Cars, string FastestCar);

public static class Report
{
    /// <summary>Parse car,lap,lap_ms. Malformed rows are input errors, not something to retry.</summary>
    public static List<Lap> Parse(IEnumerable<string> lines)
    {
        var laps = new List<Lap>();
        var n = 0;
        foreach (var line in lines)
        {
            n++;
            if (n == 1 || string.IsNullOrWhiteSpace(line)) continue;        // header
            var p = line.Split(',');
            if (p.Length != 3 || !int.TryParse(p[1], CultureInfo.InvariantCulture, out var lap)
                              || !int.TryParse(p[2], CultureInfo.InvariantCulture, out var ms) || ms <= 0)
                throw new FormatException($"line {n}: expected car,lap,lap_ms but got '{line}'");
            laps.Add(new Lap(p[0].Trim(), lap, ms));
        }
        if (laps.Count == 0) throw new FormatException("no laps in file");
        return laps;
    }

    /// <summary>A lap more than 15% slower than the car's median is treated as a pit or caution lap.</summary>
    public static RaceSummary Summarise(string raceId, IReadOnlyList<Lap> laps)
    {
        var cars = laps.GroupBy(l => l.Car).Select(g =>
        {
            var sorted = g.Select(l => l.LapMs).Order().ToArray();
            var median = sorted.Length % 2 == 1 ? sorted[sorted.Length / 2]
                                                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0;
            var best = g.MinBy(l => l.LapMs)!;
            return new CarSummary(g.Key, g.Count(), best.LapMs, best.Number, median, g.Count(l => l.LapMs > median * 1.15));
        }).OrderBy(c => c.BestLapMs).ToList();
        return new RaceSummary(raceId, laps.Count, cars, cars[0].Car);
    }

    public static string Format(int ms) => $"{ms / 60000}:{ms / 1000 % 60:00}.{ms % 1000:000}";
}
