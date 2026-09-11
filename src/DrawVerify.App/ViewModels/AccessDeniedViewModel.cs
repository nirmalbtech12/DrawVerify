namespace DrawVerify.App.ViewModels;
public sealed class AccessDeniedViewModel(string message = "You do not have permission to access this screen.") { public string Message { get; } = message; }
