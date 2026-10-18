using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Core.Processes;

namespace WingetUni.Tests;

/// <summary>
/// An <see cref="IProcessRunner"/> that replays queued results and records the requests it was
/// given, so a provider can be exercised without invoking the real tool.
/// </summary>
/// <remarks>
/// The recorded requests are what makes the argument building testable: a provider's command
/// line is asserted from <see cref="Requests"/> rather than from a real invocation.
/// </remarks>
internal sealed class StubProcessRunner : IProcessRunner
{
    private readonly List<ProcessRunRequest> _requests = [];
    private readonly Queue<Func<ProcessRunRequest, ProcessRunResult>> _responses = new();

    /// <summary>Every request the provider made, in order.</summary>
    public IReadOnlyList<ProcessRunRequest> Requests => _requests;

    public ProcessRunRequest LastRequest => _requests[^1];

    /// <summary>The result handed back for the most recent request.</summary>
    public ProcessRunResult LastResult { get; private set; } = Silent();

    /// <summary>Queues a fixed result for the next call.</summary>
    public StubProcessRunner Respond(ProcessRunResult result) => Respond(_ => result);

    /// <summary>Queues a result computed from the request, for a call whose output depends on its arguments.</summary>
    public StubProcessRunner Respond(Func<ProcessRunRequest, ProcessRunResult> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    /// <summary>Queues a success carrying one line per element.</summary>
    public StubProcessRunner RespondWith(params string[] standardOutput) => Respond(Succeeded(standardOutput));

    public Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Add(request);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"No response was queued for '{request.FileName} {string.Join(' ', request.Arguments)}'.");
        }

        LastResult = _responses.Dequeue()(request);
        return Task.FromResult(LastResult);
    }

    /// <summary>A started process that exited zero, printing the given lines.</summary>
    public static ProcessRunResult Succeeded(params string[] standardOutput) =>
        Build(started: true, exitCode: 0, standardOutput, [], timedOut: false, canceled: false, startFailure: null);

    /// <summary>A started process that exited with <paramref name="exitCode"/>.</summary>
    public static ProcessRunResult Exited(int exitCode, params string[] standardOutput) =>
        Build(started: true, exitCode, standardOutput, [], timedOut: false, canceled: false, startFailure: null);

    /// <summary>A started process that exited zero and printed nothing.</summary>
    public static ProcessRunResult Silent() => Succeeded();

    /// <summary>A process that could not be started, as when the tool is not installed.</summary>
    public static ProcessRunResult CouldNotStart(string failure) =>
        Build(started: false, exitCode: -1, [], [], timedOut: false, canceled: false, startFailure: failure);

    /// <summary>A process that was killed after its timeout.</summary>
    public static ProcessRunResult WasTimedOut(params string[] standardOutput) =>
        Build(started: true, exitCode: -1, standardOutput, [], timedOut: true, canceled: false, startFailure: null);

    /// <summary>A process that was killed because the caller cancelled.</summary>
    public static ProcessRunResult WasCanceled(params string[] standardOutput) =>
        Build(started: true, exitCode: -1, standardOutput, [], timedOut: false, canceled: true, startFailure: null);

    private static ProcessRunResult Build(
        bool started,
        int exitCode,
        IReadOnlyList<string> standardOutput,
        IReadOnlyList<string> standardError,
        bool timedOut,
        bool canceled,
        string? startFailure) => new()
        {
            Started = started,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            Transcript = [.. standardOutput, .. standardError],
            TimedOut = timedOut,
            Canceled = canceled,
            StartFailure = startFailure,
        };
}
