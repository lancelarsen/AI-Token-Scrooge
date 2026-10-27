using AIResetRadar.App.Presentation;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class AccountDragLayoutTests
{
    [Fact]
    public void SmallMovementLeavesTheOriginalGapStable()
    {
        var layout = new AccountDragLayout([200, 100, 200], 0, 20);
        Assert.False(layout.Move(110));
        Assert.Equal(0, layout.Slot);
        Assert.Equal(new double[] { 0, 220, 340 }, layout.Tops);
    }

    [Fact]
    public void DifferentHeightCardsOpenExactlyTheDraggedCardsSpace()
    {
        var layout = new AccountDragLayout([200, 100, 200], 0, 20);
        Assert.True(layout.Move(280));
        Assert.Equal(1, layout.Slot);
        Assert.Equal(new double[] { 120, 0, 340 }, layout.Tops);
        Assert.False(layout.Move(275)); // Avoid flicker when the pointer stays near the crossed boundary.
        Assert.True(layout.Move(40));
        Assert.Equal(layout.OriginalTops, layout.Tops);
    }

    [Fact]
    public void CanMoveAcrossMultipleCardsAndBackToTheOriginalSlot()
    {
        var layout = new AccountDragLayout([100, 200, 100, 100], 2, 20);
        layout.Move(-100); Assert.Equal(0, layout.Slot);
        layout.Move(10000); Assert.Equal(3, layout.Slot);
        Assert.Equal(new double[] { 0, 120, 460, 340 }, layout.Tops);
    }

    [Fact]
    public void EdgeScrollingIsBoundedAndStopsOutsideTheViewport()
    {
        Assert.Equal(0, AccountDragLayout.ScrollStep(300, 100, 500));
        Assert.Equal(0, AccountDragLayout.ScrollStep(90, 100, 500));
        Assert.Equal(0, AccountDragLayout.ScrollStep(510, 100, 500));
        Assert.Equal(-10, AccountDragLayout.ScrollStep(100, 100, 500));
        Assert.Equal(10, AccountDragLayout.ScrollStep(500, 100, 500));
        Assert.InRange(AccountDragLayout.ScrollStep(480, 100, 500), 0, 10);
    }
}
