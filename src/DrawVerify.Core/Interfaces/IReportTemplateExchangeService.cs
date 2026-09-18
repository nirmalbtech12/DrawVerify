using DrawVerify.Core.Models;
namespace DrawVerify.Core.Interfaces;
public interface IReportTemplateExchangeService { string Export(ReportTemplate template); ReportTemplate Import(string json); }
