using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Core.Processes;

namespace WingetUni.Providers;

/// <summary>
/// Shared behaviour for providers that drive a command line tool: probing for the tool,
/// applying timeouts, and turning a finished process into an <see cref="OperationResult"/>.
/// </summary>
/// <remarks>
/// Providers differ in their commands and their output, so only the parts that are genuinely
/// the same live here. Parsing and argument building stay in each provider.
/// </remarks>
public abstract class CliPackageManagerProvider : IPackageManagerProvider
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The executable's full path once it has been found, otherwise null.</summary>
    private string? _resolvedExecutable;

    protected CliPackageManagerProvider(IProcessRunner runner)
    {
        Runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    protected IProcessRunner Runner { get; }

    /// <summary>
    /// <see cref="ExecutableName"/>, resolved through the PATH so that a tool shipped as a
    /// batch shim can find its own files. See <see cref="ExecutableLocator"/>.
    /// </summary>
    private string ExecutablePath
    {
        get
        {
            if (_resolvedExecutable is not null)
            {
                return _resolvedExecutable;
            }

            var resolved = ExecutableLocator.Resolve(ExecutableName);

            // Only a real path is remembered. A tool that is not installed yet is looked for
            // again on the next probe, which is what lets a fresh install be picked up.
            if (!string.Equals(resolved, ExecutableName, StringComparison.Ordinal))
            {
                _resolvedExecutable = resolved;
            }

            return resolved;
        }
    }

    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    /// <summary>Executable name, resolved through the PATH. One element per argument.</summary>
    protected abstract string ExecutableName { get; }

    /// <summary>Arguments that make the tool print its version.</summary>
    protected virtual IReadOnlyList<string> VersionArguments => ["--version"];

    /// <summary>How long a read-only command such as a search may take.</summary>
    protected virtual TimeSpan ReadTimeout => TimeSpan.FromMinutes(5);

    /// <summary>How long an install, update, or removal may take.</summary>
    protected virtual TimeSpan WriteTimeout => TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<Package>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<Package>> ListInstalledAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<Package>> ListUpgradableAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<PackageDetails?> GetDetailsAsync(
        Package package,
        CancellationToken cancellationToken = default);

    public bool IsAvailable { get; private set; }

    public string? Version { get; private set; }

    public string? UnavailableReason { get; private set; }

    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        var request = new ProcessRunRequest
        {
            FileName = ExecutablePath,
            Arguments = VersionArguments,
            Timeout = ProbeTimeout,
        };

        var result = await Runner.RunAsync(request, null, cancellationToken).ConfigureAwait(false);

        if (!result.Started)
        {
            MarkUnavailable(result.StartFailure ?? $"'{ExecutableName}' could not be started.");
            return;
        }

        if (result.ExitCode != 0)
        {
            MarkUnavailable(
                LastMeaningfulLine(result) ?? $"'{ExecutableName}' exited with code {result.ExitCode}.");
            return;
        }

        IsAvailable = true;
        UnavailableReason = null;
        Version = ExtractVersion(result);
    }

    /// <summary>Runs a read-only command, raising <see cref="PackageManagerException"/> when it cannot answer.</summary>
    protected async Task<ProcessRunResult> RunReadAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await Runner
            .RunAsync(Request(arguments, ReadTimeout), null, cancellationToken)
            .ConfigureAwait(false);

        EnsureQuerySucceeded(result, arguments);
        return result;
    }

    /// <summary>Runs an operation, streaming its output to <paramref name="progress"/>.</summary>
    protected async Task<ProcessRunResult> RunWriteAsync(
        IReadOnlyList<string> arguments,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return await Runner
            .RunAsync(Request(arguments, WriteTimeout), progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult> ExecuteAsync(
        PackageOperation operation,
        Package package,
        InstallOptions options,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);

        var arguments = BuildArguments(operation, package, options);
        var result = await RunWriteAsync(arguments, progress, cancellationToken).ConfigureAwait(false);

        return Interpret(operation, package, result);
    }

    /// <summary>Builds the command line for one operation.</summary>
    protected abstract IReadOnlyList<string> BuildArguments(
        PackageOperation operation,
        Package package,
        InstallOptions options);

    /// <summary>
    /// Turns a finished process into a result. Overridden by providers whose tool reports
    /// failures in a form that needs translating, such as Winget's exit codes.
    /// </summary>
    protected virtual OperationResult Interpret(
        PackageOperation operation,
        Package package,
        ProcessRunResult result)
    {
        if (!result.Started)
        {
            return OperationResult.Fail(
                result.ExitCode,
                result.StartFailure ?? $"{DisplayName} could not be started.",
                result.Transcript);
        }

        if (result.Canceled)
        {
            return OperationResult.Canceled(
                $"{OngoingName(operation)} {package.Name} was cancelled.",
                result.Transcript);
        }

        if (result.TimedOut)
        {
            return OperationResult.Fail(
                result.ExitCode,
                $"{OngoingName(operation)} {package.Name} did not finish within the time limit.",
                result.Transcript);
        }

        if (result.ExitCode == 0)
        {
            return OperationResult.Ok(0, $"{OutcomeName(operation)} {package.Name}.", result.Transcript);
        }

        return OperationResult.Fail(
            result.ExitCode,
            DescribeFailure(operation, package, result),
            result.Transcript);
    }

    /// <summary>
    /// Explains a non-zero exit code. The default is the tool's own last line of output, which
    /// is more useful than an exit code on its own.
    /// </summary>
    protected virtual string DescribeFailure(
        PackageOperation operation,
        Package package,
        ProcessRunResult result)
    {
        var detail = LastMeaningfulLine(result);
        return detail is null
            ? $"{OngoingName(operation)} {package.Name} failed with exit code {result.ExitCode}."
            : $"{OngoingName(operation)} {package.Name} failed: {detail}";
    }

    /// <summary>
    /// True when an exit code means "the command worked but had something to report" rather than
    /// "something went wrong". The arguments are supplied because that meaning is per command:
    /// npm exits 1 when its update check finds updates, but a non-zero code from any of its other
    /// commands is a real failure that should be shown.
    /// </summary>
    protected virtual bool IsEmptyResult(IReadOnlyList<string> arguments, int exitCode) => false;

    protected static string OutcomeName(PackageOperation operation) => operation switch
    {
        PackageOperation.Install => "Installed",
        PackageOperation.Update => "Updated",
        PackageOperation.Uninstall => "Removed",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
    };

    protected static string OngoingName(PackageOperation operation) => operation switch
    {
        PackageOperation.Install => "Installing",
        PackageOperation.Update => "Updating",
        PackageOperation.Uninstall => "Removing",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
    };

    /// <summary>Returns the last output line that carries text, from either stream.</summary>
    protected static string? LastMeaningfulLine(ProcessRunResult result)
    {
        for (var index = result.Transcript.Count - 1; index >= 0; index--)
        {
            var line = result.Transcript[index].Trim();
            if (line.Length > 0)
            {
                return line;
            }
        }

        return null;
    }

    /// <summary>Returns the first line the tool printed, used to explain why a probe failed.</summary>
    protected virtual string? ExtractVersion(ProcessRunResult result)
    {
        var line = result.StandardOutput.FirstOrDefault(item => item.Trim().Length > 0);
        return line?.Trim();
    }

    private void EnsureQuerySucceeded(ProcessRunResult result, IReadOnlyList<string> arguments)
    {
        if (!result.Started)
        {
            throw new PackageManagerException(result.StartFailure ?? $"{DisplayName} could not be started.");
        }

        if (result.Canceled)
        {
            throw new PackageManagerException($"The {DisplayName} query was cancelled.");
        }

        if (result.TimedOut)
        {
            throw new PackageManagerException($"{DisplayName} did not respond within the time limit.");
        }

        if (result.ExitCode == 0 || IsEmptyResult(arguments, result.ExitCode))
        {
            return;
        }

        throw new PackageManagerException(DescribeQueryFailure(result));
    }

    /// <summary>Explains why a read-only command could not answer.</summary>
    protected virtual string DescribeQueryFailure(ProcessRunResult result)
    {
        var detail = LastMeaningfulLine(result);
        return detail is null
            ? $"{DisplayName} exited with code {result.ExitCode}."
            : $"{DisplayName}: {detail}";
    }

    private void MarkUnavailable(string reason)
    {
        IsAvailable = false;
        Version = null;
        UnavailableReason = reason;
    }

    private ProcessRunRequest Request(IReadOnlyList<string> arguments, TimeSpan timeout) => new()
    {
        FileName = ExecutablePath,
        Arguments = arguments,
        Timeout = timeout,
    };
}
