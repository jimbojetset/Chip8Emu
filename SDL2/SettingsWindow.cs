using System.Numerics;
using ImGuiNET;

namespace Chip8Emu
{
    /// <summary>
    /// ImGui-based settings window for quirk toggles and ROM selection
    /// </summary>
    public class SettingsWindow
    {
        private readonly Chip8 _chip8;
        private readonly Action<string> _loadRomCallback;

        private string[] _romFiles = Array.Empty<string>();
        private int _selectedRomIndex = -1;
        private string _romsDirectory = "ROMS";
        private string _currentRomName = "";
        private bool _applyDefaultLayout = true;

        // Quirk state (synced with Chip8)
        private bool _shiftQuirk;
        private bool _jumpQuirk;
        private bool _vfReset;
        private bool _memoryQuirk;
        private bool _clippingQuirk;
        private bool _displayWaitQuirk;

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => _isVisible = value;
        }

        public SettingsWindow(Chip8 chip8, Action<string> loadRomCallback)
        {
            _chip8 = chip8;
            _loadRomCallback = loadRomCallback;

            // Initial sync from Chip8 state
            SyncFromChip8();

            // Scan for ROMs
            RefreshRomList();
        }

        public void SetCurrentRom(string romPath)
        {
            _currentRomName = Path.GetFileName(romPath);
        }

        private void SyncFromChip8()
        {
            _shiftQuirk = _chip8.ShiftQuirk;
            _jumpQuirk = _chip8.JumpQuirk;
            _vfReset = _chip8.VFReset;
            _memoryQuirk = _chip8.MemoryQuirk;
            _clippingQuirk = _chip8.ClippingQuirk;
            _displayWaitQuirk = _chip8.DisplayWaitQuirk;
        }

        private void SyncToChip8()
        {
            _chip8.ShiftQuirk = _shiftQuirk;
            _chip8.JumpQuirk = _jumpQuirk;
            _chip8.VFReset = _vfReset;
            _chip8.MemoryQuirk = _memoryQuirk;
            _chip8.ClippingQuirk = _clippingQuirk;
            _chip8.DisplayWaitQuirk = _displayWaitQuirk;
        }

        private void RefreshRomList()
        {
            if (Directory.Exists(_romsDirectory))
            {
                _romFiles = Directory.GetFiles(_romsDirectory)
                    .Where(f => f.EndsWith(".ch8", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".rom", StringComparison.OrdinalIgnoreCase) ||
                                !Path.HasExtension(f) ||
                                Path.GetExtension(f).Length <= 4)
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .Cast<string>()
                    .OrderBy(f => f)
                    .ToArray();
            }
            else
            {
                _romFiles = Array.Empty<string>();
            }
        }

        public void Draw()
        {
            if (!IsVisible) return;

            if (_applyDefaultLayout)
            {
                ImGui.SetNextWindowSize(UiLayoutDefaults.SettingsWindowSize, ImGuiCond.Always);
                ImGui.SetNextWindowPos(UiLayoutDefaults.SettingsWindowPosition, ImGuiCond.Always);
                ImGui.SetNextWindowCollapsed(UiLayoutDefaults.SettingsWindowStartsCollapsed, ImGuiCond.Always);
                _applyDefaultLayout = false;
            }

            ImGui.SetNextWindowBgAlpha(UiLayoutDefaults.SettingsWindowBackgroundAlpha);

            if (ImGui.Begin(UiLayoutDefaults.SettingsWindowTitle, ref _isVisible))
            {
                if (ImGui.BeginTabBar("SettingsTabs"))
                {
                    if (ImGui.BeginTabItem("ROMs"))
                    {
                        DrawRomSection();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Quirks"))
                    {
                        DrawQuirksSection();
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
            }
            ImGui.End();
        }

        private void DrawRomSection()
        {
            // Current ROM display
            ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), _currentRomName.Length > 0 ? _currentRomName : "No ROM loaded");

            if (ImGui.Button("Refresh"))
            {
                RefreshRomList();
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"({_romFiles.Length})");

            // Scrollable list box - use remaining height
            float listHeight = ImGui.GetContentRegionAvail().Y - 25;
            if (ImGui.BeginListBox("##RomList", new Vector2(-1, listHeight)))
            {
                if (_romFiles.Length == 0)
                {
                    ImGui.TextDisabled("No ROMs found");
                    ImGui.TextDisabled($"Add .ch8 files to:");
                    ImGui.TextDisabled($"  {_romsDirectory}/");
                }
                else
                {
                    for (int i = 0; i < _romFiles.Length; i++)
                    {
                        bool isSelected = _selectedRomIndex == i;
                        if (ImGui.Selectable(_romFiles[i], isSelected))
                        {
                            _selectedRomIndex = i;
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
            bool canLoad = _selectedRomIndex >= 0 && _selectedRomIndex < _romFiles.Length;
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
            if (_selectedRomIndex >= 0 && _selectedRomIndex < _romFiles.Length)
            {
                string romPath = Path.Combine(_romsDirectory, _romFiles[_selectedRomIndex]);
                _currentRomName = _romFiles[_selectedRomIndex];
                _loadRomCallback(romPath);
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

            if (changed) SyncToChip8();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Preset buttons - compact
            if (ImGui.Button("VIP", new Vector2(60, 0)))
            {
                _shiftQuirk = false; _jumpQuirk = false; _vfReset = true;
                _memoryQuirk = false; _clippingQuirk = false; _displayWaitQuirk = true;
                SyncToChip8();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("COSMAC VIP settings");

            ImGui.SameLine();
            if (ImGui.Button("SCHIP", new Vector2(60, 0)))
            {
                _shiftQuirk = true; _jumpQuirk = true; _vfReset = false;
                _memoryQuirk = true; _clippingQuirk = false; _displayWaitQuirk = false;
                SyncToChip8();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("SUPER-CHIP settings");

            ImGui.SameLine();
            if (ImGui.Button("Off", new Vector2(40, 0)))
            {
                _shiftQuirk = false; _jumpQuirk = false; _vfReset = false;
                _memoryQuirk = false; _clippingQuirk = false; _displayWaitQuirk = false;
                SyncToChip8();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("All quirks off");
        }
    }
}
