using System.Runtime.InteropServices;
using LapReport;

// A batch job, not a service: start, do one unit of work, exit with a meaningful code.
// Configuration arrives as environment variables (Step Functions sets them as container overrides).
using var cts = new CancellationTokenSource();

// Fargate sends SIGTERM, waits stopTimeout (default 30 s), then SIGKILL. Use the window to stop cleanly.
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
{
    ctx.Cancel = true;                       // we handle shutdown ourselves
    JsonLog.Info("SIGTERM received, stopping after the current step");
    cts.Cancel();
});

return await Job.RunAsync(Environment.GetEnvironmentVariable, cts.Token);
