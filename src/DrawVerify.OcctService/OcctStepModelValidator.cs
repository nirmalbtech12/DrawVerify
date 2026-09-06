using DrawVerify.Occt;
using DrawVerify.Geometry;

namespace DrawVerify.OcctService;

/// <summary>Validates uploaded STEP streams by loading them through OCCT.</summary>
public sealed class OcctStepModelValidator : IStepModelValidator
{
    private readonly IOcctModelLoader _loader;

    public OcctStepModelValidator(IOcctModelLoader? loader = null) => _loader = loader ?? new OcctModelLoader();

    public async Task<StepModelValidationResult> ValidateAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead || !content.CanSeek)
            return new StepModelValidationResult(false, "UnreadableStream", "The uploaded stream cannot be read safely.");

        var originalPosition = content.Position;
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-{Guid.NewGuid():N}.step");
        try
        {
            content.Position = 0;
            await using (var temporaryFile = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(temporaryFile, cancellationToken).ConfigureAwait(false);
                await temporaryFile.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var model = _loader.LoadStep(path);
            return new StepModelValidationResult(true);
        }
        catch (OcctOperationException exception)
        {
            return new StepModelValidationResult(false, exception.ErrorCode, exception.Message);
        }
        finally
        {
            content.Position = originalPosition;
            File.Delete(path);
        }
    }
}
