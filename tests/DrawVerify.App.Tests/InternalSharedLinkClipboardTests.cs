using DrawVerify.App.ViewModels;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class InternalSharedLinkClipboardTests
{
    [Fact(DisplayName = "TSD-AC05 copy command is enabled only for an active full URL")]
    public async Task Copy_is_gated_by_active_status()
    {
        var details = new InternalSharedLinkDetails { Link = new InternalSharedLink { Url = "https://drawverify.internal/share?token=full-value" }, Status = SharedLinkStatus.Inactive };
        var vm = new InternalSharedLinkViewModel(new ServiceFake(details));
        await vm.LoadAsync(Guid.NewGuid());
        Assert.False(vm.CanCopyLink);
        Assert.False(vm.CopyLinkCommand.CanExecute(null));
    }

    [Fact(DisplayName = "TSD-AC05 active link exposes the exact full URL for copying")]
    public async Task Active_link_exposes_full_url()
    {
        const string url = "https://drawverify.internal/share?token=full-value";
        var details = new InternalSharedLinkDetails { Link = new InternalSharedLink { Url = url }, Status = SharedLinkStatus.Active };
        var vm = new InternalSharedLinkViewModel(new ServiceFake(details));
        await vm.LoadAsync(Guid.NewGuid());

        Assert.True(vm.CanCopyLink);
        Assert.Equal(url, vm.SelectedLink!.FullUrl);
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
