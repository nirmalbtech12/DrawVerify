using DrawVerify.App.Navigation;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class KeyboardNavigationTests
{
    [Fact(DisplayName = "WPF-002 native menu headers declare File and Upload access keys")]
    public void MenuAccessKeysAreDeclared()
    {
        Assert.Contains("_File", ScreenRegistry.Menus.Select(x => x.Header));
        Assert.Contains("_Upload", ScreenRegistry.Menus.Select(x => x.Header));
        Assert.Contains("_Dashboard / Home", ScreenRegistry.Menus[0].Children!.Select(x => x.Header));
    }

    [Fact(DisplayName = "WPF-002 Back and Alt+Left share bounded history semantics")]
    public void HistoryReturnsValidEntriesAndStopsAtRoot()
    {
        var history = new NavigationHistory();
        history.Push(ScreenRegistry.Dashboard);
        history.Push(ScreenRegistry.Verification);
        Assert.Equal(ScreenRegistry.Verification, history.TryPop());
        Assert.Equal(ScreenRegistry.Dashboard, history.TryPop());
        Assert.Null(history.TryPop());
    }
}
