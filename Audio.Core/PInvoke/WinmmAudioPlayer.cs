using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Audio.Core.PInvoke
{
    // <summary>
    /// Provides a static method to play WAV files using the Windows Core Multimedia library (winmm.dll).
    /// This implementation uses [LibraryImport] and is compatible with Native AOT compilation.
    /// </summary>
    public static partial class WinmmAudioPlayer
    {
        // Using [DllImport] is the traditional, highly compatible method for P/Invoke.
        // It is also AOT-compatible and resolves source generator issues.
        // CharSet.Unicode is equivalent to the previous StringMarshalling.Utf16.
        [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool PlaySound(string pszSound, nint hmod, uint fdwSound);

        // Flags for the PlaySound function
        private const uint SND_SYNC = 0x0000;      // Play synchronously; the function blocks until the sound finishes.
        private const uint SND_FILENAME = 0x00020000; // The pszSound parameter is a file name.
        private const uint SND_NODEFAULT = 0x00000002; // Do not play a default sound if the specified sound is not found.

        /// <summary>
        /// Synchronously plays a .wav file using winmm.dll.
        /// </summary>
        /// <param name="wavFilePath">The full path of the .wav file to play.</param>
        public static void PlayWavFile(string wavFilePath)
        {
            // Combine flags to tell the PlaySound function to synchronously play a sound from a file.
            PlaySound(wavFilePath, nint.Zero, SND_SYNC | SND_FILENAME | SND_NODEFAULT);
        }
    }

}
