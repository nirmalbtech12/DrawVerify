using DrawVerify.App.ViewModels;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class InternalSharedLinkUsageViewModelTests
{
    [Fact(DisplayName = "TSD-AC12 usage ViewModel displays tracking values and does not fabricate disabled count")]
    public async Task Usage_details_preserve_count_and_unavailable_state()
    {
        var details = new InternalSharedLinkDetails { Link = new InternalSharedLink { TrackingEnabled = true }, Status = SharedLinkStatus.Active, AccessCount = 2, LastAccessUtc = DateTime.UtcNow };
        var vm = new InternalSharedLinkViewModel(new ServiceFake(details));
        await vm.LoadAsync(Guid.NewGuid());
        Assert.Equal(2, vm.SelectedLink!.AccessCount);
        Assert.Null(new InternalSharedLinkDetails { Link = new InternalSharedLink { TrackingEnabled = false }, UsageUnavailable = true }.AccessCount);
    }

    private sealed class ServiceFake(InternalSharedLinkDetails details) : IInternalSharedLinkService
    {
        public Task<InternalSharedLinkDetails> GenerateAsync(Guid documentId, Guid versionId, InternalSharedLinkConfiguration configuration, CancellationToken cancellationToken = default) => Task.FromResult(details);
        public Task<InternalSharedLinkDetails> GetDetailsAsync(Guid linkId, CancellationToken cancellationToken = default) => Task.FromResult(details);
        public Task<IReadOnlyList<InternalSharedLinkDetails>> ListAsync(Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InternalSharedLinkDetails>>(new[] { details });
        public Task RevokeAsync(Guid linkId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<InternalSharedLinkDetails> RegenerateAsync(Guid linkId, InternalSharedLinkConfiguration? configuration = null, CancellationToken cancellationToken = default) => Task.FromResult(details);
        public Task<InternalSharedLinkAccessResult> AuthorizeAsync(InternalSharedLinkAccessRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new InternalSharedLinkAccessResult());
    }
}
