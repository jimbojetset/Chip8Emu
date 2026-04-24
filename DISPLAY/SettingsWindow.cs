using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chip8Emu.CORE;
using ImGuiNET;

namespace Chip8Emu
{
    internal record RomQuirks(
        [property: JsonPropertyName("ShiftQuirk")] bool? ShiftQuirk,
        [property: JsonPropertyName("JumpQuirk")] bool? JumpQuirk,
        [property: JsonPropertyName("VFReset")] bool? VFReset,
        [property: JsonPropertyName("MemoryQuirk")] bool? MemoryQuirk,
        [property: JsonPropertyName("ClippingQuirk")] bool? ClippingQuirk,
        [property: JsonPropertyName("DisplayWaitQuirk")] bool? DisplayWaitQuirk,
        [property: JsonPropertyName("KeyReleaseWaitQuirk")] bool? KeyReleaseWaitQuirk
    );

    internal record RomEntry(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("quirks")] RomQuirks? Quirks = null
    );

    /// <summary>
    /// ImGui-based settings window for quirk toggles and ROM selection
    /// </summary>
    public class SettingsWindow
    {
        private readonly Chip8 _chip8;
        private readonly Action<string> _loadRomCallback;
        private readonly Action _requestRedraw;

        // UI layout/state
        private readonly bool _startCollapsed;
        private bool _isVisible = true;
        private bool _applyDefaultLayout = true;
        private bool _collapseOnNextDraw;

        // ROM metadata/state
        private RomEntry[] _romEntries = Array.Empty<RomEntry>();
        private int _selectedRomIndex = -1;
        private string _romsDirectory = "ROMS";
        private string _currentRomName = "";
        private string _currentRomPath = "";

        // Quirk state (synced with Chip8)
        private bool _shiftQuirk;

        // Speed state
        private int _cpuHz;
        private bool _jumpQuirk;
        private bool _vfReset;
        private bool _memoryQuirk;
        private bool _clippingQuirk;
        private bool _displayWaitQuirk;
        private bool _keyReleaseWaitQuirk;

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value)
                {
                    return;
                }

                _isVisible = value;
                _requestRedraw();
            }
        }

        public string CurrentRomTitle => _currentRomName;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public SettingsWindow(Chip8 chip8, Action<string> loadRomCallback, bool startCollapsed = UiLayoutDefaults.SettingsWindowStartsCollapsed, Action? requestRedraw = null)
        {
            _chip8 = chip8;
            _loadRomCallback = loadRomCallback;
            _startCollapsed = startCollapsed;
            _requestRedraw = requestRedraw ?? (() => { });

            // Initial sync from Chip8 state
            SyncFromChip8();

            // Scan for ROMs
            RefreshRomList();
        }

        public void SetCurrentRom(string romPath)
        {
            string romFileName = Path.GetFileName(romPath);
            RomEntry? match = _romEntries.FirstOrDefault(e =>
                string.Equals(e.File, romFileName, StringComparison.OrdinalIgnoreCase));

            _currentRomName = match?.Title ?? romFileName;

            if (File.Exists(romPath))
            {
                _currentRomPath = romPath;
            }
            else if (match != null)
            {
                _currentRomPath = Path.Combine(_romsDirectory, match.File);
            }
            else
            {
                _currentRomPath = "";
            }
        }

        private void SyncFromChip8()
        {
            _shiftQuirk = _chip8.ShiftQuirk;
            _jumpQuirk = _chip8.JumpQuirk;
            _vfReset = _chip8.VFReset;
            _memoryQuirk = _chip8.MemoryQuirk;
            _clippingQuirk = _chip8.ClippingQuirk;
            _displayWaitQuirk = _chip8.DisplayWaitQuirk;
            _keyReleaseWaitQuirk = _chip8.KeyReleaseWaitQuirk;
            _cpuHz = _chip8.CpuHz;
        }

        private void SyncToChip8()
        {
            _chip8.ShiftQuirk = _shiftQuirk;
            _chip8.JumpQuirk = _jumpQuirk;
            _chip8.VFReset = _vfReset;
            _chip8.MemoryQuirk = _memoryQuirk;
            _chip8.ClippingQuirk = _clippingQuirk;
            _chip8.DisplayWaitQuirk = _displayWaitQuirk;
            _chip8.KeyReleaseWaitQuirk = _keyReleaseWaitQuirk;
            _chip8.CpuHz = _cpuHz;
        }

        private void RefreshRomList()
        {
            string jsonPath = Path.Combine(_romsDirectory, "roms.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    var entries = JsonSerializer.Deserialize<RomEntry[]>(json);
                    _romEntries = (entries ?? Array.Empty<RomEntry>())
                        .Where(e => !string.IsNullOrEmpty(e.Title) && !string.IsNullOrEmpty(e.File))
                        .OrderBy(e => e.Title)
                        .ToArray();
                    return;
                }
                catch
                {
                    // Fall through to filesystem scan
                }
            }

            if (Directory.Exists(_romsDirectory))
            {
                _romEntries = Directory.GetFiles(_romsDirectory)
                    .Where(f => f.EndsWith(".ch8", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".rom", StringComparison.OrdinalIgnoreCase) ||
                                !Path.HasExtension(f) ||
                                Path.GetExtension(f).Length <= 4)
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .Cast<string>()
                    .OrderBy(f => f)
                    .Select(f => new RomEntry(f, f, ""))
                    .ToArray();
            }
            else
            {
                _romEntries = Array.Empty<RomEntry>();
            }
        }

        private void SynchronizeRomMetadata()
        {
            if (!Directory.Exists(_romsDirectory))
            {
                _romEntries = Array.Empty<RomEntry>();
                return;
            }

            string jsonPath = Path.Combine(_romsDirectory, "roms.json");
            RomEntry[] existingEntries = Array.Empty<RomEntry>();

            if (File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    existingEntries = JsonSerializer.Deserialize<RomEntry[]>(json) ?? Array.Empty<RomEntry>();
                }
                catch
                {
                    existingEntries = Array.Empty<RomEntry>();
                }
            }

            var romFiles = Directory.GetFiles(_romsDirectory)
                .Where(f => f.EndsWith(".ch8", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".rom", StringComparison.OrdinalIgnoreCase) ||
                            !Path.HasExtension(f) ||
                            Path.GetExtension(f).Length <= 4)
                .Select(Path.GetFileName)
                .Where(f => !string.IsNullOrEmpty(f))
                .Cast<string>()
                .ToList();

            var romFileSet = new HashSet<string>(romFiles, StringComparer.OrdinalIgnoreCase);
            var synchronizedEntries = existingEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.File) && romFileSet.Contains(e.File))
                .ToList();

            var existingFileSet = new HashSet<string>(synchronizedEntries.Select(e => e.File), StringComparer.OrdinalIgnoreCase);
            foreach (string romFile in romFiles)
            {
                if (existingFileSet.Contains(romFile))
                    continue;

                synchronizedEntries.Add(new RomEntry(
                    romFile,
                    romFile,
                    romFile,
                    new RomQuirks(
                        ShiftQuirk: false,
                        JumpQuirk: false,
                        VFReset: true,
                        MemoryQuirk: false,
                        ClippingQuirk: false,
                        DisplayWaitQuirk: true,
                        KeyReleaseWaitQuirk: true)));
            }

            string updatedJson = JsonSerializer.Serialize(synchronizedEntries, JsonOptions);
            File.WriteAllText(jsonPath, updatedJson);

            _romEntries = synchronizedEntries
                .Where(e => !string.IsNullOrEmpty(e.Title) && !string.IsNullOrEmpty(e.File))
                .OrderBy(e => e.Title)
                .ToArray();
        }

        public void Draw()
        {
            if (!IsVisible) return;

            if (_applyDefaultLayout)
            {
                ImGui.SetNextWindowSize(UiLayoutDefaults.SettingsWindowSize, ImGuiCond.Always);
                ImGui.SetNextWindowPos(UiLayoutDefaults.SettingsWindowPosition, ImGuiCond.Always);
                ImGui.SetNextWindowCollapsed(_startCollapsed, ImGuiCond.Always);
                _applyDefaultLayout = false;
            }

            if (_collapseOnNextDraw)
            {
                ImGui.SetNextWindowCollapsed(true, ImGuiCond.Always);
                _collapseOnNextDraw = false;
            }

            ImGui.SetNextWindowBgAlpha(UiLayoutDefaults.SettingsWindowBackgroundAlpha);

            bool showContents = ImGui.Begin(UiLayoutDefaults.SettingsWindowTitle, ref _isVisible, ImGuiWindowFlags.NoResize);
            bool isCollapsed = ImGui.IsWindowCollapsed();
            Vector2 windowPos = ImGui.GetWindowPos();
            Vector2 windowSize = ImGui.GetWindowSize();

            if (!isCollapsed && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                bool clickedInsideSettings = ImGui.IsWindowHovered(
                    ImGuiHoveredFlags.RootAndChildWindows |
                    ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
                if (!clickedInsideSettings)
                {
                    _collapseOnNextDraw = true;
                    _requestRedraw();
                }
            }

            if (showContents)
            {
                if (ImGui.BeginTabBar("SettingsTabs"))
                {
                    if (ImGui.BeginTabItem("ROMs"))
                    {
                        DrawRomSection();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Keyboard"))
                    {
                        DrawKeyboardSection();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Quirks"))
                    {
                        DrawQuirksSection();
                        ImGui.EndTabItem();
                    }

                    bool speedTabDisabled = _displayWaitQuirk;
                    if (speedTabDisabled)
                    {
                        ImGui.BeginDisabled();
                    }
                    if (ImGui.BeginTabItem("Speed"))
                    {
                        DrawSpeedSection();
                        ImGui.EndTabItem();
                    }
                    if (speedTabDisabled)
                    {
                        ImGui.EndDisabled();
                    }

                    ImGui.EndTabBar();
                }
            }
            ImGui.End();

            if (isCollapsed)
            {
                DrawCollapsedSettingsBar(windowPos, windowSize);
            }
        }

        private void DrawCollapsedSettingsBar(Vector2 collapsedWindowPos, Vector2 collapsedWindowSize)
        {
            ImGui.SetNextWindowPos(new Vector2(collapsedWindowPos.X, collapsedWindowPos.Y + collapsedWindowSize.Y + 4f), ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(UiLayoutDefaults.SettingsWindowBackgroundAlpha);

            Vector2 buttonSize = ImGui.CalcTextSize("Reload");
            buttonSize.X += ImGui.GetStyle().FramePadding.X * 2f;
            buttonSize.Y += ImGui.GetStyle().FramePadding.Y * 2f;

            ImGui.SetNextWindowSize(buttonSize, ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

            ImGuiWindowFlags flags =
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav;

            if (ImGui.Begin("##SettingsCollapsedBar", flags))
            {
                bool canReload = !string.IsNullOrWhiteSpace(_currentRomPath);
                if (!canReload)
                {
                    ImGui.BeginDisabled();
                }

                if (ImGui.Button("Reload"))
                {
                    _loadRomCallback(_currentRomPath);
                }

                if (!canReload)
                {
                    ImGui.EndDisabled();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(canReload ? "Reload current ROM" : "No ROM loaded");
                }
            }
            ImGui.End();
            ImGui.PopStyleVar(2);
        }

        private void DrawRomSection()
        {
            if (ImGui.Button("Refresh"))
            {
                SynchronizeRomMetadata();
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"({_romEntries.Length})");

            // Scrollable list box - use remaining height
            float listHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
            if (ImGui.BeginListBox("##RomList", new Vector2(-1, listHeight)))
            {
                if (_romEntries.Length == 0)
                {
                    ImGui.TextDisabled("No ROMs found");
                    ImGui.TextDisabled($"Add .ch8 files to:");
                    ImGui.TextDisabled($"  {_romsDirectory}/");
                }
                else
                {
                    for (int i = 0; i < _romEntries.Length; i++)
                    {
                        bool isSelected = _selectedRomIndex == i;
                        if (ImGui.Selectable(_romEntries[i].Title, isSelected))
                        {
                            _selectedRomIndex = i;
                        }

                        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(_romEntries[i].Description))
                        {
                            ImGui.BeginTooltip();
                            ImGui.PushTextWrapPos(500f);
                            ImGui.TextUnformatted(_romEntries[i].Description.Replace("<br/>", "\n").Replace("<br />", "\n"));
                            ImGui.PopTextWrapPos();
                            ImGui.EndTooltip();
                        }

                        // Double-click to load
                        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            LoadSelectedRom();
                        }
                    }
                }
                ImGui.EndListBox();
            }

            // Load button
            bool canLoad = _selectedRomIndex >= 0 && _selectedRomIndex < _romEntries.Length;
            if (!canLoad)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Load ROM", new Vector2(-1, 0)))
            {
                LoadSelectedRom();
            }

            if (!canLoad)
            {
                ImGui.EndDisabled();
            }
        }

        private void LoadSelectedRom()
        {
            if (_selectedRomIndex >= 0 && _selectedRomIndex < _romEntries.Length)
            {
                RomEntry entry = _romEntries[_selectedRomIndex];
                string romPath = Path.Combine(_romsDirectory, entry.File);
                _currentRomName = entry.Title;
                _currentRomPath = romPath;

                if (entry.Quirks != null)
                {
                    if (entry.Quirks.ShiftQuirk.HasValue) _shiftQuirk = entry.Quirks.ShiftQuirk.Value;
                    if (entry.Quirks.JumpQuirk.HasValue) _jumpQuirk = entry.Quirks.JumpQuirk.Value;
                    if (entry.Quirks.VFReset.HasValue) _vfReset = entry.Quirks.VFReset.Value;
                    if (entry.Quirks.MemoryQuirk.HasValue) _memoryQuirk = entry.Quirks.MemoryQuirk.Value;
                    if (entry.Quirks.ClippingQuirk.HasValue) _clippingQuirk = entry.Quirks.ClippingQuirk.Value;
                    if (entry.Quirks.DisplayWaitQuirk.HasValue) _displayWaitQuirk = entry.Quirks.DisplayWaitQuirk.Value;
                    if (entry.Quirks.KeyReleaseWaitQuirk.HasValue) _keyReleaseWaitQuirk = entry.Quirks.KeyReleaseWaitQuirk.Value;
                }
                else
                {
                    // No ROM-specific quirks provided: reset to COSMAC VIP defaults.
                    _shiftQuirk = false;
                    _jumpQuirk = false;
                    _vfReset = true;
                    _memoryQuirk = false;
                    _clippingQuirk = false;
                    _displayWaitQuirk = true;
                    _keyReleaseWaitQuirk = true;
                }

                SyncToChip8();

                _loadRomCallback(romPath);
                _collapseOnNextDraw = true;
                _requestRedraw();
            }
        }

        private void DrawQuirksSection()
        {
            bool changed = false;

            // Quirk checkboxes - compact layout
            changed |= ImGui.Checkbox("Shift", ref _shiftQuirk);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("8XY6/8XYE: Shift VX directly (CHIP-48/SCHIP)");

            changed |= ImGui.Checkbox("Jump", ref _jumpQuirk);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("BNNN: Use VX instead of V0 (CHIP-48)");

            changed |= ImGui.Checkbox("VF Reset", ref _vfReset);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("8XY1/2/3: Reset VF after logic ops (VIP)");

            changed |= ImGui.Checkbox("Memory", ref _memoryQuirk);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("FX55/65: Don't modify I (SCHIP)");

            changed |= ImGui.Checkbox("Clipping", ref _clippingQuirk);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clip sprites at screen edges");

            changed |= ImGui.Checkbox("Display Wait", ref _displayWaitQuirk);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Wait for VBlank after draw (VIP)");

            changed |= ImGui.Checkbox("Key Wait Release", ref _keyReleaseWaitQuirk);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("FX0A waits for key press and release instead of press only (VIP)");

            if (changed) SyncToChip8();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Preset buttons - compact
            if (ImGui.Button("VIP", new Vector2(60, 0)))
            {
                _shiftQuirk = false; _jumpQuirk = false; _vfReset = true;
                _memoryQuirk = false; _clippingQuirk = false; _displayWaitQuirk = true;
                _keyReleaseWaitQuirk = true;
                SyncToChip8();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("COSMAC VIP settings");

            ImGui.SameLine();
            if (ImGui.Button("SCHIP", new Vector2(60, 0)))
            {
                _shiftQuirk = true; _jumpQuirk = true; _vfReset = false;
                _memoryQuirk = true; _clippingQuirk = false; _displayWaitQuirk = false;
                _keyReleaseWaitQuirk = false;
                SyncToChip8();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("SUPER-CHIP settings");

            ImGui.SameLine();
            if (ImGui.Button("Off", new Vector2(40, 0)))
            {
                _shiftQuirk = false; _jumpQuirk = false; _vfReset = false;
                _memoryQuirk = false; _clippingQuirk = false; _displayWaitQuirk = false;
                _keyReleaseWaitQuirk = false;
                SyncToChip8();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("All quirks off");
        }

        private void DrawSpeedSection()
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextDisabled("Cycles per frame (only available when Display Wait is off)");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            if (ImGui.SliderInt("##CpuHz", ref _cpuHz, 1000, 100000, "%d cycles/frame"))
            {
                _chip8.CpuHz = _cpuHz;
            }

            ImGui.Spacing();
            ImGui.TextDisabled($"~{_cpuHz / 1000}k cycles/s at 60fps");
        }

        private void DrawKeyboardSection()
        {
            ImGui.TextDisabled("CHIP-8 keypad to keyboard mapping");
            ImGui.Spacing();

            string[] keyLayoutRows =
            {
                "1  2  3  C   ->   1  2  3  4",
                "4  5  6  D   ->   Q  W  E  R",
                "7  8  9  E   ->   A  S  D  F",
                "A  0  B  F   ->   Z  X  C  V"
            };

            float maxRowWidth = 1f;
            for (int i = 0; i < keyLayoutRows.Length; i++)
            {
                float rowWidth = ImGui.CalcTextSize(keyLayoutRows[i]).X;
                if (rowWidth > maxRowWidth)
                {
                    maxRowWidth = rowWidth;
                }
            }

            float availableWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - 4f);
            float keyScale = MathF.Min(3f, MathF.Max(1f, availableWidth / maxRowWidth));

            ImGui.SetWindowFontScale(keyScale);
            for (int i = 0; i < keyLayoutRows.Length; i++)
            {
                ImGui.TextUnformatted(keyLayoutRows[i]);
            }
            ImGui.SetWindowFontScale(1f);

            ImGui.Spacing();
            ImGui.TextDisabled("Press ESC to close the emulator.");
            ImGui.TextDisabled("Press F1 to toggle settings visibility.");
        }
    }
}
