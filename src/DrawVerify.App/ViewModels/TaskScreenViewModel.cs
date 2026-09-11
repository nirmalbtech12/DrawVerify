using System.ComponentModel;

namespace DrawVerify.App.ViewModels;

public abstract class TaskScreenViewModel : INotifyPropertyChanged
{
    private bool _isDirty;
    public bool IsDirty { get => _isDirty; protected set { if (_isDirty == value) return; _isDirty = value; PropertyChanged?.Invoke(this, new(nameof(IsDirty))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void RaisePropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected void MarkDirty() => IsDirty = true;
    public void MarkDirtyFromUserInput() => MarkDirty();
}
