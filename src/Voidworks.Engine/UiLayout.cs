using System;
using Brutal.Numerics;

namespace Voidworks.Engine;

/// <summary>Screen-space rectangle used by the fixed workstation panels.</summary>
public readonly record struct LayoutRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public float2 Position => new(X, Y);
    public float2 Size => new(Width, Height);

    public bool Contains(float2 point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;
}

/// <summary>
/// Shared layout rules for the always-visible telemetry bar, toolbar, and inspector.
/// Keeping the rectangles in one place makes resize behavior deterministic and lets
/// tests verify that panels never overlap the world input area by accident.
/// </summary>
public static class WorkstationLayout
{
    public const float Margin = 10f;
    public const float Gap = 10f;
    public const float TopBarHeight = 85f;
    public const float ToolbarHeight = 78f;
    public const float MinInspectorWidth = 320f;
    public const float MaxInspectorWidth = 420f;
    public const float MinTopBarWidth = 320f;

    public static float InspectorWidth(float2 displaySize) =>
        Math.Clamp(displaySize.X * 0.27f, MinInspectorWidth, MaxInspectorWidth);

    public static LayoutRect TopBar(float2 displaySize)
    {
        float width = MainColumnWidth(displaySize);
        return new LayoutRect(Margin, Margin, width, TopBarHeight);
    }

    public static LayoutRect Toolbar(float2 displaySize) =>
        new(Margin, Margin + TopBarHeight + Gap, MainColumnWidth(displaySize), ToolbarHeight);

    public static LayoutRect Inspector(float2 displaySize) =>
        new(
            Math.Max(Margin, displaySize.X - InspectorWidth(displaySize) - Margin),
            Margin,
            InspectorWidth(displaySize),
            Math.Max(220f, displaySize.Y - Margin * 2f));

    private static float MainColumnWidth(float2 displaySize)
    {
        float width = displaySize.X - InspectorWidth(displaySize) - Margin * 3f;
        return Math.Max(MinTopBarWidth, width);
    }
}

/// <summary>
/// Converts raw window cursor coordinates into ImGui display coordinates. GLFW reports
/// screen coordinates while ImGui may use a different display size on high-DPI
/// displays, so the ratio has to be applied consistently for picking and zooming.
/// </summary>
public static class DisplayMetrics
{
    public static float2 Normalize(float2 displaySize, float2 windowSize)
    {
        if (displaySize.X <= 0f || displaySize.Y <= 0f)
            return new float2(1f, 1f);
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
            return new float2(1f, 1f);

        return new float2(displaySize.X / windowSize.X, displaySize.Y / windowSize.Y);
    }

    public static float2 CursorToDisplay(float2 cursor, float2 displaySize, float2 windowSize)
    {
        float2 scale = Normalize(displaySize, windowSize);
        return new float2(cursor.X * scale.X, cursor.Y * scale.Y);
    }

    public static float2 ViewportCenter(float2 displaySize) => displaySize * 0.5f;
}

/// <summary>
/// Single source of truth for whether pointer/keyboard input may reach the world.
/// UI capture is sampled once per frame so a panel click cannot also select or drag
/// the ship, including when a panel appears under an already-pressed mouse button.
/// </summary>
public static class WorldInputPolicy
{
    public static bool CanControlWorld(bool uiWantsMouse, bool popupOpen, bool modalOpen)
        => !uiWantsMouse && !popupOpen && !modalOpen;

    public static bool CanControlWorldKeyboard(bool uiWantsKeyboard, bool modalOpen)
        => !uiWantsKeyboard && !modalOpen;

    public static bool CanStartPan(bool uiWantsMouse, bool modalOpen, bool alreadyPanning)
        => !modalOpen && (!uiWantsMouse || alreadyPanning);
}
