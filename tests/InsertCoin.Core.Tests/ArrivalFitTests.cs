using InsertCoin.Core;
using Xunit;

namespace InsertCoin.Core.Tests;

public class ArrivalFitTests
{
    // The two built-in characters, as Rig measures them.
    [Theory]
    [InlineData(1.672f, 1.134f, 0.119f, 0.972f, 0.296f)]    // a tall figure, his head well above the cabinet's top
    [InlineData(1.417f, 0.935f, 0.104f, 0.792f, 0.217f)]    // a smaller one
    public void A_figure_of_a_persons_shape_can_arrive(float head, float hips, float ankle, float leg, float shoulders) =>
        Assert.True(ArrivalFit.Fits(head, hips, ankle, leg, shoulders));

    [Theory]
    [InlineData(1.0f, 0.3f, 0.05f, 0.25f, 0.3f)]            // a big head on short legs
    [InlineData(1.6f, 1.0f, 0.75f, 0.8f, 0.3f)]             // not standing on its origin: its ankles far from the floor
    [InlineData(2.6f, 1.6f, 0.1f, 1.4f, 0.4f)]              // a giant: its knees can't keep its head out of sight
    [InlineData(1.5f, 0.95f, 0.1f, 0.8f, 0.7f)]             // shoulders wider than the gap they stay behind
    [InlineData(1.5f, 1.45f, 0.1f, 0.8f, 0.3f)]             // nothing between its hips and its head
    public void A_figure_too_odd_to_pose_appears_instead(float head, float hips, float ankle, float leg, float shoulders) =>
        Assert.False(ArrivalFit.Fits(head, hips, ankle, leg, shoulders));
}
