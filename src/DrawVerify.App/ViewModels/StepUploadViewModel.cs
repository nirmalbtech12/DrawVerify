using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.Application.StepUpload;

namespace DrawVerify.App.ViewModels;

public sealed class StepUploadViewModel : TaskScreenViewModel
{
    private readonly DocumentService _service;
    private CancellationTokenSource? _cancellation;
    private string? _fileName;
    private Stream? _content;
    public StepUploadViewModel(DocumentService service) { _service=service; IsAuthorized=service.CanUploadStep; SelectCommand=new RelayCommand(_=>SelectRequested?.Invoke(), _=>IsAuthorized&&!IsBusy); UploadCommand=new RelayCommand(async _=>await UploadAsync(), _=>IsAuthorized&&_content is not null&&!IsBusy); CancelCommand=new RelayCommand(_=>_cancellation?.Cancel(), _=>IsBusy); }
    public event Action? SelectRequested;
    public string? FileName { get=>_fileName; set { _fileName=value; OnChanged(); } }
    public string Message { get; private set; }=string.Empty;
    public double Progress { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsAuthorized { get; }
    public ICommand SelectCommand { get; }
    public event Action<DrawVerify.Core.Models.Document>? DocumentUploaded;
    public ICommand UploadCommand { get; }
    public ICommand CancelCommand { get; }
    public void Select(string name, Stream content) { _content?.Dispose();FileName=name;_content=content;MarkDirty();Message=string.Empty;OnChanged(nameof(Message));((RelayCommand)UploadCommand).Raise(); }
    private async Task UploadAsync()
    { if(_content is null||FileName is null)return; IsBusy=true;Progress=0;OnChanged(nameof(IsBusy));OnChanged(nameof(Progress));_cancellation=new CancellationTokenSource();try { var request=new StepUploadRequest(FileName,_content){Progress=new Progress<long>(bytes=>{Progress=_content.Length==0?100:bytes*100d/_content.Length;OnChanged(nameof(Progress));}),CancellationToken=_cancellation.Token};var result=await _service.PrepareStepUploadAsync(request);if(result.IsDuplicate){var answer=System.Windows.MessageBox.Show(result.Message,"Duplicate",System.Windows.MessageBoxButton.YesNo);if(answer==System.Windows.MessageBoxResult.Yes)result=await _service.UploadStepAsync(request,true);else if(result.Document is not null)result=await _service.CancelDuplicateAsync(request,result.Document.DocumentId);}else if(result.Kind==StepUploadResultKind.Success)result=await _service.UploadStepAsync(request);Message=result.Message;OnChanged(nameof(Message));if(result.Document is not null&&result.Kind==StepUploadResultKind.Success)DocumentUploaded?.Invoke(result.Document); } catch(OperationCanceledException){Message="Upload cancelled.";OnChanged(nameof(Message));} catch(Exception ex){Message=$"Upload failed: {ex.Message}";OnChanged(nameof(Message));var details=$"Upload failed for '{FileName}'.\n\n{ex}";System.Windows.MessageBox.Show(details,"Upload failed (Ctrl+C copies details)",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Error);}finally{IsBusy=false;_cancellation.Dispose();_cancellation=null;OnChanged(nameof(IsBusy));((RelayCommand)UploadCommand).Raise();((RelayCommand)CancelCommand).Raise();} }
    private void OnChanged([CallerMemberName]string? name=null)=>RaisePropertyChanged(name);
    private sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute=null):ICommand { public event EventHandler? CanExecuteChanged;public bool CanExecute(object? p)=>canExecute?.Invoke(p)??true;public void Execute(object? p)=>execute(p);public void Raise()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty); }
}
