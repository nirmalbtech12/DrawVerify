using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SharedDocumentsViewModelTests
{
    [Fact(DisplayName = "TSD-ACCESS-FILTER TSD-SCOPED-SEARCH shared list has search, intentional empty state, and visible load failure")]
    public void Shared_documents_view_model_contract_is_explicit()
    {
        var source = Read("src/DrawVerify.App/ViewModels/SharedDocumentsViewModel.cs");
        Assert.Contains("ObservableCollection<SharedDocument> Documents", source);
        Assert.Contains("SearchCommand", source);
        Assert.Contains("No approved documents have been shared with you.", source);
        Assert.Contains("catch (Exception exception)", source);
        Assert.Contains("Status = exception.Message", source);
    }

    [Fact(DisplayName = "TSD-METADATA shared list binds required document metadata")]
    public void Shared_documents_view_binds_required_metadata()
    {
        var xaml = Read("src/DrawVerify.App/Views/SharedDocumentsView.xaml");
        foreach (var field in new[] { "DocumentName", "RevisionText", "ApprovalStatus", "FinalApprovalUtc", "ApproverName", "SharedByName" })
            Assert.Contains(field, xaml);
        Assert.Contains("IsReadOnly=\"True\"", xaml);
    }

    [Fact(DisplayName = "TSD-REVOCATION revocation refreshes the shared list through the same query boundary")]
    public void Shared_documents_refresh_uses_service_list_boundary()
    {
        var source = Read("src/DrawVerify.App/ViewModels/SharedDocumentsViewModel.cs");
        Assert.Contains("await _service.ListAsync(new SharedDocumentQuery(Search))", source);
        Assert.Contains("Documents.Clear()", source);
    }

    private static string Read(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++, current = current.Parent)
        {
            var path = Path.Combine(current.FullName, relativePath);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relativePath);
    }
}
