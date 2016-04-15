﻿using System;
using System.Runtime.InteropServices;

namespace Screna.Audio
{
    class AudioCaptureClient : IDisposable
    {
        IAudioCaptureClient audioCaptureClientInterface;

        public AudioCaptureClient(IAudioCaptureClient audioCaptureClientInterface)
        {
            this.audioCaptureClientInterface = audioCaptureClientInterface;
        }

        /// <summary>
        /// Gets a pointer to the buffer
        /// </summary>
        /// <param name="numFramesToRead">Number of frames to read</param>
        /// <param name="bufferFlags">Buffer flags</param>
        /// <returns>Pointer to the buffer</returns>
        public IntPtr GetBuffer(out int numFramesToRead, out int bufferFlags)
        {
            IntPtr bufferPointer;
            long devicePosition, qpcPosition;
            Marshal.ThrowExceptionForHR(audioCaptureClientInterface.GetBuffer(out bufferPointer, out numFramesToRead, out bufferFlags, out devicePosition, out qpcPosition));
            return bufferPointer;
        }

        /// <summary>
        /// Gets the size of the next packet
        /// </summary>
        public int GetNextPacketSize()
        {
            int numFramesInNextPacket;
            Marshal.ThrowExceptionForHR(audioCaptureClientInterface.GetNextPacketSize(out numFramesInNextPacket));
            return numFramesInNextPacket;
        }

        /// <summary>
        /// Release buffer
        /// </summary>
        /// <param name="numFramesWritten">Number of frames written</param>
        public void ReleaseBuffer(int numFramesWritten)
        {
            Marshal.ThrowExceptionForHR(audioCaptureClientInterface.ReleaseBuffer(numFramesWritten));
        }

        /// <summary>
        /// Release the COM object
        /// </summary>
        public void Dispose()
        {
            if (audioCaptureClientInterface == null)
                return;

            // althugh GC would do this for us, we want it done now
            // to let us reopen WASAPI
            Marshal.ReleaseComObject(audioCaptureClientInterface);
            audioCaptureClientInterface = null;
            GC.SuppressFinalize(this);
        }
    }
}