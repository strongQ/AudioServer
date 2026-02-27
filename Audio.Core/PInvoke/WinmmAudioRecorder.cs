using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Audio.Core.PInvoke
{
    /// <summary>
    /// Provides a low-level audio recorder using Windows Core Audio APIs (winmm.dll).
    /// This implementation is designed to be compatible with Native AOT compilation.
    /// </summary>
    public sealed class WinmmAudioRecorder : IDisposable
    {
        // --- P/Invoke Definitions for winmm.dll ---
        private delegate void WaveInProc(IntPtr hwi, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveInOpen(out IntPtr phwi, uint uDeviceID, [In] ref WAVEFORMATEX pwfx, WaveInProc dwCallback, IntPtr dwCallbackInstance, uint fdwOpen);
        [DllImport("winmm.dll")]
        private static extern int waveInPrepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll")]
        private static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll")]
        private static extern int waveInAddBuffer(IntPtr hwi, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll")]
        private static extern int waveInStart(IntPtr hwi);
        [DllImport("winmm.dll")]
        private static extern int waveInStop(IntPtr hwi);
        [DllImport("winmm.dll")]
        private static extern int waveInReset(IntPtr hwi);
        [DllImport("winmm.dll")]
        private static extern int waveInClose(IntPtr hwi);

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        // Constants
        private const uint WAVE_MAPPER = 0xFFFFFFFF;
        private const uint CALLBACK_FUNCTION = 0x00030000;
        private const uint WIM_DATA = 0x3C0;
        private const int MMSYSERR_NOERROR = 0;
        private const int NUM_BUFFERS = 3;
        private const int BUFFER_SIZE = 16000; // 0.5 seconds of audio data at 16kHz 16-bit mono

        private IntPtr waveInHandle;
        private readonly WaveInProc waveInCallback;
        private readonly List<byte[]> recordedDataChunks = new List<byte[]>();
        private readonly WAVEHDR[] waveHeaders = new WAVEHDR[NUM_BUFFERS];
        private readonly GCHandle[] waveHeaderHandles = new GCHandle[NUM_BUFFERS];
        private readonly GCHandle[] bufferHandles = new GCHandle[NUM_BUFFERS];
        private bool isRecording;

        public WinmmAudioRecorder()
        {
            waveInCallback = WaveInCallback; // Keep delegate alive
        }

        public void StartRecording()
        {
            if (isRecording) return;

            WAVEFORMATEX format = new WAVEFORMATEX
            {
                wFormatTag = 1, // PCM
                nChannels = 1,
                nSamplesPerSec = 16000,
                wBitsPerSample = 16,
                nBlockAlign = 2,
                nAvgBytesPerSec = 32000,
                cbSize = 0
            };

            ThrowOnWaveError(waveInOpen(out waveInHandle, WAVE_MAPPER, ref format, waveInCallback, IntPtr.Zero, CALLBACK_FUNCTION));

            for (int i = 0; i < NUM_BUFFERS; i++)
            {
                var buffer = new byte[BUFFER_SIZE];
                bufferHandles[i] = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                waveHeaders[i] = new WAVEHDR { lpData = bufferHandles[i].AddrOfPinnedObject(), dwBufferLength = BUFFER_SIZE };
                waveHeaderHandles[i] = GCHandle.Alloc(waveHeaders[i], GCHandleType.Pinned);

                ThrowOnWaveError(waveInPrepareHeader(waveInHandle, waveHeaderHandles[i].AddrOfPinnedObject(), (uint)Marshal.SizeOf<WAVEHDR>()));
                ThrowOnWaveError(waveInAddBuffer(waveInHandle, waveHeaderHandles[i].AddrOfPinnedObject(), (uint)Marshal.SizeOf<WAVEHDR>()));
            }

            ThrowOnWaveError(waveInStart(waveInHandle));
            isRecording = true;
        }

        public byte[] StopRecording()
        {


            if (!isRecording) return Array.Empty<byte>();

            ThrowOnWaveError(waveInStop(waveInHandle));
            // Reset might be necessary to flush any pending buffers
            // ThrowOnWaveError(waveInReset(waveInHandle));
            isRecording = false;

            // The callback might still be processing the last buffers. A small delay can help.
            Thread.Sleep(200);

            // Combine all recorded chunks into a single byte array
            using (var ms = new MemoryStream())
            {
                foreach (var chunk in recordedDataChunks)
                {
                    ms.Write(chunk, 0, chunk.Length);
                }
                return ms.ToArray();
            }


        }

        private void WaveInCallback(IntPtr hwi, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
        {
            if (uMsg == WIM_DATA)
            {
                var header = Marshal.PtrToStructure<WAVEHDR>(dwParam1);
                if (header.dwBytesRecorded > 0)
                {
                    var buffer = new byte[header.dwBytesRecorded];
                    Marshal.Copy(header.lpData, buffer, 0, (int)header.dwBytesRecorded);
                    lock (recordedDataChunks)
                    {
                        recordedDataChunks.Add(buffer);
                    }
                }

                if (isRecording && waveInHandle != IntPtr.Zero)
                {
                    // Re-queue the buffer for more recording
                    waveInAddBuffer(hwi, dwParam1, (uint)Marshal.SizeOf<WAVEHDR>());
                }
            }
        }

        private void ThrowOnWaveError(int result)
        {
            if (result != MMSYSERR_NOERROR)
            {
                // In a real app, you might want a more sophisticated error lookup
                throw new Exception($"A WinMM audio error occurred: {result}");
            }
        }

        public void Dispose()
        {
            if (isRecording) StopRecording();

            if (waveInHandle != IntPtr.Zero)
            {
                for (int i = 0; i < NUM_BUFFERS; i++)
                {
                    if (waveHeaderHandles[i].IsAllocated)
                    {
                        waveInUnprepareHeader(waveInHandle, waveHeaderHandles[i].AddrOfPinnedObject(), (uint)Marshal.SizeOf<WAVEHDR>());
                        waveHeaderHandles[i].Free();
                    }
                    if (bufferHandles[i].IsAllocated)
                    {
                        bufferHandles[i].Free();
                    }
                }
                waveInClose(waveInHandle);
                waveInHandle = IntPtr.Zero;
            }
        }
    }
}
