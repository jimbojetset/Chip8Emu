using System.Numerics;

namespace Chip8Emu
{
    /// <summary>
    /// Centralized defaults for Dear ImGui window layout and appearance.
    /// </summary>
    public static class UiLayoutDefaults
    {
        public const string SettingsWindowTitle = "Settings [F1]";
        public static readonly Vector2 SettingsWindowSize = new(280f, 310f);
        public static readonly Vector2 SettingsWindowPosition = Vector2.Zero;
        public const bool SettingsWindowStartsCollapsed = true;
        public const float SettingsWindowBackgroundAlpha = 0.85f;
    }
}
