using System.Collections.Concurrent;
using WingetUni.Core.Models;

namespace WingetUni.Core.Services;

/// <summary>
/// Runs package operations one at a time and reports their progress.
/// </summary>
/// <remarks>
/// Operations are serialized deliberately. Package managers refuse to install two things at
/// once, and an installer that reboots or locks files would break a concurrent run. Updates and
/// removals are queued the same way, so a bulk update behaves predictably.
/// </remarks>
public sealed class PackageOperationQueue : IDisposable
{
    /// <summary>How often the growing log is republished while an operation runs.</summary>
    internal static readonly TimeSpan LogPublishInterval = TimeSpan.FromMilliseconds(150);

    private readonly ConcurrentQueue<PackageOperationRequest> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<OperationRecord> _history = [];
    private readonly object _gate = new();

    private Task? _worker;
    private CancellationTokenSource? _current;
    private int _outstanding;
    private bool _disposed;

    /// <summary>Raised whenever an operation is added or its state changes.</summary>
    public event EventHandler<OperationRecord>? RecordChanged;

    /// <summary>Raised whenever the queue starts or stops being busy.</summary>
    public event EventHandler? QueueChanged;

    /// <summary>True while at least one operation is pending or running.</summary>
    public bool IsRunning => Volatile.Read(ref _outstanding) > 0;

    /// <summary>Whether the operation currently running has been asked to stop.</summary>
    public bool IsCancellationRequested => _current?.IsCancellationRequested ?? false;

    /// <summary>Every operation this session, newest last.</summary>
    public IReadOnlyList<OperationRecord> History
    {
        get
        {
            lock (_gate)
            {
                return [.. _history];
            }
        }
    }

    /// <summary>Queues operations and starts the worker if it is not already running.</summary>
    public void Enqueue(IEnumerable<PackageOperationRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var added = 0;
        foreach (var request in requests)
        {
            _pending.Enqueue(request);
            added++;
        }

        if (added == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_worker is null or { IsCompleted: true })
            {
                _worker = Task.Run(ProcessAsync);
            }
        }

        Interlocked.Add(ref _outstanding, added);
        _signal.Release(added);
        RaiseQueueChanged();
    }

    /// <summary>Asks the running operation to stop. Queued operations are not affected.</summary>
    public void CancelCurrent() => _current?.Cancel();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current?.Cancel();
        _shutdown.Cancel();
        _signal.Release();

        try
        {
            // Longer than ProcessRunner's 10-second kill grace: a cancelled operation
            // needs that long to drain its output before the worker can finish, and
            // stopping the wait early would dispose the queue under a running worker.
            _worker?.Wait(TimeSpan.FromSeconds(15));
        }
        catch (AggregateException)
        {
            // The worker was cancelled as part of shutting down; there is nothing to report.
        }

        _shutdown.Dispose();
        _signal.Dispose();
    }

    private async Task ProcessAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_pending.TryDequeue(out var request))
            {
                continue;
            }

            try
            {
                await ExecuteAsync(request).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _outstanding);
                RaiseQueueChanged();
            }
        }
    }

    private async Task ExecuteAsync(PackageOperationRequest request)
    {
        var record = CreateRecord(request);
        Remember(record);
        Publish(record);

        using var cancellation = new CancellationTokenSource();
        _current = cancellation;

        record = record with { State = OperationState.Running, UpdatedAt = DateTimeOffset.Now };
        Remember(record);
        Publish(record);

        var log = new OperationLog();

        // Installers report progress far faster than a window can usefully redraw, so the log is
        // republished on an interval for as long as the operation runs.
        using var stopPublisher = new CancellationTokenSource();
        var publisher = PublishLogAsync(record, log, stopPublisher.Token);

        try
        {
            var result = await request.Provider
                .ExecuteAsync(request.Operation, request.Package, request.Options, log, cancellation.Token)
                .ConfigureAwait(false);

            var state = result.Success
                ? OperationState.Succeeded
                : result.IsCanceled
                    ? OperationState.Canceled
                    : OperationState.Failed;

            record = record with
            {
                State = state,
                Message = result.Message,
                Log = log.Snapshot(),
                UpdatedAt = DateTimeOffset.Now,
            };
        }
        catch (OperationCanceledException)
        {
            record = record with
            {
                State = OperationState.Canceled,
                Message = $"{request.Title} was cancelled.",
                Log = log.Snapshot(),
                UpdatedAt = DateTimeOffset.Now,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A provider reports failures through its result, so reaching here means a defect.
            // The queue records it and moves on instead of abandoning the remaining operations.
            record = record with
            {
                State = OperationState.Failed,
                Message = $"{request.Title} failed unexpectedly: {exception.Message}",
                Log = log.Snapshot(),
                UpdatedAt = DateTimeOffset.Now,
            };
        }
        finally
        {
            _current = null;
            stopPublisher.Cancel();
            await publisher.ConfigureAwait(false);
        }

        Remember(record);
        Publish(record);
    }

    /// <summary>Republishes the running operation's log so the interface can follow along.</summary>
    private async Task PublishLogAsync(OperationRecord running, OperationLog log, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(LogPublishInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (log.IsPublishDue())
            {
                Publish(running with { Log = log.Snapshot(), UpdatedAt = DateTimeOffset.Now });
            }
        }
    }

    private OperationRecord CreateRecord(PackageOperationRequest request) => new()
    {
        Id = Guid.NewGuid(),
        ManagerId = request.Provider.Id,
        ManagerDisplayName = request.Provider.DisplayName,
        Operation = request.Operation,
        Package = request.Package,
        State = OperationState.Pending,
        UpdatedAt = DateTimeOffset.Now,
        Log = [],
    };

    private void Remember(OperationRecord record)
    {
        lock (_gate)
        {
            var index = _history.FindIndex(item => item.Id == record.Id);
            if (index < 0)
            {
                _history.Add(record);
            }
            else
            {
                _history[index] = record;
            }
        }
    }

    private void Publish(OperationRecord record) => RecordChanged?.Invoke(this, record);

    private void RaiseQueueChanged() => QueueChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Collects an operation's output and decides when to publish it.
    /// </summary>
    /// <remarks>
    /// A manager writes to its output and error streams from two threads, so the log is guarded
    /// against concurrent appends. It is republished on an interval rather than per line
    /// because installers emit progress many times a second, which would flood the interface.
    /// </remarks>
    internal sealed class OperationLog : IProgress<OperationProgress>
    {
        private readonly List<string> _lines = [];
        private readonly object _gate = new();
        private long _lastPublish;

        public void Report(OperationProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);

            lock (_gate)
            {
                _lines.Add(value.IsError ? $"[error] {value.Message}" : value.Message);
            }
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }

        /// <summary>True at most once per <see cref="LogPublishInterval"/> while output arrives.</summary>
        public bool IsPublishDue()
        {
            var now = Environment.TickCount64;

            lock (_gate)
            {
                if (now - _lastPublish < LogPublishInterval.TotalMilliseconds)
                {
                    return false;
                }

                _lastPublish = now;
                return true;
            }
        }
    }
}
