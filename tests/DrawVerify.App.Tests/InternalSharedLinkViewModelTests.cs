using DrawVerify.App.ViewModels;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class InternalSharedLinkViewModelTests
{
    [Fact(DisplayName = "TSD-AC03 TSD-AC04 TSD-AC13 TSD-AC14 ViewModel exposes configuration and selected details")]
    public async Task View_model_loads_details_and_gates_copy_to_active_links()
    {
        var link = new InternalSharedLink { Url = "https://drawverify.internal/share?token=opaque", IsActive = true };
        var vm = new InternalSharedLinkViewModel(new ServiceFake(new InternalSharedLinkDetails { Link = link, Status = SharedLinkStatus.Active }));
        await vm.LoadAsync(Guid.NewGuid());
        Assert.Equal(link.Url, vm.SelectedLink!.FullUrl);
        Assert.True(vm.CanCopyLink);
        vm.AccessLevel = SharedLinkAccessLevel.ViewOnly;
        vm.ActivateImmediately = false;
        vm.ExpiresUtc = DateTime.UtcNow.AddHours(1);
        Assert.False(vm.ActivateImmediately);
        Assert.Equal(SharedLinkAccessLevel.ViewOnly, vm.AccessLevel);
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
