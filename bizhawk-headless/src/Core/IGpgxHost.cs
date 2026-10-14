using System;

namespace OpenGGF.BizHawk.Headless
{
    public interface IGpgxHost : IDisposable
    {
        int CompletedFrame { get; }

        /// <summary>
        /// Whether the most recently completed frame was a lag frame (the
        /// core did not poll input). Port of the Lua recorder's
        /// emu.islagged(); consumed by the S2 special-stage row writer.
        /// </summary>
        bool IsLagged { get; }

        /// <summary>
        /// The cumulative lag-frame count since power-on, read from the
        /// core's own accounting. Port of the Lua recorder's emu.lagcount();
        /// consumed by the S1 complete-run lag_state aux event, whose
        /// fixture values are monotone across the whole pass (boot frames
        /// and inter-segment gaps included).
        /// </summary>
        int LagCount { get; }

        void ClearButtons();
        void SetButton(string name, bool pressed);
        IDisposable RegisterExecuteCallback(uint address, Action callback);
        void Advance();
        byte ReadMainRamByte(int offset);
    }

    public interface ICpuRegisterReader
    {
        uint ReadCpuRegister(string name);
    }

    /// <summary>Opt-in rendered diagnostics, separate from ordinary trace advancement.
    /// Captures end-of-completed-hardware-frame pixels and observed VDP domains;
    /// it does not assert that frame-end VRAM describes every earlier scanline.</summary>
    public interface IGpgxVideoCaptureHost
    {
        void AdvanceWithVideo();
        GpgxPublishedVideoFrame CapturePublishedFrame();
    }

    public sealed class GpgxPublishedVideoFrame
    {
        public int CompletedFrame { get; private set; }
        public bool IsLagged { get; private set; }
        public int LagCount { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int[] ArgbPixels { get; private set; }
        public byte[] Vram { get; private set; }
        public byte[] Cram { get; private set; }
        // Pinned GPGX exposes its complete128-byte backing domain; Genesis uses80 bytes.
        public byte[] Vsram { get; private set; }
        public string VramEndian { get; private set; }
        public string CramEndian { get; private set; }
        public string VsramEndian { get; private set; }
        internal GpgxPublishedVideoFrame(int frame, bool lagged, int lagCount,
            int width, int height, int[] pixels, byte[] vram, byte[] cram, byte[] vsram,
            string vramEndian, string cramEndian, string vsramEndian)
        {
            if (width <= 0 || height <= 0 || pixels == null
                || (long)width * height > pixels.Length)
                throw new InvalidOperationException("Invalid current video dimensions/buffer.");
            if (vram == null || vram.Length != 65536 || cram == null || cram.Length != 128
                || vsram == null || vsram.Length != 128)
                throw new InvalidOperationException("Invalid published VDP domain lengths.");
            CompletedFrame = frame; IsLagged = lagged; LagCount = lagCount;
            Width = width; Height = height;
            ArgbPixels = (int[])pixels.Clone(); Vram = (byte[])vram.Clone();
            Cram = (byte[])cram.Clone(); Vsram = (byte[])vsram.Clone();
            VramEndian = vramEndian; CramEndian = cramEndian; VsramEndian = vsramEndian;
        }
    }

    internal sealed class GpgxVideoCaptureGeneration
    {
        private int renderedFrame = -1;
        internal void Invalidate() { renderedFrame = -1; }
        internal void Advance(Action<bool, bool> advance, Func<int> completed, Action check)
        {
            Invalidate(); // Even a failure before core.Frame increments invalidates the old pixels.
            advance(true, false);
            check(); // A failed core/callback pass cannot publish a rendered generation.
            renderedFrame = completed();
        }
        internal void RequireCurrent(int completed)
        {
            if (renderedFrame != completed)
                throw new InvalidOperationException("Current frame was not advanced with video.");
        }
    }

}
