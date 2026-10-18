namespace WingetUni.Core.Models;

/// <summary>The operation to perform on a package.</summary>
public enum PackageOperation
{
    Install,
    Update,
    Uninstall,
}

/// <summary>
/// A message emitted while an operation runs. Managers stream installer progress, and that
/// stream is what the operation log shows to the user.
/// </summary>
/// <param name="Message">One line of output, with surrounding whitespace removed.</param>
/// <param name="IsError">True when the manager wrote the line to its error stream.</param>
public sealed record OperationProgress(string Message, bool IsError = false);

/// <summary>
/// The outcome of an operation. Managers report failures both through the exit code and through
/// their output, so both are kept: <see cref="Message"/> is a readable summary, and
/// <see cref="Output"/> preserves everything the manager said.
/// </summary>
public sealed class OperationResult
{
    private OperationResult(bool success, bool canceled, int exitCode, string message, IReadOnlyList<string> output)
    {
        Success = success;
        IsCanceled = canceled;
        ExitCode = exitCode;
        Message = message;
        Output = output;
    }

    public bool Success { get; }

    /// <summary>
    /// True when the operation stopped because it was cancelled rather than because the manager
    /// rejected it. Kept separate from <see cref="Success"/> so the interface can label a
    /// cancellation differently from a failure.
    /// </summary>
    public bool IsCanceled { get; }

    public int ExitCode { get; }

    /// <summary>Readable summary of what happened, suitable for a notification or a dialog.</summary>
    public string Message { get; }

    /// <summary>Every line the manager produced, in order, for the operation log.</summary>
    public IReadOnlyList<string> Output { get; }

    public static OperationResult Ok(int exitCode, string message, IReadOnlyList<string> output) =>
        new(true, false, exitCode, message, output);

    public static OperationResult Fail(int exitCode, string message, IReadOnlyList<string> output) =>
        new(false, false, exitCode, message, output);

    public static OperationResult Canceled(string message, IReadOnlyList<string> output) =>
        new(false, true, exitCode: -1, message, output);
}
