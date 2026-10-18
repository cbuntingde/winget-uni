using WingetUni.Core.Models;

namespace WingetUni.Core.Processes;

/// <summary>Describes one external command to run.</summary>
public sealed class ProcessRunRequest
{
    /// <summary>Executable name or full path. A bare name is resolved through the PATH.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Arguments, one element per argument. Passing them pre-split avoids the quoting bugs
    /// that come from building a single command line string: a package id or install path
    /// containing a space or a quote is escaped by the operating system, not by hand.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    /// <summary>How long to let the process run before it is killed. Installers can be slow, so this is generous.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);

    public static ProcessRunRequest Create(string fileName, params string[] arguments) =>
        new() { FileName = fileName, Arguments = arguments };
}

/// <summary>How a <see cref="ProcessRunRequest"/> ended.</summary>
public sealed class ProcessRunResult
{
    /// <summary>False when the executable could not be started, for example because it is not installed.</summary>
    public required bool Started { get; init; }

    public required int ExitCode { get; init; }

    /// <summary>Standard output split into lines, with carriage-return progress updates separated out.</summary>
    public required IReadOnlyList<string> StandardOutput { get; init; }

    public required IReadOnlyList<string> StandardError { get; init; }

    /// <summary>Both streams in the order the process produced them, for an operation log.</summary>
    public required IReadOnlyList<string> Transcript { get; init; }

    /// <summary>True when the process exceeded <see cref="ProcessRunRequest.Timeout"/> and was killed.</summary>
    public required bool TimedOut { get; init; }

    /// <summary>True when the caller cancelled the operation and the process was killed.</summary>
    public required bool Canceled { get; init; }

    /// <summary>Why the process could not be started, when <see cref="Started"/> is false.</summary>
    public string? StartFailure { get; init; }

    public bool Succeeded => Started && !Canceled && !TimedOut && ExitCode == 0;

    public static ProcessRunResult NotStarted(string failure) => new()
    {
        Started = false,
        ExitCode = -1,
        StandardOutput = [],
        StandardError = [],
        Transcript = [],
        TimedOut = false,
        Canceled = false,
        StartFailure = failure,
    };
}
