using Brutal.Numerics;
using Voidworks.Engine;
using Xunit;

namespace Voidworks.Tests;

public class UiLayoutAndInputTests
{
    [Theory]
    [InlineData(1280f, 720f)]
    [InlineData(1600f, 900f)]
    [InlineData(2560f, 1440f)]
    public void FixedPanels_DoNotOverlapAtCommonResolutions(float width, float height)
    {
        var display = new float2(width, height);
        LayoutRect topBar = WorkstationLayout.TopBar(display);
        LayoutRect toolbar = WorkstationLayout.Toolbar(display);
        LayoutRect inspector = WorkstationLayout.Inspector(display);

        Assert.True(topBar.Right <= inspector.X, "Telemetry bar overlaps the inspector.");
        Assert.True(toolbar.Right <= inspector.X, "Toolbar overlaps the inspector.");
        Assert.True(toolbar.Y >= topBar.Bottom, "Toolbar overlaps the telemetry bar.");
        Assert.True(inspector.Bottom <= height, "Inspector extends past the display.");
        Assert.True(inspector.Right <= width, "Inspector extends past the display.");
    }

    [Fact]
    public void InspectorWidth_StaysWithinReadableBounds()
    {
        Assert.Equal(WorkstationLayout.MinInspectorWidth,
            WorkstationLayout.InspectorWidth(new float2(800f, 600f)));
        Assert.Equal(WorkstationLayout.MaxInspectorWidth,
            WorkstationLayout.InspectorWidth(new float2(3000f, 1600f)));
    }

    [Fact]
    public void CursorToDisplay_MatchesIdentityAtNormalDpi()
    {
        var cursor = new float2(640f, 360f);
        var display = new float2(1600f, 900f);

        float2 converted = DisplayMetrics.CursorToDisplay(cursor, display, display);

        Assert.Equal(cursor.X, converted.X, precision: 4);
        Assert.Equal(cursor.Y, converted.Y, precision: 4);
    }

    [Fact]
    public void CursorToDisplay_ScalesForHighDpiDisplays()
    {
        var cursor = new float2(320f, 180f);

        float2 converted = DisplayMetrics.CursorToDisplay(
            cursor,
            new float2(1920f, 1080f),
            new float2(1280f, 720f));

        Assert.Equal(480f, converted.X, precision: 4);
        Assert.Equal(270f, converted.Y, precision: 4);
    }

    [Fact]
    public void CursorToDisplay_FallsBackWhenWindowSizeIsUnavailable()
    {
        var cursor = new float2(100f, 50f);
        float2 converted = DisplayMetrics.CursorToDisplay(cursor, new float2(1600f, 900f), float2.Zero);

        Assert.Equal(cursor.X, converted.X, precision: 4);
        Assert.Equal(cursor.Y, converted.Y, precision: 4);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void WorldInputPolicy_RequiresUiCaptureAndModalChecks(
        bool uiCapture,
        bool popupOpen,
        bool modalOpen,
        bool expected)
    {
        Assert.Equal(expected, WorldInputPolicy.CanControlWorld(uiCapture, popupOpen, modalOpen));
    }

    [Fact]
    public void CanStartPan_AllowsAnInProgressDragEvenWhenCaptureChanges()
    {
        Assert.True(WorldInputPolicy.CanStartPan(uiWantsMouse: true, modalOpen: false, alreadyPanning: true));
        Assert.False(WorldInputPolicy.CanStartPan(uiWantsMouse: true, modalOpen: false, alreadyPanning: false));
        Assert.False(WorldInputPolicy.CanStartPan(uiWantsMouse: false, modalOpen: true, alreadyPanning: true));
    }
}
