namespace LapReport;

/// <summary>
/// The container's exit code is its contract with the orchestrator. Step Functions reads it from
/// the ECS task result and decides: done, retry later, or stop and alert.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int BadInput = 2;          // retrying won't help: fail the execution
    public const int TempFail = 75;         // EX_TEMPFAIL (sysexits.h): try again later
}
