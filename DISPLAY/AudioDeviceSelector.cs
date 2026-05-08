using System.Numerics;
using ImGuiNET;

namespace Chip8Emu
{
    /// <summary>
    /// Modal ImGui popup that prompts the user to choose an audio output device.
    /// Selection is made by clicking an entry or pressing the matching number key
    /// (0-9 for the first ten devices). The popup only opens when more than one
    /// device is available; otherwise it completes immediately with the default.
    /// </summary>
    internal sealed class AudioDeviceSelector
    {
        private const string PopupId = "Select Audio Device";

        private readonly List<string> _devices;
        private bool _needsOpen;
        private bool _completed;
        private string? _selectedDeviceName;

        public AudioDeviceSelector(List<string> devices)
        {
            _devices = devices;

            if (_devices.Count > 1)
            {
                _needsOpen = true;
            }
            else
            {
                // 0 or 1 devices: nothing to choose - use the system default.
                _selectedDeviceName = null;
                _completed = true;
            }
        }

        /// <summary>True once the user has made a selection (or no prompt was needed).</summary>
        public bool IsCompleted => _completed;

        /// <summary>The selected device name, or null to use the system default.</summary>
        public string? SelectedDeviceName => _selectedDeviceName;

        /// <summary>Render the modal. Must be called inside an active ImGui frame.</summary>
        public void Draw()
        {
            if (_completed)
                return;

            if (_needsOpen)
            {
                ImGui.OpenPopup(PopupId);
                _needsOpen = false;
            }

            Vector2 center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            if (ImGui.BeginPopupModal(PopupId, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove))
            {
                ImGui.Text("Multiple audio devices detected. Select one:");
                ImGui.Separator();

                for (int i = 0; i < _devices.Count; i++)
                {
                    string label = $"[{i}] {_devices[i]}";
                    if (ImGui.Selectable(label))
                    {
                        Select(i);
                        ImGui.CloseCurrentPopup();
                        ImGui.EndPopup();
                        return;
                    }
                }

                // Number-key selection (matches the previous command-line behaviour).
                int maxKey = Math.Min(10, _devices.Count);
                for (int i = 0; i < maxKey; i++)
                {
                    if (ImGui.IsKeyPressed(ImGuiKey._0 + i, false) ||
                        ImGui.IsKeyPressed(ImGuiKey.Keypad0 + i, false))
                    {
                        Select(i);
                        ImGui.CloseCurrentPopup();
                        break;
                    }
                }

                ImGui.EndPopup();
            }
        }

        private void Select(int index)
        {
            _selectedDeviceName = _devices[index];
            _completed = true;
            Console.WriteLine($"User selected audio device [{index}] {_selectedDeviceName}");
        }
    }
}