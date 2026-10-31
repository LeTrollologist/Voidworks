using System.Text;
using Brutal.ImGuiApi;

namespace Voidworks.Engine;

/// <summary>
/// Creates ImString values directly from UTF-8 bytes. BRUTAL's implicit string
/// conversion uses a shared arena; long status or guidance text can exhaust it and
/// throw "Cannot grow beyond MaxCapacity" mid-frame. Explicit UTF-8 construction
/// avoids that path for dynamic, player-facing text.
/// </summary>
public static class UiText
{
    public static ImString Im(string value) => new(Encoding.UTF8.GetBytes(value));

    /// <summary>
    /// Releases the shared ImString text arena. BRUTAL does not reset it between
    /// frames; without this call, UI text accumulates until a frame throws
    /// "Cannot grow beyond MaxCapacity". Call after the previous frame has been
    /// rendered and swapped, when no ImString from that frame is still needed.
    /// </summary>
    public static void ResetFrameArena() => ImString.SharedStorage.Reset();
}
