using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Optional load-time diagnostics from the shipped direct FIFO instructions.
    /// Does not create work, write RAM, or supply canonical v5 completion events.
    /// Origin: S3K ending reunion exact-head attribution, 2026-10-15.
    /// </summary>
    public sealed class S3kExactKosMeasurementObserver
    {
        public const uint ModuleSubmissionPc = 0x1B46;
        public const uint DirectSubmissionPc = 0x1BEE;
        public const uint HeadServicePc = 0x1C1A;
        public const uint HeadRetirementPc = 0x1CE4;
        private const uint LevelLoopReturn = 0x651C;
        private const uint TitleCardLoopReturn = 0x62D8;
        private sealed class Job
        {
            public long Ordinal;
            public uint Source, Destination, SubmissionPc;
            public HardwareTimingEventEngine.StandardKosShape Shape;
            public string Fingerprint;
            public int Opportunities;
            public bool Unclassified;
        }
        private readonly byte[] rom;
        private readonly HardwareTimingEventEngine inspector;
        private readonly TextWriter output;
        private readonly string fixture, movieSha256, romSha1;
        private readonly List<Job> queue = new List<Job>();
        private long ordinal;
        private int epoch, lastRawFrame = -1, sequence;

        public S3kExactKosMeasurementObserver(byte[] rom, TextWriter output,
            string fixture, string movieSha256, string romSha1)
        {
            if (rom == null) throw new ArgumentNullException("rom");
            if (output == null) throw new ArgumentNullException("output");
            if (string.IsNullOrWhiteSpace(fixture)
                || movieSha256 == null
                || !System.Text.RegularExpressions.Regex.IsMatch(movieSha256, "\\A[0-9a-fA-F]{64}\\z")
                || !string.Equals(romSha1, "CFBF98C36C776677290A872547AC47C53D2761D6",
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Exact Kos measurement requires movie/locked-ROM identity.");
            this.rom = (byte[])rom.Clone();
            inspector = new HardwareTimingEventEngine(this.rom);
            this.output = output;
            this.fixture = fixture;
            this.movieSha256 = movieSha256.ToLowerInvariant();
            this.romSha1 = romSha1.ToUpperInvariant();
        }

        /// <summary>
        /// Three literal measurement hooks by default. Exact top-level enqueue
        /// is separately opt-in; its module return is skipped to avoid duplicates.
        /// All opcodes are checked before any callback is registered.
        /// </summary>
        public IDisposable Attach(IGpgxHost host, Func<int> rawFrame,
            bool includeDirectSubmissions = false)
        {
            if (host == null) throw new ArgumentNullException("host");
            if (rawFrame == null) throw new ArgumentNullException("rawFrame");
            var cpu = host as ICpuRegisterReader;
            if (cpu == null) throw new InvalidOperationException(
                "Exact Kos measurement requires CPU register reads.");
            RequireOpcode(ModuleSubmissionPc, new byte[] { 0x00,0x38,0x00,0x80,0xFF,0x60,0x4E,0x75 });
            RequireOpcode(HeadServicePc, new byte[] { 0x4A,0x78,0xFF,0x0E,0x67,0x00,0x00,0xDA,0x6B,0x00,0x00,0xD8 });
            RequireOpcode(HeadRetirementPc, new byte[] { 0x67,0x14,0x41,0xF8,0xFF,0x40,0x43,0xF8 });
            RequireOpcode(LevelLoopReturn - 6, new byte[] { 0x4E,0xB9,0x00,0x00,0x1C,0x1A });
            RequireOpcode(TitleCardLoopReturn - 6, new byte[] { 0x4E,0xB9,0x00,0x00,0x1C,0x1A });
            if (includeDirectSubmissions)
                RequireOpcode(DirectSubmissionPc, new byte[] { 0x4E,0x75,0x4A,0x78,0xFF,0x0E });
            var callbacks = new List<IDisposable>();
            try
            {
                callbacks.Add(host.RegisterExecuteCallback(ModuleSubmissionPc,
                    () => Submitted(host, ModuleSubmissionPc)));
                callbacks.Add(host.RegisterExecuteCallback(HeadServicePc,
                    () => Served(host, cpu)));
                callbacks.Add(host.RegisterExecuteCallback(HeadRetirementPc,
                    () => Retired(host, rawFrame())));
                if (includeDirectSubmissions)
                    callbacks.Add(host.RegisterExecuteCallback(DirectSubmissionPc, () =>
                    {
                        if (ReturnAddress(host, cpu) != ModuleSubmissionPc)
                            Submitted(host, DirectSubmissionPc);
                    }));
                return new Scope(callbacks);
            }
            catch
            {
                foreach (var callback in callbacks) callback.Dispose();
                throw;
            }
        }

        public void Reset()
        {
            queue.Clear();
            ordinal = 0;
            epoch++;
            lastRawFrame = -1;
            sequence = 0;
        }

        private void Submitted(IGpgxHost host, uint pc)
        {
            int count = Count(host);
            if (count == 0 || count <= queue.Count)
                throw new InvalidDataException("Exact Kos enqueue did not append one occupied FIFO entry.");
            Discover(host, count - 1);
            queue.Add(ReadJob(host, count - 1, pc));
        }

        private void Served(IGpgxHost host, ICpuRegisterReader cpu)
        {
            int count = Count(host);
            Discover(host, count);
            if (count == 0) return;
            Job head = queue[0];
            uint caller = ReturnAddress(host, cpu);
            // LevelLoop and loc_62CC own the runtime PRE service admission.
            // Other literal callers (title, continue, results, etc.) remain diagnostic.
            bool classified = (S3KRam.U8(host, S3KRam.GameMode) & 0x7F) == 0x0C
                && (caller == LevelLoopReturn || caller == TitleCardLoopReturn);
            if (!classified) head.Unclassified = true;
            head.Opportunities = checked(head.Opportunities + 1);
        }

        private void Retired(IGpgxHost host, int rawFrame)
        {
            if (queue.Count == 0)
                throw new InvalidDataException("Exact Kos retirement has no observed head.");
            Job head = queue[0];
            if (head.Opportunities == 0 || Count(host) != queue.Count - 1
                || (S3KRam.U16(host, S3KRam.KosDecompQueueCount) & 0x8000) != 0
                || S3KRam.U32(host, S3KRam.KosDecompQueue) != head.Source + (uint)head.Shape.CompressedLength
                || S3KRam.U32(host, S3KRam.KosDecompQueue + 4) != head.Destination + (uint)head.Shape.DestinationLength)
                throw new InvalidDataException("Exact Kos retirement disagrees with consumed descriptor/count.");
            // At 1CE4 the count is committed, but the remaining entries have
            // not shifted. Compare the same physical tail before changing our ledger.
            for (int i = 1; i < queue.Count; i++) RequireEntry(host, i, queue[i]);
            Write(head, rawFrame);
            queue.RemoveAt(0);
        }

        private void Discover(IGpgxHost host, int count)
        {
            if (count < queue.Count)
                throw new InvalidDataException("Kos FIFO disappeared without observed retirement/reset.");
            for (int i = 0; i < queue.Count; i++) RequireEntry(host, i, queue[i]);
            while (queue.Count < count) queue.Add(ReadJob(host, queue.Count, 0));
        }

        private Job ReadJob(IGpgxHost host, int index, uint pc)
        {
            int entry = S3KRam.KosDecompQueue + index * S3KRam.KosDecompQueueEntrySize;
            uint source = S3KRam.U32(host, entry), destination = S3KRam.U32(host, entry + 4);
            var shape = inspector.InspectStandardKos(source);
            return new Job { Ordinal = ordinal++, Source = source, Destination = destination,
                SubmissionPc = pc, Shape = shape,
                Fingerprint = HardwareTimingEventEngine.ComputeSubmissionFingerprint(
                    "KOS_DECOMPRESSION_QUEUE", checked((int)source), shape.CompressedLength,
                    unchecked((int)destination), shape.DestinationLength, "kosinski", 1) };
        }

        private static int Count(IGpgxHost host)
        {
            int count = S3KRam.U16(host, S3KRam.KosDecompQueueCount) & 0x7FFF;
            if (count > S3KRam.KosDecompQueueCapacity)
                throw new InvalidDataException("Exact Kos measurement requires the native four-entry FIFO.");
            return count;
        }
        private static void RequireEntry(IGpgxHost host, int index, Job job)
        {
            int entry = S3KRam.KosDecompQueue + index * S3KRam.KosDecompQueueEntrySize;
            if (S3KRam.U32(host, entry) != job.Source || S3KRam.U32(host, entry + 4) != job.Destination)
                throw new InvalidDataException("Exact Kos FIFO descriptor changed before committed retirement.");
        }
        private static uint ReturnAddress(IGpgxHost host, ICpuRegisterReader cpu)
        {
            uint stack = cpu.ReadCpuRegister("M68K A7");
            if ((stack & 0xFFFF0000) != 0xFFFF0000 || (stack & 0xFFFF) > 0xFFFC)
                throw new InvalidDataException("Exact Kos caller stack is outside main RAM.");
            return S3KRam.U32(host, (int)(stack & 0xFFFF));
        }
        private void RequireOpcode(uint pc, byte[] bytes)
        {
            if (pc + bytes.Length > rom.Length)
                throw new InvalidDataException("Exact Kos measurement ROM hook is out of bounds.");
            for (int i = 0; i < bytes.Length; i++)
                if (rom[pc + i] != bytes[i])
                    throw new InvalidDataException("Exact Kos measurement ROM hook mismatch at " + pc.ToString("X"));
        }
        private void Write(Job job, int rawFrame)
        {
            if (rawFrame < 0 || rawFrame < lastRawFrame)
                throw new InvalidDataException("Exact Kos measurement raw frame is nonmonotonic.");
            if (rawFrame != lastRawFrame) { lastRawFrame = rawFrame; sequence = 0; }
            using (var json = new JsonTextWriter(output) { CloseOutput = false })
            {
                json.WriteStartObject();
                Property(json,"measurement_schema",2); Property(json,"recorder_version","load-time-measurement-v2");
                Property(json,"fixture",fixture); Property(json,"movie_sha256",movieSha256); Property(json,"rom_sha1",romSha1);
                Property(json,"service_model","s3k-kos-v1"); Property(json,"epoch",epoch); Property(json,"raw_frame",rawFrame);
                Property(json,"sequence_in_frame",sequence++); Property(json,"boundary","pre_main_loop");
                Property(json,"kind","KOS_DECOMPRESSION_QUEUE"); Property(json,"ordinal",job.Ordinal); Property(json,"fingerprint",job.Fingerprint);
                Property(json,"parent_fingerprint",null);
                Property(json,"observation_precision",job.SubmissionPc == 0 ? "head_service_censored" : "exact_fifo_callbacks");
                Property(json,"submission_pc",job.SubmissionPc);
                Property(json,"classified",!job.Unclassified); Property(json,"service_opportunities",job.Opportunities);
                Property(json,"source",job.Source); Property(json,"destination",job.Destination);
                Property(json,"compressed_length",job.Shape.CompressedLength); Property(json,"decompressed_length",job.Shape.DestinationLength);
                Property(json,"literal_commands",job.Shape.LiteralCommands); Property(json,"short_copy_commands",job.Shape.ShortCopyCommands);
                Property(json,"long_copy_commands",job.Shape.LongCopyCommands); Property(json,"copied_output_length",job.Shape.CopiedOutputLength);
                json.WriteEndObject(); json.Flush();
            }
            output.Write('\n');
        }
        private static void Property(JsonWriter json, string name, object value)
        { json.WritePropertyName(name); json.WriteValue(value); }
        private sealed class Scope : IDisposable
        {
            private List<IDisposable> callbacks;
            public Scope(List<IDisposable> callbacks) { this.callbacks = callbacks; }
            public void Dispose()
            {
                if (callbacks == null) return;
                foreach (var callback in callbacks) callback.Dispose();
                callbacks = null;
            }
        }
    }
}
