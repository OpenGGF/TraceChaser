using System;
using System.Collections.Generic;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Fixed Sonic 2 REV01 observation of the accepted M68K-to-Z80 sound
    /// transfer. This is evidence only: it neither writes emulated memory nor
    /// feeds a request into any driver, queue, or playback owner.
    /// </summary>
    internal sealed class S2PreconsumptionRequestObserver : IDisposable
    {
        internal const uint Pc = 0x0010D6;
        internal const ushort MarkerToken = 24;
        internal const byte MarkerSourceCpu = 2;
        internal const ushort MarkerServiceToken = 0;
        internal const byte MarkerServiceKind = 0;
        internal const byte MarkerDepth = 0;
        private const int MaximumTransfersPerRow = 4;
        private readonly ICpuRegisterReader registers;
        private readonly IDisposable registration;
        private readonly Func<uint> callbackWatermark;
        private readonly Queue<PendingTransfer> pending =
            new Queue<PendingTransfer>();
        private int activeRow = -1;
        private bool disposed;

        internal sealed class Transfer
        {
            internal Transfer(int row, byte request, ushort slot, uint stack,
                uint nativeOrdinal, ushort serviceToken, byte serviceKind,
                byte depth)
                : this(row, request, slot, stack, nativeOrdinal, serviceToken,
                    serviceKind, depth, MarkerSourceCpu)
            { }

            internal Transfer(int row, byte request, ushort slot, uint stack,
                uint nativeOrdinal, ushort serviceToken, byte serviceKind,
                byte depth, byte sourceCpu)
            {
                Row = row; Request = request; Slot = slot; Pc = S2PreconsumptionRequestObserver.Pc;
                A7 = stack; NativeOrdinal = nativeOrdinal;
                ServiceToken = serviceToken; ServiceKind = serviceKind; Depth = depth;
                SourceCpu = sourceCpu;
            }

            internal int Row { get; private set; }
            internal byte Request { get; private set; }
            internal ushort Slot { get; private set; }
            internal uint Pc { get; private set; }
            internal uint A7 { get; private set; }
            internal uint NativeOrdinal { get; private set; }
            internal ushort ServiceToken { get; private set; }
            internal byte ServiceKind { get; private set; }
            internal byte Depth { get; private set; }
            internal byte SourceCpu { get; private set; }
        }

        private sealed class PendingTransfer
        {
            internal int Row; internal byte Request; internal ushort Slot;
            internal uint A7; internal uint MarkerOrdinal;
        }

        internal S2PreconsumptionRequestObserver(IGpgxHost host)
            : this(host, null)
        { }

        internal S2PreconsumptionRequestObserver(IGpgxHost host,
            Func<uint> watermark)
        {
            if (host == null) throw new ArgumentNullException("host");
            registers = host as ICpuRegisterReader;
            if (registers == null)
                throw new InvalidOperationException(
                    "The fixed S2 request observer requires M68K register reads.");
            callbackWatermark = watermark;
            registration = host.RegisterExecuteCallback(Pc, OnTransfer);
            if (registration == null)
                throw new InvalidOperationException(
                    "The fixed S2 request observer was not registered.");
        }

        internal void BeginRow(int row)
        {
            if (disposed) throw new ObjectDisposedException(
                "S2PreconsumptionRequestObserver");
            if (row < 0) throw new ArgumentOutOfRangeException("row");
            if (activeRow >= 0 || pending.Count != 0)
                throw new InvalidOperationException(
                    "The S2 request observer has an unmatched prior row.");
            activeRow = row;
        }

        /// <summary>
        /// Consumes the native drain owned by the request session immediately
        /// after the same row's advance.  Callers cannot attach an arbitrary
        /// frame capture to a callback from another advance.
        /// </summary>
        internal IReadOnlyList<Transfer> CompleteOwnedRow(int row,
            IEnumerable<GpgxAudioTraceEvent> events)
        {
            if (events == null) throw new ArgumentNullException("events");
            if (activeRow != row)
                throw new InvalidOperationException(
                    "The S2 request marker is outside its callback row.");
            var transfers = new List<Transfer>();
            foreach (GpgxAudioTraceEvent value in events)
            {
                if (!IsFixedMarkerCandidate(value))
                {
                    if (pending.Count != 0 && value.Kind == 2)
                        throw new InvalidOperationException(
                            "The S2 request terminal boundary preceded its marker.");
                    continue;
                }
                if (pending.Count == 0)
                    throw new InvalidOperationException(
                        "The S2 request marker is orphaned or duplicated.");
                if (callbackWatermark != null
                    && value.Ordinal != pending.Peek().MarkerOrdinal)
                    throw new InvalidOperationException(
                        "The S2 request marker is not the callback successor.");
                if (value.Kind != 10 || value.Value != 3 || value.Pc != Pc
                    || value.Subject != MarkerToken || value.PayloadLength != 4)
                    throw new InvalidOperationException(
                        "The S2 request next record is not its exact marker.");
                if (value.SourceCpu != MarkerSourceCpu
                    || value.ServiceToken != MarkerServiceToken
                    || value.ParentToken != MarkerServiceToken
                    || value.ServiceKindId != MarkerServiceKind
                    || value.Depth != MarkerDepth)
                    throw new InvalidOperationException(
                        "The S2 request marker source/owner differs.");
                PendingTransfer captured = pending.Dequeue();
                if (value.Payload != captured.A7)
                    throw new InvalidOperationException(
                        "The S2 request marker A7 differs from the callback.");
                transfers.Add(new Transfer(captured.Row, captured.Request,
                    captured.Slot, captured.A7, value.Ordinal,
                    value.ServiceToken, value.ServiceKindId, value.Depth,
                    value.SourceCpu));
            }
            if (pending.Count != 0)
                throw new InvalidOperationException(
                    "The S2 request callback has no exact native A7 marker.");
            activeRow = -1;
            return transfers.AsReadOnly();
        }

        private static bool IsFixedMarkerCandidate(GpgxAudioTraceEvent value)
        {
            // A known marker token identifies malformed kind/value/PC shapes;
            // the fixed action-7 shape identifies a wrong token.  Everything
            // else is an ordinary service/native record, not request evidence.
            return value.Subject == MarkerToken
                || (value.Kind == 10 && value.Value == 3 && value.Pc == Pc);
        }

        private void OnTransfer()
        {
            if (activeRow < 0)
                throw new InvalidOperationException(
                    "The S2 request callback is outside an active row.");
            if (pending.Count >= MaximumTransfersPerRow)
                throw new InvalidOperationException(
                    "The S2 request callback exceeded its four-slot bound.");
            byte request = (byte)registers.ReadCpuRegister("D0");
            ushort slot = (ushort)registers.ReadCpuRegister("D1");
            if (request == 0)
                throw new InvalidOperationException(
                    "The S2 request callback observed a zero transfer.");
            if (slot > 3)
                throw new InvalidOperationException(
                    "The S2 request callback observed a slot outside 0..3.");
            pending.Enqueue(new PendingTransfer
            {
                Row = activeRow, Request = request, Slot = slot,
                A7 = registers.ReadCpuRegister("A7"),
                MarkerOrdinal = callbackWatermark == null ? 0
                    : callbackWatermark()
            });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            registration.Dispose();
            if (pending.Count != 0)
                throw new InvalidOperationException(
                    "The S2 request observer ended with unmatched callbacks.");
        }
    }
}
