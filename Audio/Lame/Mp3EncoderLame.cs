﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Screna.Audio
{
    /// <summary>
    /// Mpeg Layer 3 (MP3) audio encoder using the LAME codec in external DLL.
    /// </summary>
    /// <remarks>
    /// Only 16-bit audio is currently supported.
    /// The class is designed for using only a single instance at a time.
    /// Find information about and downloads of the LAME project at http://lame.sourceforge.net/.
    /// <para>
    /// Default Name for the loaded lame dll is lameenc32.dll for 32-bit process and lameenc64.dll for 64-bit process.
    /// Use <see cref="Load(string)"/> to load the library from a custom path.
    /// </para>
    /// </remarks>
    public class Mp3EncoderLame : IAudioEncoder
    {
        /// <summary>
        /// Supported output bit rates (in kilobits per second).
        /// </summary>
        /// <remarks>
        /// Currently supported are 64, 96, 128, 160, 192 and 320 kbps.
        /// </remarks>
        public static readonly int[] SupportedBitRates = { 64, 96, 128, 160, 192, 320 };

        #region Loading LAME DLL
        static Type LameFacadeType;

        /// <summary>
        /// Sets the location of LAME DLL for using by this class.
        /// </summary>
        /// <remarks>
        /// This method may be called before creating any instances of this class.
        /// The LAME DLL should have the appropriate bitness (32/64), depending on the current process.
        /// If it is not already loaded into the process, the method loads it automatically.
        /// </remarks>
        public static void Load(string LameDllPath)
        {
            var LibraryName = Path.GetFileName(LameDllPath);

            if (!IsLibraryLoaded(LibraryName))
            {
                var LoadResult = LoadLibrary(LameDllPath);

                if (LoadResult == IntPtr.Zero)
                    throw new DllNotFoundException($"Library '{LameDllPath}' could not be loaded.");
            }

            var FacadeAssembly = GenerateLameFacadeAssembly(LibraryName);
            LameFacadeType = FacadeAssembly.GetType(typeof(Mp3EncoderLame).Namespace + ".LameFacadeImpl");
        }

        static Assembly GenerateLameFacadeAssembly(string LameDllName)
        {
            var ThisAssembly = typeof(Mp3EncoderLame).Assembly;
            var CSCompiler = new Microsoft.CSharp.CSharpCodeProvider();

            var CompilerOptions = new System.CodeDom.Compiler.CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false,
                IncludeDebugInformation = false,
                CompilerOptions = "/optimize",
                ReferencedAssemblies = { "mscorlib.dll", ThisAssembly.Location }
            };

            var SourceCode = GetLameFacadeAssemblySource(LameDllName, ThisAssembly);
            var CompilerResult = CSCompiler.CompileAssemblyFromSource(CompilerOptions, SourceCode);

            if (CompilerResult.Errors.HasErrors)
                throw new Exception("Could not generate LAME facade assembly.");

            return CompilerResult.CompiledAssembly;
        }

        static string GetLameFacadeAssemblySource(string LameDllName, Assembly ResourceAssembly)
        {
            string SourceCode;

            using (var SourceStream = ResourceAssembly.GetManifestResourceStream("Screna.Audio.Lame.LameFacadeImpl.cs"))
            using (var SourceReader = new StreamReader(SourceStream))
                SourceCode = SourceReader.ReadToEnd();
            
            return SourceCode.Replace("\"lame_enc.dll\"", $"\"{LameDllName}\"");
        }

        static bool IsLibraryLoaded(string LibraryName)
        {
            return Process.GetCurrentProcess().Modules.Cast<ProcessModule>().
                Any(m => string.Compare(m.ModuleName, LibraryName, StringComparison.InvariantCultureIgnoreCase) == 0);
        }

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Auto)]
        static extern IntPtr LoadLibrary(string fileName);
        #endregion

        const int SampleByteSize = 2;

        readonly ILameFacade LameFacade;

        /// <summary>
        /// Creates a new instance of <see cref="Mp3EncoderLame"/>.
        /// </summary>
        /// <param name="ChannelCount">Channel count.</param>
        /// <param name="SampleRate">Sample rate (in samples per second).</param>
        /// <param name="outputBitRateKbps">Output bit rate (in kilobits per second).</param>
        /// <remarks>
        /// Encoder expects audio data in 16-bit samples.
        /// Stereo data should be interleaved: left sample first, right sample second.
        /// </remarks>
        public Mp3EncoderLame(int ChannelCount = 1, int SampleRate = 44100, int outputBitRateKbps = 160)
        {
            if (LameFacadeType == null)
                Load(Path.Combine(Environment.CurrentDirectory, $"lameenc{(Environment.Is64BitProcess ? 64 : 32)}.dll"));

            LameFacade = (ILameFacade)Activator.CreateInstance(LameFacadeType);
            LameFacade.ChannelCount = ChannelCount;
            LameFacade.InputSampleRate = SampleRate;
            LameFacade.OutputBitRate = outputBitRateKbps;

            LameFacade.PrepareEncoding();

            WaveFormat = new Mp3WaveFormat(SampleRate, ChannelCount, LameFacade.FrameSize, EncoderDelay: LameFacade.EncoderDelay);
        }

        /// <summary>
        /// Releases resources.
        /// </summary>
        public void Dispose() => (LameFacade as IDisposable)?.Dispose();

        /// <summary>
        /// Encodes block of audio data.
        /// </summary>
        public int Encode(byte[] source, int sourceOffset, int sourceCount, byte[] destination, int destinationOffset)
        {
            return LameFacade.Encode(source, sourceOffset, sourceCount / SampleByteSize, destination, destinationOffset);
        }
        
        /// <summary>
        /// Ensures that the buffer is big enough to hold the result of encoding <paramref name="SourceCount"/> bytes.
        /// </summary>
        public void EnsureBufferIsSufficient(ref byte[] Buffer, int SourceCount)
        {
            var maxLength = GetMaxEncodedLength(SourceCount);
            if (Buffer?.Length >= maxLength)
                return;

            var newLength = Buffer?.Length * 2 ?? 1024;

            while (newLength < maxLength)
                newLength *= 2;

            Array.Resize(ref Buffer, newLength);
        }

        /// <summary>
        /// Flushes internal encoder's buffers.
        /// </summary>
        public int Flush(byte[] Destination, int DestinationOffset) => LameFacade.FinishEncoding(Destination, DestinationOffset);

        /// <summary>
        /// Gets if RIFF header is needed when writing to a file.
        /// </summary>
        public bool RequiresRiffHeader => false;

        /// <summary>
        /// Gets maximum length of encoded data. Estimate taken from the description of 'lame_encode_buffer' method in 'lame.h'
        /// </summary>
        public int GetMaxEncodedLength(int SourceCount) => (int)Math.Ceiling(1.25 * SourceCount / SampleByteSize + 7200);

        /// <summary>
        /// Wave Format including Mp3 Specific Data.
        /// </summary>
        public WaveFormat WaveFormat { get; }
    }
}
