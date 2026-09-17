using DrawVerify.App.ViewModels;
using DrawVerify.App.Views;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using System.Windows.Controls;
using System.Windows.Data;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class ReportViewModelTests
{
    [Fact(DisplayName = "TD-08 report model surfaces verification fields for external review")]
    public void Report_model_contains_result_values_timestamp_and_verifier()
    {
        var fingerprint = new DocumentFingerprint { Value = new string('e', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };
        var report = new VerificationReport
        {
            FingerprintVerification = new FingerprintVerificationResult(FingerprintVerificationStatus.Match, fingerprint, "Fingerprint Verified - Document Unchanged")
            {
                Stored = fingerprint, VerifiedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), VerifierDisplayName = "Checker One", Revision = 2
            }
        };
        var content = report.ToMarkdown();
        Assert.Contains("Match", content);
        Assert.Contains(fingerprint.Value, content);
        Assert.Contains("Checker One", content);
        Assert.Contains("2026-01-01", content);
    }
    [Fact(DisplayName = "TD-09 report content includes the persisted fingerprint")]
    public void Report_model_renders_fingerprint_for_external_review()
    {
        var fingerprint = new DocumentFingerprint { Value = new string('d', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };
        var content = new VerificationReport { Fingerprint = fingerprint }.ToMarkdown();

        Assert.Contains(fingerprint.Value, content);
        Assert.Contains(fingerprint.Algorithm, content);
    }
    [Fact(DisplayName = "TD-APPUI-EXTRA-002 report ViewModel exposes extra CSV export")]
    public async Task Extra_csv_command_surfaces_extra_rows_and_status()
    {
        var authentication = new SessionAuthenticationService(); authentication.SignIn(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }));
        var sessionId = Guid.NewGuid();
        var viewModel = new ReportViewModel(new ReportService(new ReportRepository(sessionId, true), authentication, new RoleAuthorization())) { SessionId = sessionId.ToString() };
        Assert.True(await viewModel.ExportExtraDimensionsAsync()); Assert.Contains("StepReference", viewModel.CsvContent); Assert.Contains("Edge:extra", viewModel.CsvContent); Assert.Equal("Extra dimensions exported.", viewModel.Status);
    }
    [Fact(DisplayName = "Verification report content uses a display-only binding")]
    public void Report_content_binding_is_one_way()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = new ReportView();
                var reportContent = Descendants(view)
                    .OfType<TextBox>()
                    .Single(textBox => BindingOperations.GetBinding(textBox, TextBox.TextProperty)?.Path.Path == nameof(ReportViewModel.ReportContent));

                Assert.Equal(BindingMode.OneWay, BindingOperations.GetBinding(reportContent, TextBox.TextProperty)!.Mode);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)), "WPF binding assertion thread did not finish within the bound.");

        if (failure is not null) throw new Xunit.Sdk.XunitException("Report content binding assertion failed.", failure);
    }

    [Fact(DisplayName = "TD-008 CSV export command surfaces authorized export content")]
    public async Task Csv_export_updates_content_and_status()
    {
        var authentication = new SessionAuthenticationService(); authentication.SignIn(new AuthenticatedUser("approver", new[] { SystemRoles.Approver }) { UserId = "approver" });
        var sessionId = Guid.NewGuid(); var service = new ReportService(new ReportRepository(sessionId), authentication, new RoleAuthorization()); var viewModel = new ReportViewModel(service) { SessionId = sessionId.ToString() };
        Assert.True(await viewModel.ExportMissingDimensionsAsync()); Assert.Contains("Layer", viewModel.CsvContent); Assert.Contains("DIM", viewModel.CsvContent); Assert.Equal("Missing dimensions exported.", viewModel.Status);
    }

    [Fact(DisplayName = "TD-007 report preview includes finalized Checker metadata and preserves unsigned reports")]
    public void Report_preview_renders_checker_attestation_or_explicit_absence()
    {
        var signature = SignatureData.CreateChecker(SignatureMethod.Typed, "checker approval", "checker-001", "Checker One", Guid.NewGuid(), 1, new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero), "Quality", "Inspection");
        var signed = new VerificationReport { CheckerSignature = signature }.ToMarkdown();
        var unsigned = new VerificationReport().ToMarkdown();
        Assert.Contains("Checker One", signed);
        Assert.Contains("Checker", signed);
        Assert.Contains("2026-09-16", signed);
        Assert.DoesNotContain("Checker Signature:", unsigned);
    }

    [Fact(DisplayName = "TD-008 report ViewModel export remains authorized and version-compatible")]
    public void Report_view_contract_exposes_checker_projection_without_required_signature()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "ViewModels", "ReportViewModel.cs")));
        Assert.Contains("Export", source);
        Assert.Contains("CsvContent", source);
        var report = new VerificationReport();
        Assert.Null(report.CheckerSignature);
    }

    [Fact(DisplayName = "TD-KAN70-08 PDF download command exposes scoped generation and busy-state contract")]
    public async Task Pdf_download_command_and_scope_controls_are_exposed()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "ViewModels", "ReportViewModel.cs")));
        var xaml = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Views", "ReportView.xaml")));
        var authentication = new SessionAuthenticationService();
        authentication.SignIn(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }));
        var viewModel = new ReportViewModel(new ReportService(new ReportRepository(Guid.NewGuid()), authentication, new RoleAuthorization()));

        Assert.NotNull(viewModel.GeneratePdfCommand);
        Assert.True(viewModel.GeneratePdfCommand.CanExecute(null));
        Assert.Contains("GeneratePdfCommand", source);
        Assert.Contains("MismatchedAndMissingOnly", source);
        Assert.Contains("GdtOnly", source);
        Assert.Contains("Download PDF report", xaml);
        Assert.Contains("MismatchedAndMissingOnly", xaml);
        Assert.Contains("GdtOnly", xaml);
        viewModel.SessionId = "not-a-guid";
        Assert.False(await viewModel.GeneratePdfAsync());
        Assert.Contains("valid verification session", viewModel.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN70-09 report view preserves existing bindings and adds PDF/scoped bindings")]
    public void Report_view_preserves_preview_csv_and_new_report_bindings()
    {
        var xaml = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Views", "ReportView.xaml")));
        Assert.Contains("ReportContent", xaml);
        Assert.Contains("GenerateCommand", xaml);
        Assert.Contains("ExportMissingDimensionsCommand", xaml);
        Assert.Contains("ExportExtraDimensionsCommand", xaml);
        Assert.Contains("GeneratePdfCommand", xaml);
        Assert.Contains("IsChecked=\"{Binding MismatchedAndMissingOnly}", xaml);
        Assert.Contains("IsChecked=\"{Binding GdtOnly}", xaml);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current is not null; index++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }

    private sealed class ReportRepository(Guid sessionId, bool extra = false) : IVerificationSessionRepository
    {
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = sessionId });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(extra ? new[] { new DimensionMatch { DimensionMatchId = Guid.NewGuid(), Status = DimensionMatchStatus.ExtraDimension, StepGeometryReference = "Edge:extra", MeasuredValue = 8 } } : new[] { new DimensionMatch { DimensionMatchId = Guid.NewGuid(), Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 1, NominalValue = 50, DxfLayer = "DIM", DimensionText = "50.00" } });
        public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static IEnumerable<object> Descendants(object parent)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren((System.Windows.DependencyObject)parent).Cast<object>())
        {
            yield return child;
            if (child is System.Windows.DependencyObject dependencyObject)
                foreach (var descendant in Descendants(dependencyObject)) yield return descendant;
        }
    }
}
