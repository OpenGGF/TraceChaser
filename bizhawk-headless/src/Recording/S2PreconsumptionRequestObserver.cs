using System;
using System.Collections.Generic;
using System.IO;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Candidate-only native boundary used while the fixed M68K execute
    /// callback is active. It reports the first native ordinal that can follow
    /// that callback; ABI-4 EventCount remains ready-phase-only.
    /// </summary>
    internal interface IS2RequestSuccessorOrdinalApi
    {
        int S2RequestSuccessorOrdinal(out uint ordinal);
    }

    /// <summary>
    /// Session-owned Sonic 2 REV01 observation of the accepted M68K-to-Z80
    /// sound transfer. This is evidence only: it neither writes emulated
    /// memory nor feeds a request into any driver, queue, or playback owner.
    /// </summary>
    internal sealed class S2PreconsumptionRequestObserver : IDisposable
    {
        internal const uint Pc = 0x0010D6;
        /// <summary>
        /// sndDriverInput's other store into Z80 RAM: move.b
        /// d0,zVar.QueueToPlay(a1) at .isNotPauseCommand, which the
        /// disassembly labels loc_10C0 (docs/s2disasm/s2.asm:1302-1304).
        /// <see cref="Pc"/> names the SFX store inside .loop (:1317-1326);
        /// this one carries every music request, and also any sound a caller
        /// routes through PlayMusic rather than PlaySound, such as the
        /// ring-milestone check at :25913-25914.
        /// </summary>
        internal const uint MusicPc = 0x0010C0;
        /// <summary>
        /// The slot a music transfer is recorded under. The SFX site reads D1,
        /// which the .loop index makes a real queue slot, but at
        /// <see cref="MusicPc"/> D1 holds the pause-check residue of
        /// move.b d0,d1 and subi.b #MusID_Pause,d1 (:1294-1295), so it is not
        /// a slot and is not read. Four is outside the driver's 0..3 SFX
        /// queue, so a music transfer can never be confused with one.
        /// </summary>
        internal const ushort MusicSlot = 4;
        internal const ushort MarkerToken = 24;
        internal const ushort Kind3MarkerToken = 25;
        internal const byte MarkerSourceCpu = 2;
        internal const ushort MarkerServiceToken = 0;
        internal const byte MarkerServiceKind = 0;
        internal const byte Kind3MarkerServiceKind = 3;
        internal const byte MarkerDepth = 0;
        /// <summary>
        /// One pass of sndDriverInput can transfer at most one music request
        /// and four SFX ones, because .doSFX loads moveq #4-1,d1 on the
        /// shipped fixBugs = 0 path (docs/s2disasm/s2.asm:1310-1315). The
        /// bound is that maximum rather than a comfortable margin, so a row
        /// that genuinely exceeds it fails loudly instead of being trimmed.
        /// </summary>
        private const int MaximumTransfersPerRow = 5;

        private readonly ICpuRegisterReader registers;
        private readonly CompleteRunAudioObserver nativeObserver;
        private readonly IDisposable registration;
        private readonly IDisposable musicRegistration;
        /// <summary>
        /// Music-store transfers, kept apart from <see cref="pending"/>
        /// because that queue's contract is one native action-7 marker per
        /// callback and this site emits none. They are published with the row
        /// they arrived in and with zeroed native correlation fields, which is
        /// the honest record: the row is observed, the service is not.
        /// </summary>
        private readonly List<PendingTransfer> musicPending =
            new List<PendingTransfer>();
        private readonly Queue<PendingTransfer> pending =
            new Queue<PendingTransfer>();
        private readonly List<Transfer> published = new List<Transfer>();
        private ushort rowStartKind4RootToken;
        private int activeRow = -1;
        private int nextRow;
        private bool disposed;
        private bool completed;
        private bool failed;
        private readonly int expectedEnd;

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
                Row = row; Request = request; Slot = slot;
                // sndDriverInput stores twice and the slot says which store
                // this was: the reserved music slot can only come from
                // loc_10C0 (docs/s2disasm/s2.asm:1302-1304), everything else
                // from the SFX store inside .loop (:1317-1326).
                Pc = slot == MusicSlot
                    ? S2PreconsumptionRequestObserver.MusicPc
                    : S2PreconsumptionRequestObserver.Pc;
                A7 = stack;
                NativeOrdinal = nativeOrdinal; ServiceToken = serviceToken;
                ServiceKind = serviceKind; Depth = depth; SourceCpu = sourceCpu;
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
            internal int Row;
            internal byte Request;
            internal ushort Slot;
            internal uint A7;
            internal uint SuccessorOrdinal;
        }

        /// <summary>The one frame and transfer list produced by one drain.</summary>
        internal sealed class OwnedRow
        {
            internal OwnedRow(CompleteRunAudioObserver.FrameCapture frame,
                IReadOnlyList<Transfer> transfers)
            { Frame = frame; Transfers = transfers; }

            internal CompleteRunAudioObserver.FrameCapture Frame
            { get; private set; }
            internal IReadOnlyList<Transfer> Transfers
            { get; private set; }
        }

        internal S2PreconsumptionRequestObserver(
            S2PreconsumptionRequestProfile.Candidate candidate,
            IGpgxHost host, CompleteRunAudioObserver observer)
            : this(candidate, host, observer,
                S2AudioObserverProfile.ExclusiveEnd)
        { }

        internal S2PreconsumptionRequestObserver(
            S2PreconsumptionRequestProfile.Candidate candidate,
            IGpgxHost host, CompleteRunAudioObserver observer,
            int expectedExclusiveEnd)
        {
            if (candidate == null) throw new ArgumentNullException("candidate");
            if (host == null) throw new ArgumentNullException("host");
            if (observer == null) throw new ArgumentNullException("observer");
            if (expectedExclusiveEnd <= 0)
                throw new ArgumentOutOfRangeException("expectedExclusiveEnd");
            if (candidate.Pc != Pc || candidate.Opcode != "13801009"
                || candidate.MusicPc != MusicPc
                || candidate.MusicOpcode != "13400008"
                || candidate.MusicSlot != MusicSlot
                || candidate.MarkerToken != MarkerToken
                || candidate.Kind3MarkerToken != Kind3MarkerToken
                || candidate.ProductionBound)
                throw new InvalidDataException(
                    "The S2 request candidate cannot select a different hook or authority state.");
            registers = host as ICpuRegisterReader;
            if (registers == null)
                throw new InvalidOperationException(
                    "The fixed S2 request observer requires M68K register reads.");
            nativeObserver = observer;
            expectedEnd = expectedExclusiveEnd;
            registration = host.RegisterExecuteCallback(Pc, OnTransfer);
            if (registration == null)
                throw new InvalidOperationException(
                    "The fixed S2 request observer was not registered.");
            musicRegistration =
                host.RegisterExecuteCallback(MusicPc, OnMusicTransfer);
            if (musicRegistration == null)
                throw new InvalidOperationException(
                    "The fixed S2 music request observer was not registered.");
        }

        internal IReadOnlyList<Transfer> PublishedTransfers
        { get { return published.AsReadOnly(); } }

        /// <summary>
        /// Owns BeginFrame, the emulator advance, EndFrame, the native drain,
        /// and request correlation. No caller can attach a separately-built
        /// event list or a nullable ordering watermark.
        /// </summary>
        internal IReadOnlyList<Transfer> AdvanceRow(int row, Action advance)
        {
            return AdvanceOwnedRow(row, advance).Transfers;
        }

        internal OwnedRow AdvanceOwnedRow(int row, Action advance)
        {
            if (disposed) throw new ObjectDisposedException(
                "S2PreconsumptionRequestObserver");
            if (advance == null) throw new ArgumentNullException("advance");
            if (row != nextRow)
            {
                DisposeAfterFailure();
                throw new InvalidDataException(
                    "The S2 request candidate cannot carry evidence across rows.");
            }
            try
            {
                BeginOwnedRow(row);
                CompleteRunAudioObserver.FrameCapture frame =
                    nativeObserver.CaptureCanonicalFrame(row, advance);
                IReadOnlyList<Transfer> transfers = CorrelateOwnedFrame(
                    row, frame.RawEvents);
                if (row >= S2AudioObserverProfile.FirstRow)
                    for (int index = 0; index < transfers.Count; index++)
                        published.Add(transfers[index]);
                nextRow++;
                return new OwnedRow(frame, transfers);
            }
            catch
            {
                DisposeAfterFailure();
                throw;
            }
        }

        internal void Complete()
        {
            if (disposed) throw new ObjectDisposedException(
                "S2PreconsumptionRequestObserver");
            if (nextRow != expectedEnd)
            {
                DisposeAfterFailure();
                throw new InvalidDataException(
                    "The S2 request candidate ended before its full power-on interval.");
            }
            completed = true;
            Dispose();
        }

        private void BeginOwnedRow(int row)
        {
            if (activeRow >= 0 || pending.Count != 0)
                throw new InvalidOperationException(
                    "The S2 request observer has an unmatched prior row.");
            rowStartKind4RootToken =
                nativeObserver.CurrentS2RequestKind4RootToken();
            activeRow = row;
        }

        private IReadOnlyList<Transfer> CorrelateOwnedFrame(int row,
            IReadOnlyList<GpgxAudioTraceEvent> events)
        {
            if (activeRow != row)
                throw new InvalidOperationException(
                    "The S2 request marker is outside its callback row.");
            var transfers = new List<Transfer>();
            for (int index = 0; index < events.Count; index++)
            {
                GpgxAudioTraceEvent value = events[index];
                if (!IsFixedMarkerCandidate(value))
                {
                    if (pending.Count != 0 && value.Kind == 2
                        && value.Ordinal >= pending.Peek().SuccessorOrdinal)
                        throw new InvalidOperationException(
                            "The S2 request terminal boundary followed its callback before its marker.");
                    continue;
                }
                if (pending.Count == 0)
                    throw new InvalidOperationException(
                        "The S2 request marker is orphaned or duplicated.");
                PendingTransfer captured = pending.Peek();
                if (value.Ordinal < captured.SuccessorOrdinal)
                    throw new InvalidOperationException(
                        "The S2 request marker predates its callback successor boundary.");
                if (value.Kind != 10 || value.Value != 3 || value.Pc != Pc
                    || value.PayloadLength != 4)
                    throw new InvalidOperationException(
                        "The S2 request next marker is not its exact fixed action-7 record.");
                if (value.SourceCpu != MarkerSourceCpu
                    || !HasReviewedMarkerOwner(value, events, index))
                    throw new InvalidOperationException(
                        "The S2 request marker source/owner differs.");
                pending.Dequeue();
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
            for (int index = 0; index < musicPending.Count; index++)
            {
                PendingTransfer music = musicPending[index];
                transfers.Add(new Transfer(music.Row, music.Request,
                    music.Slot, music.A7, 0, 0, 0, 0, MarkerSourceCpu));
            }
            musicPending.Clear();
            activeRow = -1;
            rowStartKind4RootToken = 0;
            return transfers.AsReadOnly();
        }

        private static bool IsFixedMarkerCandidate(GpgxAudioTraceEvent value)
        {
            return value.Subject == MarkerToken
                || value.Subject == Kind3MarkerToken
                || (value.Kind == 10 && value.Value == 3 && value.Pc == Pc);
        }

        private bool HasReviewedMarkerOwner(GpgxAudioTraceEvent value,
            IReadOnlyList<GpgxAudioTraceEvent> events, int markerIndex)
        {
            bool root = value.Subject == MarkerToken
                && value.ServiceToken == MarkerServiceToken
                && value.ParentToken == MarkerServiceToken
                && value.ServiceKindId == MarkerServiceKind
                && value.Depth == MarkerDepth;
            bool kind3 = value.Subject == Kind3MarkerToken
                && value.ServiceToken != MarkerServiceToken
                && value.ParentToken == MarkerServiceToken
                && value.ServiceKindId == Kind3MarkerServiceKind
                && value.Depth == MarkerDepth;
            bool nestedKind3 = value.Subject == Kind3MarkerToken
                && value.ServiceToken != MarkerServiceToken
                && value.ParentToken != MarkerServiceToken
                && value.ServiceToken != value.ParentToken
                && value.ServiceKindId == Kind3MarkerServiceKind
                && value.Depth == 1
                && HasKind4RootLifecycle(value.ParentToken, events,
                    markerIndex);
            return root || kind3 || nestedKind3;
        }

        private bool HasKind4RootLifecycle(ushort rootToken,
            IReadOnlyList<GpgxAudioTraceEvent> events, int markerIndex)
        {
            if (rootToken == rowStartKind4RootToken) return true;
            for (int index = 0; index < markerIndex; index++)
            {
                GpgxAudioTraceEvent value = events[index];
                if (value.Kind == 1 && value.ServiceToken == rootToken
                    && value.ParentToken == 0 && value.ServiceKindId == 4
                    && value.Depth == 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The music half of sndDriverInput. It records the same request byte
        /// from D0 and the same successor ordinal, under the fixed
        /// <see cref="MusicSlot"/>; it reads no slot register, because there
        /// is none to read at this instruction.
        /// </summary>
        private void OnMusicTransfer()
        {
            if (activeRow < 0)
                throw new InvalidOperationException(
                    "The S2 music request callback is outside an active row.");
            if (pending.Count + musicPending.Count >= MaximumTransfersPerRow)
                throw new InvalidOperationException(
                    "The S2 music request callback exceeded its five-slot bound.");
            byte request = (byte)registers.ReadCpuRegister("M68K D0");
            if (request == 0)
                throw new InvalidOperationException(
                    "The S2 music request callback observed a zero transfer.");
            // No successor ordinal is read. That call is only valid inside the
            // armed native action-7 context the SFX site owns, and this store
            // runs outside it; asking for one fails with status -3 on the
            // driver's very first music load.
            musicPending.Add(new PendingTransfer
            {
                Row = activeRow,
                Request = request,
                Slot = MusicSlot,
                A7 = registers.ReadCpuRegister("M68K A7"),
                SuccessorOrdinal = 0
            });
        }

        private void OnTransfer()
        {
            if (activeRow < 0)
                throw new InvalidOperationException(
                    "The S2 request callback is outside an active row.");
            if (pending.Count >= MaximumTransfersPerRow)
                throw new InvalidOperationException(
                    "The S2 request callback exceeded its five-slot bound.");
            byte request = (byte)registers.ReadCpuRegister("M68K D0");
            ushort slot = (ushort)registers.ReadCpuRegister("M68K D1");
            if (request == 0)
                throw new InvalidOperationException(
                    "The S2 request callback observed a zero transfer.");
            if (slot > 3)
                throw new InvalidOperationException(
                    "The S2 request callback observed a slot outside 0..3.");
            pending.Enqueue(new PendingTransfer
            {
                Row = activeRow,
                Request = request,
                Slot = slot,
                A7 = registers.ReadCpuRegister("M68K A7"),
                SuccessorOrdinal = nativeObserver.CurrentS2RequestSuccessorOrdinal()
            });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            registration.Dispose();
            musicRegistration.Dispose();
            if (musicPending.Count != 0)
                throw new InvalidOperationException(
                    "The S2 music request observer ended with unpublished callbacks.");
            if (pending.Count != 0)
                throw new InvalidOperationException(
                    "The S2 request observer ended with unmatched callbacks.");
            if (!completed && !failed)
                throw new InvalidDataException(
                    "The S2 request candidate was disposed before its full power-on interval.");
        }

        private void DisposeAfterFailure()
        {
            if (disposed) return;
            failed = true;
            try { Dispose(); }
            catch (InvalidOperationException) { }
        }
    }
}
