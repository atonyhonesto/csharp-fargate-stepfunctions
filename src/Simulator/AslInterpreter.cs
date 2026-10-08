using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Simulator;

/// <summary>Raised by a task runner the way Step Functions raises States.TaskFailed for a non-zero ECS exit.</summary>
public sealed class TaskFailedException(string cause) : Exception("States.TaskFailed")
{
    public string Cause { get; } = cause;
}

/// <summary>
/// Runs the subset of Amazon States Language this state machine uses (Pass, Task, Choice, Wait, Succeed,
/// Fail, Catch, ResultPath, Parameters with JSONPath and the Format/MathAdd/StringToJson intrinsics),
/// so the workflow's logic can be tested without an AWS account. Retry is AWS's job and is not simulated.
/// </summary>
public sealed partial class AslInterpreter(JsonObject definition, Func<JsonNode, JsonNode> runTask)
{
    public List<string> Path { get; } = [];

    public (string Status, string? Error, JsonNode? Output) Execute(JsonNode input)
    {
        var states = definition["States"]!.AsObject();
        var name = (string)definition["StartAt"]!;
        var data = input.DeepClone();
        for (var transitions = 0; transitions < 100; transitions++)
        {
            Path.Add(name);
            var s = states[name]!.AsObject();
            switch ((string)s["Type"]!)
            {
                case "Pass":
                    var result = s["Parameters"] is { } p ? Resolve(p, data)! : data.DeepClone();
                    data = ApplyResultPath(data, result, (string?)s["ResultPath"]);
                    name = (string)s["Next"]!;
                    break;
                case "Task":
                    try
                    {
                        data = ApplyResultPath(data, runTask(Resolve(s["Parameters"], data)!), (string?)s["ResultPath"]);
                        name = (string)s["Next"]!;
                    }
                    catch (TaskFailedException e)
                    {
                        var c = s["Catch"]?.AsArray().FirstOrDefault(c => c!["ErrorEquals"]!.AsArray()
                            .Any(x => (string)x! is "States.TaskFailed" or "States.ALL"));
                        if (c is null) return ("FAILED", "States.TaskFailed", null);
                        var error = new JsonObject { ["Error"] = "States.TaskFailed", ["Cause"] = e.Cause };
                        data = ApplyResultPath(data, error, (string?)c["ResultPath"]);
                        name = (string)c["Next"]!;
                    }
                    break;
                case "Choice":
                    var match = s["Choices"]!.AsArray().FirstOrDefault(c => Test(c!, data));
                    name = (string)(match is null ? s["Default"] : match["Next"])!;
                    break;
                case "Wait":
                    name = (string)s["Next"]!;                        // time passes instantly here
                    break;
                case "Succeed":
                    return ("SUCCEEDED", null, data);
                case "Fail":
                    return ("FAILED", (string?)s["Error"], null);
                default:
                    throw new NotSupportedException($"state type {s["Type"]}");
            }
        }
        throw new InvalidOperationException("too many transitions: is there a loop without an exit?");
    }

    private static JsonNode ApplyResultPath(JsonNode data, JsonNode result, string? resultPath)
    {
        if (resultPath is null or "$") return result;
        var copy = data.DeepClone().AsObject();
        copy[resultPath["$.".Length..]] = result;                  // single-level paths are all we need
        return copy;
    }

    private static JsonNode? Resolve(JsonNode? template, JsonNode data)
    {
        if (template is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var (key, value) in obj)
            {
                if (key.EndsWith(".$", StringComparison.Ordinal))
                    result[key[..^2]] = Evaluate((string)value!, data);   // "x.$": path or intrinsic
                else
                    result[key] = Resolve(value, data);
            }
            return result;
        }
        if (template is JsonArray arr)
        {
            var result = new JsonArray();
            foreach (var item in arr) result.Add(Resolve(item, data));
            return result;
        }
        return template?.DeepClone();
    }

    private static JsonNode? Evaluate(string expr, JsonNode data)
    {
        var m = Intrinsic().Match(expr);
        if (!m.Success) return Select(data, expr)?.DeepClone();
        var args = new List<JsonNode?>();
        foreach (var raw in SplitArgs(m.Groups[2].Value))
            args.Add(raw.StartsWith('\'') ? JsonValue.Create(raw.Trim('\''))               // 'string literal'
                   : int.TryParse(raw, CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n) // number literal
                   : Select(data, raw));                                                          // $.path

        switch (m.Groups[1].Value)
        {
            case "Format":
            {
                var parts = ((string)args[0]!).Split("{}");
                var text = parts[0];
                for (var i = 1; i < parts.Length; i++)
                    text += args[i]!.ToJsonString().Trim('"') + parts[i];
                return JsonValue.Create(text);
            }
            case "MathAdd":
                return JsonValue.Create((int)args[0]! + (int)args[1]!);
            case "StringToJson":
                return JsonNode.Parse((string)args[0]!);
            default:
                throw new NotSupportedException($"States.{m.Groups[1].Value}");
        }
    }

    private static IEnumerable<string> SplitArgs(string s) => s.Split(',').Select(a => a.Trim());

    /// <summary>JSONPath subset: $.a.b[0].c</summary>
    public static JsonNode? Select(JsonNode? node, string path)
    {
        foreach (Match t in PathToken().Matches(path))
        {
            node = t.Groups[1].Success ? (node as JsonObject)?[t.Groups[1].Value]
                                       : (node as JsonArray) is { } arr && int.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture) < arr.Count
                                           ? arr[int.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture)] : null;
            if (node is null) return null;
        }
        return node;
    }

    private static bool Test(JsonNode rule, JsonNode data)
    {
        if (rule["And"] is JsonArray all) return all.All(r => Test(r!, data));
        var v = Select(data, (string)rule["Variable"]!);
        if (rule["IsPresent"] is { } present) return (v is not null) == (bool)present;
        if (rule["NumericEquals"] is { } eq) return v is JsonValue n && n.TryGetValue<double>(out var x) && x == (double)eq;
        if (rule["NumericLessThan"] is { } lt) return v is JsonValue n2 && n2.TryGetValue<double>(out var y) && y < (double)lt;
        throw new NotSupportedException(rule.ToJsonString());
    }

    [GeneratedRegex(@"^States\.(\w+)\((.*)\)$")]
    private static partial Regex Intrinsic();

    [GeneratedRegex(@"\.(\w+)|\[(\d+)\]")]
    private static partial Regex PathToken();
}
