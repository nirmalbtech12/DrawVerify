using System.Windows.Input;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
namespace DrawVerify.App.ViewModels;
public sealed class WorkflowConfigurationViewModel : TaskScreenViewModel
{
    private readonly IWorkflowConfigurationService _service; private readonly ISessionAuthenticationService _session; private readonly RoleAuthorization _authorization; private readonly ICentralizedLogger? _logger;
    private string _message=string.Empty; private string _errors=string.Empty; private WorkflowConfiguration? _configuration;
    public WorkflowConfigurationViewModel(IWorkflowConfigurationService service, ISessionAuthenticationService session, RoleAuthorization authorization, ICentralizedLogger? logger=null) { _service=service; _session=session; _authorization=authorization; _logger=logger; SaveCommand=new Command(async _=>await SaveAsync(), _=>CanEdit); PreviewCommand=new Command(async _=>await PreviewAsync(), _=>CanEdit); _=LoadAsync(); }
    public bool CanEdit => _session.IsAuthenticated && _authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageGlobalConfiguration);
    public WorkflowConfiguration? Configuration { get=>_configuration; private set { _configuration=value; RaisePropertyChanged(nameof(Configuration)); RaisePropertyChanged(nameof(RolesSummary)); } }
    public string RolesSummary => string.Join(", ", Configuration?.Roles.Select(x=>$"{x.DisplayName} ({x.SystemRole})") ?? Array.Empty<string>());
    public string Message { get=>_message; private set { _message=value; RaisePropertyChanged(nameof(Message)); } }
    public string ValidationErrors { get=>_errors; private set { _errors=value; RaisePropertyChanged(nameof(ValidationErrors)); } }
    public ICommand SaveCommand { get; } public ICommand PreviewCommand { get; }
    private async Task LoadAsync() { try { Configuration=await _service.GetAsync(); } catch(InvalidOperationException) { } catch(Exception ex) { _logger?.Log(ex,"Workflow configuration UI load","Unable to load workflow configuration."); Message="Unable to load workflow configuration."; } }
    private async Task SaveAsync() { try { if(Configuration is null)return; Configuration=await _service.SaveAsync(Configuration,Configuration.Version); Message="Workflow configuration saved."; IsDirty=false; ValidationErrors=string.Empty; } catch(InvalidOperationException ex) { ValidationErrors=ex.Message; } catch(Exception ex) { _logger?.Log(ex,"Workflow configuration UI save","Unable to save workflow configuration."); Message="Unable to save workflow configuration."; } }
    private async Task PreviewAsync() { try { if(Configuration is null)return; var result=await _service.PreviewAsync(Configuration,new WorkflowPreviewMetadata()); Message=result.MatchesTrigger?"Preview: workflow matches configured triggers.":"Preview: no workflow trigger matches."; } catch(InvalidOperationException ex) { ValidationErrors=ex.Message; } catch(Exception ex) { _logger?.Log(ex,"Workflow configuration UI preview","Unable to preview workflow configuration."); Message="Unable to preview workflow configuration."; } }
    private sealed class Command(Action<object?> execute, Predicate<object?> canExecute):ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p)=>canExecute(p); public void Execute(object? p)=>execute(p); }
}
