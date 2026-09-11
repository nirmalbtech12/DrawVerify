namespace DrawVerify.App.Navigation;

public sealed class NavigationHistory
{
    private readonly Stack<string> _entries = new();
    public bool CanGoBack => _entries.Count > 0;
    public void Push(string target) => _entries.Push(target);
    public string? TryPop() => _entries.Count == 0 ? null : _entries.Pop();
    public void Clear() => _entries.Clear();
}
