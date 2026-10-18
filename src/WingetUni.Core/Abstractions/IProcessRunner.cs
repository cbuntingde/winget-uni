using WingetUni.Core.Models;
using WingetUni.Core.Processes;

namespace WingetUni.Core.Abstractions;

/// <summary>Runs an external command line tool and reports its output.</summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="request"/> to completion.
    /// </summary>
    /// <param name="progress">
    /// Receives output lines as they arrive. Managers stream installer progress, so this is
    /// what keeps a long install from looking frozen. May be null.
    /// </param>
    /// <returns>
    /// A result describing how the process ended. A process that could not be started at all
    /// is reported as a failure result rather than an exception, because a missing package
    /// manager executable is an expected condition on a machine that does not have it.
    /// </returns>
    Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
