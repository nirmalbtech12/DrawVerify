using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using Microsoft.Win32;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.App.Controls;

public partial class SignatureStampControl : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty IsMakerProperty = DependencyProperty.Register(nameof(IsMaker), typeof(bool), typeof(SignatureStampControl), new PropertyMetadata(false, OnIsMakerChanged));
    public static readonly DependencyProperty IsCheckerProperty = DependencyProperty.Register(nameof(IsChecker), typeof(bool), typeof(SignatureStampControl), new PropertyMetadata(false, OnIsMakerChanged));
    public static readonly DependencyProperty IsApproverProperty = DependencyProperty.Register(nameof(IsApprover), typeof(bool), typeof(SignatureStampControl), new PropertyMetadata(false, OnIsMakerChanged));
    public static readonly DependencyProperty MakerUserIdProperty = DependencyProperty.Register(nameof(MakerUserId), typeof(string), typeof(SignatureStampControl), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty MakerDisplayNameProperty = DependencyProperty.Register(nameof(MakerDisplayName), typeof(string), typeof(SignatureStampControl), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty FinalizedSignatureProperty = DependencyProperty.Register(nameof(FinalizedSignature), typeof(SignatureData), typeof(SignatureStampControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnFinalizedChanged));
    public static readonly DependencyProperty DraftNameProperty = DependencyProperty.Register(nameof(DraftName), typeof(string), typeof(SignatureStampControl), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SignatureStampControl), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DepartmentProperty = DependencyProperty.Register(nameof(Department), typeof(string), typeof(SignatureStampControl), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty LoggerProperty = DependencyProperty.Register(nameof(Logger), typeof(ICentralizedLogger), typeof(SignatureStampControl), new PropertyMetadata(null));
    public static readonly DependencyProperty DocumentIdProperty = DependencyProperty.Register(nameof(DocumentId), typeof(Guid), typeof(SignatureStampControl), new PropertyMetadata(Guid.Empty));
    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(nameof(Revision), typeof(int), typeof(SignatureStampControl), new PropertyMetadata(0));
    public bool IsMaker { get => (bool)GetValue(IsMakerProperty); set => SetValue(IsMakerProperty, value); }
    public bool IsChecker { get => (bool)GetValue(IsCheckerProperty); set => SetValue(IsCheckerProperty, value); }
    public bool IsApprover { get => (bool)GetValue(IsApproverProperty); set => SetValue(IsApproverProperty, value); }
    public bool IsSignatureVisible => IsMaker || IsChecker || IsApprover;
    public string SignatureLabel => IsApprover ? "Approver Signature" : IsChecker ? "Checker Signature" : "Maker Signature";
    public string MakerUserId { get => (string)GetValue(MakerUserIdProperty); set => SetValue(MakerUserIdProperty, value); }
    public string MakerDisplayName { get => (string)GetValue(MakerDisplayNameProperty); set => SetValue(MakerDisplayNameProperty, value); }
    public SignatureData? FinalizedSignature { get => (SignatureData?)GetValue(FinalizedSignatureProperty); set => SetValue(FinalizedSignatureProperty, value); }
    public string DraftName { get => (string)GetValue(DraftNameProperty); set => SetValue(DraftNameProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Department { get => (string)GetValue(DepartmentProperty); set => SetValue(DepartmentProperty, value); }
    public ICentralizedLogger? Logger { get => (ICentralizedLogger?)GetValue(LoggerProperty); set => SetValue(LoggerProperty, value); }
    public Guid DocumentId { get => (Guid)GetValue(DocumentIdProperty); set => SetValue(DocumentIdProperty, value); }
    public int Revision { get => (int)GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }
    public bool IsFinalized => FinalizedSignature is not null;
    public bool CanEdit => (IsMaker || IsChecker || IsApprover) && !IsFinalized;
    public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewText);
    public bool IsPreviewVisible => HasPreview;
    public string PreviewText { get; private set; } = string.Empty;
    public string ErrorText { get; private set; } = string.Empty;
    public SignatureData? PendingSignature { get; private set; }
    public event EventHandler? SignatureFinalizationRequested;
    public event EventHandler? ApproverSignatureFinalizationRequested;
    public string MetadataText => FinalizedSignature is null ? string.Empty : string.Join(" · ", new[] { FinalizedSignature.IsChecker ? FinalizedSignature.CheckerDisplayName : FinalizedSignature.IsApprover ? FinalizedSignature.ApproverDisplayName : FinalizedSignature.MakerDisplayName, FinalizedSignature.Role, $"{FinalizedSignature.TimestampUtc:O}", FinalizedSignature.Method.ToString(), FinalizedSignature.Title, FinalizedSignature.Department }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public event PropertyChangedEventHandler? PropertyChanged;
    public SignatureStampControl() { InitializeComponent(); }
    private static void OnIsMakerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { ((SignatureStampControl)d).NotifyState(); }
    private static void OnFinalizedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { ((SignatureStampControl)d).NotifyState(); }
    private void PreviewTyped_Click(object sender, RoutedEventArgs e) => Preview(SignatureMethod.Typed, DraftName);
    private void PreviewDrawing_Click(object sender, RoutedEventArgs e)
    {
        try { using var stream = new MemoryStream(); DrawingSurface.Strokes.Save(stream, false); Preview(SignatureMethod.Drawn, Convert.ToBase64String(stream.ToArray())); }
        catch (Exception exception) { ShowCaptureError(exception); }
    }
    private void Upload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
            if (dialog.ShowDialog() != true) return;
            var bytes = File.ReadAllBytes(dialog.FileName);
            if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024) throw new InvalidDataException("The signature image is empty or exceeds the supported size.");
            var payload = Convert.ToBase64String(bytes);
            SignatureData.ValidateUploadedPayload(payload);
            Preview(SignatureMethod.Uploaded, payload);
        }
        catch (Exception exception) { ShowCaptureError(exception); }
    }
    private void Preview(SignatureMethod method, string payload)
    {
        if (!CanEdit) return;
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("Signature input is required.");
        if (method == SignatureMethod.Uploaded) SignatureData.ValidateUploadedPayload(payload);
        PreviewText = $"{method}: {payload}"; ErrorText = string.Empty; NotifyState();
    }
    private void Finalize_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!CanEdit) return;
            if (string.IsNullOrWhiteSpace(PreviewText)) throw new ArgumentException("Preview the signature before finalizing.");
            var separator = PreviewText.IndexOf(": ", StringComparison.Ordinal);
            var method = Enum.Parse<SignatureMethod>(PreviewText[..separator]);
            var payload = PreviewText[(separator + 2)..];
            var signature = IsApprover ? SignatureData.CreateApprover(method, payload, MakerUserId, MakerDisplayName, DocumentId, Revision, DateTimeOffset.UtcNow, Title, Department) : IsChecker ? SignatureData.CreateChecker(method, payload, MakerUserId, MakerDisplayName, DocumentId, Revision, DateTimeOffset.UtcNow, Title, Department) : SignatureData.Create(method, payload, MakerUserId, MakerDisplayName, DocumentId, Revision, DateTimeOffset.UtcNow, Title, Department);
            if (IsApprover) { PendingSignature = signature; ApproverSignatureFinalizationRequested?.Invoke(this, EventArgs.Empty); }
            else if (IsChecker) { PendingSignature = signature; SignatureFinalizationRequested?.Invoke(this, EventArgs.Empty); }
            else FinalizedSignature = signature;
            ErrorText = string.Empty; NotifyState();
        }
        catch (Exception exception) { ShowCaptureError(exception); }
    }
    private void CancelPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        PreviewText = string.Empty;
        ErrorText = string.Empty;
        NotifyState();
    }
    private void ShowCaptureError(Exception exception) { Logger?.Log(exception, "WPF signature capture", $"Unable to capture signature for documentId={DocumentId}, revision={Revision}."); ErrorText = "Failed to capture signature. Please try again."; NotifyState(); }
    private void NotifyState() { Raise(nameof(IsFinalized)); Raise(nameof(CanEdit)); Raise(nameof(IsSignatureVisible)); Raise(nameof(SignatureLabel)); Raise(nameof(HasPreview)); Raise(nameof(IsPreviewVisible)); Raise(nameof(PreviewText)); Raise(nameof(ErrorText)); Raise(nameof(MetadataText)); }
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
