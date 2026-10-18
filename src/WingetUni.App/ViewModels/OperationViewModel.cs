using WingetUni.App.Infrastructure;
using WingetUni.Core.Services;

namespace WingetUni.App.ViewModels;

/// <summary>One entry in the operation queue panel.</summary>
public sealed class OperationViewModel : ObservableObject
{
    private OperationRecord _record;

    public OperationViewModel(OperationRecord record)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
    }

    public Guid Id => _record.Id;

    public string Title => _record.Title;

    public string ManagerName => _record.ManagerDisplayName;

    public OperationState State => _record.State;

    public bool IsFinished => _record.IsFinished;

    public bool IsRunning => _record.State == OperationState.Running;

    public string? Message => _record.Message;

    public string LogText => _record.Log.Count == 0 ? string.Empty : string.Join(Environment.NewLine, _record.Log);

    public string StateText => _record.State switch
    {
        OperationState.Pending => "Waiting",
        OperationState.Running => "Running",
        OperationState.Succeeded => "Done",
        OperationState.Failed => "Failed",
        OperationState.Canceled => "Cancelled",
        _ => "Unknown",
    };

    public void Update(OperationRecord record)
    {
        _record = record;

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(LogText));
    }
}
