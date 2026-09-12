using DrawVerify.Geometry.Models;
using Xunit;

namespace DrawVerify.OcctService.Tests;

public sealed class Kan125ProjectionDrawingTests
{
    [Fact(DisplayName = "TD-03 supports all six projection views and finite managed entities")]
    public void Projection_contract_covers_supported_views_and_entity_mapping()
    {
        Assert.Equal(new[] { "Front", "Rear", "Top", "Bottom", "Left", "Right" },
            Enum.GetNames<ProjectionDrawingView>());
        var entity = new ProjectionDrawingEntity(ProjectionDrawingEntityType.Line, [0, 1, 2, 3], false);
        Assert.All(entity.Coordinates, value => Assert.True(double.IsFinite(value)));
        Assert.Equal(4, entity.Coordinates.Count);
    }

    [Fact(DisplayName = "TD-03 rejects invalid projection views")]
    public void Invalid_view_is_not_part_of_the_managed_contract()
        => Assert.False(Enum.IsDefined((ProjectionDrawingView)99));
}
