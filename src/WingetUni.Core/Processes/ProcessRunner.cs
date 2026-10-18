using System.Diagnostics;
using System.Text;
using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;

namespace WingetUni.Core.Processes;

/// <summary>
/// Runs command line package managers and captures their output.
/// </summary>
/// <remarks>
/// Two details matter for wrapping these tools correctly:
/// <list type="bullet">
///   <item>Output is decoded as UTF-8. Winget writes UTF-8 to a redirected stream, so no
///   code page guessing is involved.</item>
///   <item>Lines are split on carriage return as well as line feed. Managers draw progress
///   with a bare carriage return, and treating those as line breaks is what turns a spinner
///   into readable progress lines.</item>
/// </list>
/// </remarks>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>UTF-8 without a byte order mark, matching what the managers emit.</summary>
    private static readonly Encoding OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>How long to wait for output to drain after a process is killed.</summary>
    private static readonly TimeSpan KillGracePeriod = TimeSpan.FromSeconds(10);

    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory ?? Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = OutputEncoding,
            StandardErrorEncoding = OutputEncoding,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return ProcessRunResult.NotStarted($"'{request.FileName}' did not start.");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ProcessRunResult.NotStarted($"'{request.FileName}' could not be started: {exception.Message}");
        }

        // Managers prompt on standard input when they think a user is watching. Nothing can
        // answer that prompt here, so the stream is closed to make them take the silent path.
        process.StandardInput.Close();

        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken);

        var standardOutput = new List<string>();
        var standardError = new List<string>();
        var transcript = new List<string>();

        var outputTask = ReadStreamAsync(process.StandardOutput, standardOutput, transcript, progress, linkedSource.Token);
        var errorTask = ReadStreamAsync(process.StandardError, standardError, transcript, progress, linkedSource.Token);

        var canceled = false;
        var timedOut = false;
        var exitCode = -1;

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            canceled = cancellationToken.IsCancellationRequested;
            timedOut = !canceled && timeoutSource.IsCancellationRequested;
            await KillAsync(process).ConfigureAwait(false);
        }

        await AwaitQuietlyAsync(outputTask).ConfigureAwait(false);
        await AwaitQuietlyAsync(errorTask).ConfigureAwait(false);

        if (process.HasExited)
        {
            exitCode = process.ExitCode;
        }

        return new ProcessRunResult
        {
            Started = true,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            Transcript = transcript,
            TimedOut = timedOut,
            Canceled = canceled,
        };
    }

    /// <summary>
    /// Reads one stream into <paramref name="lines"/>, reporting each line as it completes.
    /// </summary>
    private static async Task ReadStreamAsync(
        StreamReader reader,
        List<string> lines,
        List<string> transcript,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        var pending = new StringBuilder();

        void Flush()
        {
            var line = pending.ToString().TrimEnd();
            pending.Clear();

            if (line.Length == 0)
            {
                return;
            }

            lock (transcript)
            {
                lines.Add(line);
                transcript.Add(line);
            }

            progress?.Report(new OperationProgress(line));
        }

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                var character = buffer[i];
                if (character is '\r' or '\n')
                {
                    Flush();
                }
                else
                {
                    pending.Append(character);
                }
            }
        }

        Flush();
    }

    private static async Task KillAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // The process exited between the check and the kill; there is nothing left to do.
        }

        try
        {
            using var grace = new CancellationTokenSource(KillGracePeriod);
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The kill did not land within the grace period. The result still reports the
            // operation as cancelled or timed out, so the caller is told what happened.
        }
    }

    private static async Task AwaitQuietlyAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the operation was cancelled; any lines read so far are kept.
        }
    }
}
