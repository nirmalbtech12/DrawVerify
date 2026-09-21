using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateConversionServiceTests
{
    [Fact(DisplayName = "TSD-01 authorized upload propagates source and actor before publication")]
    public async Task Authorized_upload_starts_conversion_and_publishes_only_after_success()
    {
        var templates = new TemplateRepositoryFake(); var documents = new DocumentRepositoryFake(); var converter = new ConverterFake { Result = Docx() }; var logger = new LoggerFake();
        var session = Admin(); var source = new ReportTemplateSource { SourceId = Guid.NewGuid(), FileName = "source.pdf", PdfBytes = [1, 2, 3] };
        var result = await new ReportTemplateConversionService(templates, documents, converter, session, new RoleAuthorization(), logger).ConvertAsync(source);
        Assert.Equal(source, converter.Source); Assert.Single(templates.Items); Assert.Equal(result.Id, templates.Items[0].Id); Assert.Equal("admin", documents.Audits.Single().ActorId); Assert.Empty(logger.Errors);
    }

    [Theory(DisplayName = "TSD-02 malformed or non-PDF upload is rejected"), InlineData("source.txt", new byte[] { 1 }), InlineData("source.pdf", new byte[] { })]
    public async Task Invalid_upload_does_not_start_or_publish(string name, byte[] bytes)
    {
        var templates = new TemplateRepositoryFake(); var documents = new DocumentRepositoryFake(); var converter = new ConverterFake(); var logger = new LoggerFake();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReportTemplateConversionService(templates, documents, converter, Admin(), new RoleAuthorization(), logger).ConvertAsync(new ReportTemplateSource { FileName = name, PdfBytes = bytes }));
        Assert.Equal(0, converter.Calls); Assert.Empty(templates.Items);
    }

    [Fact(DisplayName = "TSD-04 conversion failure is visible and logged with source context")]
    public async Task Conversion_failure_has_no_document_and_retains_original_exception()
    {
        var documents = new DocumentRepositoryFake(); var converter = new ConverterFake { Error = new InvalidDataException("parser detail") }; var logger = new LoggerFake(); var source = new ReportTemplateSource { FileName = "bad.pdf", PdfBytes = [1] };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new ReportTemplateConversionService(new TemplateRepositoryFake(), documents, converter, Admin(), new RoleAuthorization(), logger).ConvertAsync(source));
        Assert.Contains("parser detail", error.Message); Assert.Empty(documents.Documents); Assert.Contains(logger.Errors, x => x.Exception is InvalidDataException && x.Context.Contains(source.FileName));
    }

    [Fact(DisplayName = "TSD-06 stored source-linked template is available to the editor")]
    public async Task Stored_document_preserves_source_linkage()
    {
        var source = new ReportTemplateSource { FileName = "source.pdf", PdfBytes = [1] }; var documents = new DocumentRepositoryFake(); var templates = new TemplateRepositoryFake();
        var saved = await new ReportTemplateConversionService(templates, documents, new ConverterFake { Result = Docx() }, Admin(), new RoleAuthorization()).ConvertAsync(source);
        var document = await documents.GetAsync(saved.Id, saved.Version);
        Assert.NotNull(document); Assert.Equal(source.SourceId, document!.SourceId); Assert.Equal(source.FileName, document.SourceFileName); Assert.NotEmpty(document.Content);
    }

    [Fact(DisplayName = "TSD-07 reconversion versions the target and retains source linkage")]
    public async Task Reconversion_replaces_target_without_changing_unrelated_template()
    {
        var source = new ReportTemplateSource { FileName = "source.pdf", PdfBytes = [1] }; var documents = new DocumentRepositoryFake(); var templates = new TemplateRepositoryFake(); var converter = new ConverterFake { Result = Docx(2) };
        var service = new ReportTemplateConversionService(templates, documents, converter, Admin(), new RoleAuthorization()); var target = await service.ConvertAsync(source); var unrelated = await service.ConvertAsync(new ReportTemplateSource { FileName = "other.pdf", PdfBytes = [2] });
        var replacement = await service.ReconvertAsync(target.Id, target.Version); var stored = await documents.GetAsync(target.Id, replacement.Version);
        Assert.Equal(target.Version + 1, replacement.Version); Assert.Equal(source.SourceId, replacement.Source!.SourceId); Assert.Equal(2, stored!.Content[3]); Assert.Equal(unrelated.Version, (await templates.GetAsync(unrelated.Id))!.Version);
    }

    [Fact(DisplayName = "TSD-08 failed reconversion retains the prior artifact")]
    public async Task Failed_reconversion_keeps_prior_version_and_logs_failure()
    {
        var source = new ReportTemplateSource { FileName = "source.pdf", PdfBytes = [1] }; var documents = new DocumentRepositoryFake(); var templates = new TemplateRepositoryFake(); var converter = new ConverterFake { Result = Docx(1) }; var logger = new LoggerFake();
        var service = new ReportTemplateConversionService(templates, documents, converter, Admin(), new RoleAuthorization(), logger); var target = await service.ConvertAsync(source); var prior = await documents.GetAsync(target.Id, target.Version); converter.Error = new InvalidDataException("reconversion failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReconvertAsync(target.Id, target.Version));
        Assert.Equal(prior, await documents.GetAsync(target.Id, target.Version)); Assert.Contains(logger.Errors, x => x.Exception.Message == "reconversion failed");
    }

    [Fact(DisplayName = "TSD-09 success and failure create complete audit outcomes")]
    public async Task Conversion_outcomes_record_success_and_failure_audits()
    {
        var documents = new DocumentRepositoryFake(); var templates = new TemplateRepositoryFake(); var converter = new ConverterFake { Result = Docx() }; var service = new ReportTemplateConversionService(templates, documents, converter, Admin(), new RoleAuthorization());
        await service.ConvertAsync(new ReportTemplateSource { FileName = "ok.pdf", PdfBytes = [1] }); converter.Error = new InvalidDataException("bad"); await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConvertAsync(new ReportTemplateSource { FileName = "bad.pdf", PdfBytes = [2] }));
        Assert.Equal(2, documents.Audits.Count); Assert.Contains(documents.Audits, x => x.Status == "Success" && x.TemplateId is not null && x.ActorId == "admin"); Assert.Contains(documents.Audits, x => x.Status == "Failed" && !string.IsNullOrWhiteSpace(x.ErrorDetails));
    }

    [Fact(DisplayName = "TSD-10 audit persistence failure is visible and not reported as success")]
    public async Task Audit_write_failure_is_logged_and_fails_operation()
    {
        var documents = new DocumentRepositoryFake { ThrowOnAudit = true }; var logger = new LoggerFake(); var service = new ReportTemplateConversionService(new TemplateRepositoryFake(), documents, new ConverterFake { Result = Docx() }, Admin(), new RoleAuthorization(), logger);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConvertAsync(new ReportTemplateSource { FileName = "source.pdf", PdfBytes = [1] }));
        Assert.Contains("audit record", error.Message); Assert.Contains(logger.Errors, x => x.Exception.Message == "audit unavailable" && x.Context.Contains("sourceId") && x.Context.Contains("templateId") && x.Context.Contains("actorId"));
    }

    private static SessionAuthenticationService Admin() { var s = new SessionAuthenticationService(); s.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin })); return s; }
    private static PdfToWordConversionResult Docx(byte marker = 1) => new() { Content = [0x50, 0x4B, 0x03, marker] };
    private sealed class ConverterFake : IPdfToWordConverter { public int Calls; public ReportTemplateSource? Source; public PdfToWordConversionResult Result { get; set; } = Docx(); public Exception? Error { get; set; } public Task<PdfToWordConversionResult> ConvertAsync(ReportTemplateSource source, CancellationToken cancellationToken = default) { Calls++; Source = source; return Error is null ? Task.FromResult(Result) : Task.FromException<PdfToWordConversionResult>(Error); } }
    private sealed class TemplateRepositoryFake : IReportTemplateRepository { public List<ReportTemplate> Items { get; } = []; public Task<IReadOnlyList<ReportTemplate>> ListAsync(CancellationToken c = default) => Task.FromResult<IReadOnlyList<ReportTemplate>>(Items); public Task<ReportTemplate?> GetAsync(Guid id, int? version = null, CancellationToken c = default) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id && (!version.HasValue || x.Version == version))); public Task<ReportTemplate> SaveAsync(ReportTemplate t, int expectedVersion, ReportTemplateAuditEntry a, CancellationToken c = default) { var saved = t with { Version = expectedVersion == 0 ? 1 : expectedVersion + 1 }; var i = Items.FindIndex(x => x.Id == saved.Id); if (i >= 0) Items[i] = saved; else Items.Add(saved); return Task.FromResult(saved); } public Task DeleteAsync(Guid i, int v, ReportTemplateAuditEntry a, CancellationToken c = default) => throw new NotImplementedException(); public Task SetDefaultAsync(Guid i, ReportTemplateAuditEntry a, CancellationToken c = default) => Task.CompletedTask; public Task<IReadOnlyList<ReportTemplateVersion>> GetVersionsAsync(Guid i, CancellationToken c = default) => Task.FromResult<IReadOnlyList<ReportTemplateVersion>>([]); public Task AssignAsync(ReportTemplateAssignment a, ReportTemplateAuditEntry b, CancellationToken c = default) => Task.CompletedTask; public Task<ReportTemplate?> ResolveAsync(string? p, string? w, CancellationToken c = default) => Task.FromResult<ReportTemplate?>(null); public Task<IReadOnlyList<ReportTemplateAuditEntry>> GetAuditAsync(Guid? i = null, CancellationToken c = default) => Task.FromResult<IReadOnlyList<ReportTemplateAuditEntry>>([]); }
    private sealed class DocumentRepositoryFake : IReportTemplateDocumentRepository { public List<ReportTemplateDocument> Documents { get; } = []; public List<TemplateConversionAuditEntry> Audits { get; } = []; public bool ThrowOnAudit; public Task<ReportTemplateDocument> SaveAsync(ReportTemplateDocument d, int? e = null, CancellationToken c = default) { Documents.RemoveAll(x => x.TemplateId == d.TemplateId && x.Version == d.Version); Documents.Add(d); return Task.FromResult(d); } public Task<ReportTemplateDocument?> GetAsync(Guid t, int? v = null, CancellationToken c = default) => Task.FromResult(Documents.LastOrDefault(x => x.TemplateId == t && (!v.HasValue || x.Version == v))); public Task<ReportTemplateSource?> GetSourceAsync(Guid s, CancellationToken c = default) => Task.FromResult<ReportTemplateSource?>(null); public Task SaveSourceAsync(ReportTemplateSource s, CancellationToken c = default) => Task.CompletedTask; public Task RecordConversionAuditAsync(TemplateConversionAuditEntry e, CancellationToken c = default) { if (ThrowOnAudit) return Task.FromException(new InvalidOperationException("audit unavailable")); Audits.Add(e); return Task.CompletedTask; } public Task<IReadOnlyList<TemplateConversionAuditEntry>> GetConversionAuditAsync(Guid? t = null, CancellationToken c = default) => Task.FromResult<IReadOnlyList<TemplateConversionAuditEntry>>(Audits); }
    private sealed class LoggerFake : ICentralizedLogger { public List<(Exception Exception, string Context)> Errors { get; } = []; public void Log(Exception e, string o, string c) => Errors.Add((e, c)); }
}
