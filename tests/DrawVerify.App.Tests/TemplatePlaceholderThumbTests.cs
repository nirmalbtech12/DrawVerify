using DrawVerify.App.Controls;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class TemplatePlaceholderThumbTests
{
    [Fact(DisplayName = "TSD-04 placeholder thumb exposes its bound placeholder")]
    public void Thumb_binds_placeholder()
    {
        Sta.Run(() =>
        {
            var placeholder = new TemplatePlaceholder { Type = TemplatePlaceholderType.ProjectName, Label = "Project" };
            var thumb = new TemplatePlaceholderThumb { Placeholder = placeholder };
            Assert.Same(placeholder, thumb.Placeholder);
        });
    }
}
