using DrawVerify.App.ViewModels;
using DrawVerify.Core.Interfaces;
using DrawVerify.OcctService;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class StepPreviewViewModelTests
{
    [Fact(DisplayName = "TSD-01 processed STEP model becomes available and preview failures remain visible")]
    public async Task Uploaded_model_is_available_after_processing_and_failure_is_visible()
    {
        var service = new ControlledPreviewService();
        using var viewModel = new StepPreviewViewModel(preview: service);
        var id = Guid.NewGuid();
        viewModel.AddUploadedModel(id, "part.step", "part.step");
        await WaitForAsync(() => !viewModel.IsProcessing && viewModel.IsModelLoaded);
        Assert.True(viewModel.IsModelLoaded);
        Assert.Equal("part.step", viewModel.ModelPath);
        Assert.False(viewModel.IsProcessing);

        service.FailPaths.Add("broken.step");
        var failed = new StepPreviewModelChoice(Guid.NewGuid(), "broken.step", "broken.step");
        viewModel.Models.Add(failed);
        viewModel.SelectedModel = failed;
        await service.WaitForCallAsync("broken.step");
        await WaitForAsync(() => !viewModel.IsProcessing);
        Assert.False(failed.IsLoaded);
        Assert.Equal("The STEP preview service could not load the model.", failed.Error);
        Assert.False(viewModel.IsModelLoaded);
    }

    [Fact(DisplayName = "TSD-02 processing state and status span pending model loading and finish on success or failure")]
    public async Task Processing_status_tracks_pending_success_and_failure()
    {
        var service = new ControlledPreviewService { HoldLoads = true };
        using var viewModel = new StepPreviewViewModel(preview: service);
        viewModel.AddUploadedModel(Guid.NewGuid(), "part.step", "part.step");
        await service.WaitForCallAsync("part.step");
        Assert.True(viewModel.IsProcessing);
        Assert.Contains("Processing 3D model", viewModel.ProcessingStatus);
        service.Release("part.step");
        await WaitForAsync(() => !viewModel.IsProcessing);
        Assert.Empty(viewModel.ProcessingStatus);

        service.HoldLoads = false;
        service.FailPaths.Add("broken.step");
        var broken = new StepPreviewModelChoice(Guid.NewGuid(), "broken.step", "broken.step");
        viewModel.Models.Add(broken);
        viewModel.SelectedModel = broken;
        await WaitForAsync(() => !viewModel.IsProcessing);
        Assert.Contains("failed", viewModel.ProcessingStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TSD-02 cancellation ends model processing")]
    public async Task Processing_cancellation_ends_loading_with_cancellation_status()
    {
        var service = new ControlledPreviewService { HoldLoads = true };
        using var viewModel = new StepPreviewViewModel(preview: service);
        viewModel.AddUploadedModel(Guid.NewGuid(), "part.step", "part.step");
        await service.WaitForCallAsync("part.step");
        Assert.True(viewModel.IsProcessing);
        Assert.Contains("Processing 3D model", viewModel.ProcessingStatus);

        viewModel.CancelProcessing();
        await WaitForAsync(() => !viewModel.IsProcessing);
        Assert.Equal("Processing cancelled.", viewModel.ProcessingStatus);
        service.Release("part.step");
    }

    [Fact(DisplayName = "TSD-04 model switching keeps individual failure state and disposes preview service")]
    public async Task Model_choices_switch_independently_and_preview_is_disposed()
    {
        var service = new ControlledPreviewService();
        var viewModel = new StepPreviewViewModel(preview: service);
        var first = new StepPreviewModelChoice(Guid.NewGuid(), "first.step", "first.step");
        var second = new StepPreviewModelChoice(Guid.NewGuid(), "second.step", "second.step");
        service.FailPaths.Add("second.step");
        viewModel.Models.Add(first);
        viewModel.Models.Add(second);
        viewModel.SelectedModel = first;
        await WaitForAsync(() => !viewModel.IsProcessing && first.IsLoaded);
        Assert.True(first.IsLoaded);
        viewModel.SelectedModel = second;
        await WaitForAsync(() => !viewModel.IsProcessing && !string.IsNullOrEmpty(second.Error));
        Assert.False(second.IsLoaded);
        Assert.NotEmpty(second.Error);
        Assert.True(first.IsLoaded);
        viewModel.Dispose();
        Assert.True(service.IsDisposed);
    }

    [Fact(DisplayName = "TSD-05 settings restore for the same document and remain isolated between documents")]
    public async Task Viewer_settings_round_trip_is_document_scoped()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrawVerify", "viewer-settings");
        var firstPath = Path.Combine(root, $"{firstId:N}.json");
        var secondPath = Path.Combine(root, $"{secondId:N}.json");
        System.Windows.Media.Media3D.Point3D firstCameraPosition = default;
        System.Windows.Media.Media3D.Vector3D firstCameraLookDirection = default;
        try
        {
            using (var first = new StepPreviewViewModel(preview: new ControlledPreviewService()))
            {
                first.AddUploadedModel(firstId, "first.step", "first.step");
                await WaitForAsync(() => !first.IsProcessing && first.IsModelLoaded);
                first.Orbit(30, 10);
                first.Zoom(0.5);
                first.SectionAxis = "X";
                first.SectionPosition = 0.25;
                first.IsSectionEnabled = true;
                firstCameraPosition = first.CameraPosition;
                firstCameraLookDirection = first.CameraLookDirection;
            }

            using var reloaded = new StepPreviewViewModel(preview: new ControlledPreviewService());
            reloaded.AddUploadedModel(firstId, "first.step", "first.step");
            await WaitForAsync(() => !reloaded.IsProcessing && reloaded.IsModelLoaded);
            Assert.Equal(2, reloaded.CameraZoom);
            Assert.Equal(firstCameraPosition, reloaded.CameraPosition);
            Assert.Equal(firstCameraLookDirection, reloaded.CameraLookDirection);
            Assert.True(reloaded.IsSectionEnabled);
            Assert.Equal("X", reloaded.SectionAxis);
            Assert.Equal(0.25, reloaded.SectionPosition);

            using var other = new StepPreviewViewModel(preview: new ControlledPreviewService());
            other.AddUploadedModel(secondId, "second.step", "second.step");
            await WaitForAsync(() => !other.IsProcessing && other.IsModelLoaded);
            Assert.Equal(1, other.CameraZoom);

            File.WriteAllText(secondPath, "not-json");
            var logger = new CapturingLogger();
            using var malformed = new StepPreviewViewModel(logger, new ControlledPreviewService());
            malformed.AddUploadedModel(secondId, "second.step", "second.step");
            await WaitForAsync(() => !malformed.IsProcessing && malformed.IsModelLoaded);
            Assert.Equal(1, malformed.CameraZoom);
            Assert.Equal("Viewer settings could not be restored.", malformed.ProcessingStatus);
            Assert.Contains(logger.Entries, entry => entry.Operation == "Restore STEP viewer settings" && entry.Exception is System.Text.Json.JsonException);

            File.WriteAllText(secondPath, "{\"TargetX\":0,\"TargetY\":0,\"TargetZ\":0,\"PositionX\":0,\"PositionY\":0,\"PositionZ\":250,\"LookX\":0,\"LookY\":0,\"LookZ\":-250,\"Zoom\":99}");
            using var outOfRange = new StepPreviewViewModel(logger, new ControlledPreviewService());
            outOfRange.AddUploadedModel(secondId, "second.step", "second.step");
            await WaitForAsync(() => !outOfRange.IsProcessing && outOfRange.IsModelLoaded);
            Assert.Equal(1, outOfRange.CameraZoom);
            Assert.Equal("Viewer settings could not be restored.", outOfRange.ProcessingStatus);

            var lockedPath = Path.Combine(root, $"{firstId:N}.json");
            File.WriteAllText(lockedPath, "{}");
            using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var unreadableLogger = new CapturingLogger();
                using var unreadable = new StepPreviewViewModel(unreadableLogger, new ControlledPreviewService());
                unreadable.AddUploadedModel(firstId, "first.step", "first.step");
                await WaitForAsync(() => !unreadable.IsProcessing && unreadable.IsModelLoaded);
                Assert.Equal("Viewer settings could not be restored.", unreadable.ProcessingStatus);
                Assert.Contains(unreadableLogger.Entries, entry => entry.Operation == "Restore STEP viewer settings" && entry.Exception is IOException);
            }

            File.Delete(lockedPath);
            Directory.CreateDirectory(lockedPath);
            var writeLogger = new CapturingLogger();
            using (var unwritable = new StepPreviewViewModel(writeLogger, new ControlledPreviewService()))
            {
                unwritable.AddUploadedModel(firstId, "first.step", "first.step");
                await WaitForAsync(() => !unwritable.IsProcessing && unwritable.IsModelLoaded);
                unwritable.Zoom(0.5);
                Assert.Equal("Viewer settings could not be saved.", unwritable.ProcessingStatus);
                Assert.Contains(writeLogger.Entries, entry => entry.Operation == "Save STEP viewer settings" && entry.Exception is IOException or UnauthorizedAccessException);
            }
        }
        finally
        {
            if (File.Exists(firstPath)) File.Delete(firstPath);
            if (File.Exists(secondPath)) File.Delete(secondPath);
            var firstDirectory = Path.Combine(root, $"{firstId:N}.json");
            if (Directory.Exists(firstDirectory)) Directory.Delete(firstDirectory);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class ControlledPreviewService : IStepPreviewService
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _called = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource> _released = new(StringComparer.Ordinal);
        public bool HoldLoads { get; set; }
        public HashSet<string> FailPaths { get; } = new(StringComparer.Ordinal);
        public bool IsDisposed { get; private set; }
        public StepPreviewMeshData? ModelMesh { get; private set; }

        public bool LoadModel(string path)
        {
            Task? wait = null;
            lock (_gate)
            {
                _called.Add(path);
                if (HoldLoads)
                {
                    if (!_released.TryGetValue(path, out var source)) _released[path] = source = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wait = source.Task;
                }
            }
            wait?.Wait(TimeSpan.FromSeconds(5));
            if (FailPaths.Contains(path)) return false;
            ModelMesh = new StepPreviewMeshData(
                new[] { new StepPreviewVertex(new StepPreviewPoint(0, 0, 0)), new StepPreviewVertex(new StepPreviewPoint(1, 0, 0)), new StepPreviewVertex(new StepPreviewPoint(0, 1, 0)) },
                new[] { new StepPreviewTriangle(0, 1, 2, 1) });
            return true;
        }

        public async Task WaitForCallAsync(string path)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                lock (_gate) if (_called.Contains(path)) return;
                await Task.Delay(10, timeout.Token);
            }
        }

        public void Release(string path)
        {
            lock (_gate) if (_released.TryGetValue(path, out var source)) source.TrySetResult();
        }

        public StepPreviewHighlight? ResolveHighlight(string? reference) => null;
        public void Dispose() => IsDisposed = true;
    }

    private sealed class CapturingLogger : ICentralizedLogger
    {
        public List<(Exception Exception, string Operation, string Context)> Entries { get; } = new();
        public void Log(Exception exception, string operation, string context) => Entries.Add((exception, operation, context));
    }
}
