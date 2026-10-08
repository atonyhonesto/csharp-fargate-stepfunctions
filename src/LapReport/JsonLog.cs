using System.Text.Encodings.Web;
using System.Text.Json;

namespace LapReport;

/// <summary>One JSON object per line on stdout: the awslogs driver ships it to CloudWatch, Logs Insights can query it.</summary>
public static class JsonLog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,      // keep '+' in timestamps readable
    };

    public static Action<string> Write { get; set; } = Console.WriteLine;

    public static void Info(string message, object? data = null) => Emit("info", message, data);
    public static void Error(string message, object? data = null) => Emit("error", message, data);

    private static void Emit(string level, string message, object? data) =>
        Write(JsonSerializer.Serialize(new { ts = DateTimeOffset.UtcNow.ToString("O"), level, message, data }, Options));
}
