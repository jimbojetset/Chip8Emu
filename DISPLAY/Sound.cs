using SDL2;
using static SDL2.SDL;
using System.Threading;
using System.Threading.Tasks;

namespace Chip8Emu
{
    internal static class Sound
    {
        private static uint _audioDevice;
        private static bool _audioInitialized = false;
        private static SDL_AudioSpec _audioSpec;
        private static readonly object _lock = new();
        private static volatile bool _playingTone = false;
        private static CancellationTokenSource? _toneCts;
        private static ushort _toneFrequency = 440;
        private static ushort _toneVolume = 16383;

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

            // One-shot: queue and return. This remains available but the emulator now
            // uses StartTone/StopTone for continuous sound driven by the ST timer.
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
            // Stop any one-shot audio and any streaming tone
            if (_audioInitialized && _audioDevice != 0)
            {
                lock (_lock)
                {
                    // Stop streaming tone if active
                    if (_playingTone)
                    {
                        _toneCts?.Cancel();
                        _playingTone = false;
                        _toneCts = null;
                    }

                    SDL_ClearQueuedAudio(_audioDevice);
                }
            }
        }

        public static void StartTone(ushort frequency, ushort volume = 16383)
        {
            if (!_audioInitialized)
            {
                Initialize();
                if (!_audioInitialized) return;
            }

            lock (_lock)
            {
                if (_playingTone)
                {
                    // Update frequency/volume for existing tone
                    _toneFrequency = frequency;
                    _toneVolume = volume;
                    return;
                }

                _playingTone = true;
                _toneFrequency = frequency;
                _toneVolume = volume;
                _toneCts = new CancellationTokenSource();
                var token = _toneCts.Token;

                Task.Run(() =>
                {
                    try
                    {
                        const int chunkMs = 50; // queue 50ms chunks
                        int sampleCount = (int)(_audioSpec.freq * chunkMs / 1000.0);
                        if (sampleCount <= 0) return;

                        short[] samples = new short[sampleCount];

                        while (!token.IsCancellationRequested && _audioInitialized && _audioDevice != 0)
                        {
                            // Throttle queued audio to ~1s max
                            uint queued = SDL_GetQueuedAudioSize(_audioDevice);
                            uint maxQueued = (uint)(_audioSpec.freq * sizeof(short) * 1);
                            if (queued > maxQueued)
                            {
                                Thread.Sleep(10);
                                continue;
                            }

                            double amp = _toneVolume >> 2;
                            double theta = _toneFrequency * 2.0 * Math.PI / _audioSpec.freq;
                            for (int i = 0; i < sampleCount; i++)
                            {
                                samples[i] = (short)(amp * Math.Sin(theta * i));
                            }

                            unsafe
                            {
                                fixed (short* ptr = samples)
                                {
                                    if (SDL_QueueAudio(_audioDevice, (IntPtr)ptr, (uint)(sampleCount * sizeof(short))) < 0)
                                    {
                                        Console.WriteLine($"Failed to queue audio: {SDL_GetError()}");
                                        break;
                                    }
                                }
                            }

                            // Yield a bit to allow the audio device to consume queued bytes
                            Thread.Sleep(10);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Tone thread error: {ex}");
                    }
                }, token);
            }
        }

        public static void StopTone()
        {
            lock (_lock)
            {
                if (!_playingTone) return;
                _toneCts?.Cancel();
                _playingTone = false;
                _toneCts = null;
                if (_audioInitialized && _audioDevice != 0)
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
