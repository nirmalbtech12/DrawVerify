using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DrawVerify.App.ViewModels;

public abstract class TaskScreenViewModel : INotifyPropertyChanged
{
    private bool _isDirty;
    public bool IsDirty { get => _isDirty; protected set { if (_isDirty == value) return; _isDirty = value; PropertyChanged?.Invoke(this, new(nameof(IsDirty))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void MarkDirty() => IsDirty = true;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
