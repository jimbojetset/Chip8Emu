using SDL2;
using static SDL2.SDL;

namespace Chip8Emu
{
    internal static class Sound
    {
        private static uint _audioDevice;
        private static bool _audioInitialized = false;
        private static SDL_AudioSpec _audioSpec;
        private static readonly object _lock = new();

        public static void Initialize()
        {
            if (_audioInitialized) return;

            SDL_AudioSpec want = new()
            {
                freq = 44100,
                format = AUDIO_S16LSB,
                channels = 1,
                samples = 2048,
                callback = null
            };

            // Get the default audio device name
            int deviceCount = SDL_GetNumAudioDevices(0);
            string? deviceName = deviceCount > 0 ? SDL_GetAudioDeviceName(0, 0) : null;

            _audioDevice = SDL_OpenAudioDevice(deviceName!, 0, ref want, out _audioSpec, 0);
            if (_audioDevice == 0)
            {
                Console.WriteLine($"Failed to open audio device: {SDL_GetError()}");
                return;
            }

            _audioInitialized = true;
            SDL_PauseAudioDevice(_audioDevice, 0); // Start audio playback
        }

        public static void PlaySound(ushort frequency, int msDuration, ushort volume = 16383)
        {
            if (!_audioInitialized)
            {
                Initialize();
                if (!_audioInitialized) return;
            }

            lock (_lock)
            {
                // Clear any previously queued audio
                SDL_ClearQueuedAudio(_audioDevice);

                // Generate sine wave samples
                int sampleCount = (int)(_audioSpec.freq * msDuration / 1000.0);
                if (sampleCount <= 0) return;

                short[] samples = new short[sampleCount];
                double amp = volume >> 2;
                double theta = frequency * 2.0 * Math.PI / _audioSpec.freq;

                for (int i = 0; i < sampleCount; i++)
                {
                    samples[i] = (short)(amp * Math.Sin(theta * i));
                }

                // Queue audio
                unsafe
                {
                    fixed (short* ptr = samples)
                    {
                        if (SDL_QueueAudio(_audioDevice, (IntPtr)ptr, (uint)(sampleCount * sizeof(short))) < 0)
                        {
                            Console.WriteLine($"Failed to queue audio: {SDL_GetError()}");
                        }
                    }
                }
            }
        }

        public static void StopSound()
        {
            if (_audioInitialized && _audioDevice != 0)
            {
                SDL_ClearQueuedAudio(_audioDevice);
            }
        }

        public static void Cleanup()
        {
            if (_audioInitialized && _audioDevice != 0)
            {
                SDL_CloseAudioDevice(_audioDevice);
                _audioDevice = 0;
                _audioInitialized = false;
            }
        }
    }
}