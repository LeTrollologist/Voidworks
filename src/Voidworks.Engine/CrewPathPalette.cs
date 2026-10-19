using Brutal.Numerics;

namespace Voidworks.Engine;

internal static class CrewPathPalette
{
    private static readonly uint[] DrawColors =
    {
        0xFF40D0FF,
        0xFF50E080,
        0xFFFFA040,
        0xFFFF60D0
    };

    private static readonly float4[] UiColors =
    {
        new float4(0.25f, 0.82f, 1.0f, 1.0f),
        new float4(0.31f, 0.88f, 0.50f, 1.0f),
        new float4(1.0f, 0.63f, 0.25f, 1.0f),
        new float4(1.0f, 0.38f, 0.82f, 1.0f)
    };

    private static readonly string[] ColorNames = { "cyan", "green", "orange", "pink" };

    public static uint GetDrawColor(int index) => DrawColors[index % DrawColors.Length];
    public static float4 GetUiColor(int index) => UiColors[index % UiColors.Length];
    public static string GetColorName(int index) => ColorNames[index % ColorNames.Length];
}
