using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public readonly struct SimulationSessionCheckpointIdentity
    {
        public SimulationSessionCheckpointIdentity(SimulationSessionId sessionId, SimulationTick tick)
        {
            SessionId = sessionId;
            Tick = tick;
        }

        public SimulationSessionId SessionId { get; }
        public SimulationTick Tick { get; }
        public override string ToString() => $"{SessionId}/checkpoint/{Tick.Value}";
    }

    internal readonly struct SimulationCharacterStatePayload
    {
        readonly SimulationCharacterStatePayloadStorage.Page m_Page;

        internal SimulationCharacterStatePayload(SimulationCharacterStatePayloadStorage.Page page,
            ReadOnlyMemory<byte> bytes, CharacterStateHash hash)
        {
            m_Page = page;
            Bytes = bytes;
            Hash = hash;
        }

        internal ReadOnlyMemory<byte> Bytes { get; }
        internal CharacterStateHash Hash { get; }
        internal void Retain() => m_Page.Retain();
        internal void Release() => m_Page?.Release();
    }

    internal sealed class SimulationCharacterStatePayloadStorage
    {
        internal sealed class Page : IDisposable
        {
            int m_Retainers;

            internal Page(int maximumPayloadBytes, int hashPrefixBytes, int actorCount)
            {
                Writer = new CanonicalWriter(new byte[checked(maximumPayloadBytes +
                    hashPrefixBytes * actorCount)]);
            }

            internal CanonicalWriter Writer { get; }
            internal bool IsReleased => m_Retainers == 0;
            internal void Retain() => m_Retainers++;
            public void Dispose() => Release();

            internal void Release()
            {
                if (--m_Retainers != 0)
                    return;
                Writer.Reset();
            }
        }

        readonly Page[] m_Pages;

        internal SimulationCharacterStatePayloadStorage(int pageCount, int maximumPayloadBytes,
            int hashPrefixBytes, int actorCount)
        {
            m_Pages = new Page[pageCount];
            for (int index = 0; index < m_Pages.Length; index++)
                m_Pages[index] = new Page(maximumPayloadBytes, hashPrefixBytes, actorCount);
        }

        internal Page Acquire()
        {
            for (int index = 0; index < m_Pages.Length; index++)
            {
                Page page = m_Pages[index];
                if (!page.IsReleased)
                    continue;
                page.Retain();
                return page;
            }
            throw new InvalidOperationException("Character canonical payload storage exceeded its prepared state capacity.");
        }
    }

    internal readonly struct SimulationSessionCheckpointPayloadLayout
    {
        internal SimulationSessionCheckpointPayloadLayout(SimulationSessionCompositionDescriptor descriptor,
            in GameplayEffectSessionCapacity capacity,
            int maximumDetachedTextPayloads, int stepSnapshotCapacity)
        {
            PayloadPageCount = checked(capacity.EncodedCheckpoints + capacity.CapturePayloads + capacity.RestorePayloads);
            int activePayloadPages = checked(PayloadPageCount + stepSnapshotCapacity);
            if (activePayloadPages > 0 && descriptor.MaximumSessionCheckpointPayloadBytes <= 0)
                throw new InvalidOperationException("Session checkpoint storage requires MaximumSessionCheckpointPayloadBytes.");
            MaximumPayloadBytes = activePayloadPages == 0 ? 0 : descriptor.MaximumSessionCheckpointPayloadBytes;
            RetainedPayloadPageCount = activePayloadPages == 0 ? 0 : checked(
                activePayloadPages + capacity.DecodedStatePageCount + maximumDetachedTextPayloads);
        }

        internal static int MaximumNonEmptyTextValues(int maximumPayloadBytes,
            long scalarTextValues, long exitTextValues, long treeTextValues)
        {
            long remainingBytes = maximumPayloadBytes;
            long scalars = Math.Min(scalarTextValues, remainingBytes / (sizeof(int) + sizeof(char)));
            remainingBytes -= scalars * (sizeof(int) + sizeof(char));
            long exits = Math.Min(exitTextValues, remainingBytes / (sizeof(int) * 2 + sizeof(char)));
            remainingBytes -= exits * (sizeof(int) * 2 + sizeof(char));
            long trees = Math.Min(treeTextValues, remainingBytes /
                (sizeof(int) + sizeof(char) + (sizeof(int) + sizeof(ulong) * 2) / 4));
            return checked((int)(scalars + exits + trees));
        }

        internal readonly int PayloadPageCount;
        internal readonly int MaximumPayloadBytes;
        internal readonly int RetainedPayloadPageCount;
    }

    internal sealed class SimulationSessionCheckpointPayloadStorage
    {
        internal sealed class Page
        {
            internal Page(SimulationSessionCheckpointPayloadStorage owner, int index,
                SimulationSessionCompositionDescriptor descriptor)
            {
                Owner = owner;
                Index = index;
                Reader = new CanonicalReader(Array.Empty<byte>());
                PreparedCheckpoint = new SimulationSessionCheckpoint(descriptor);
            }

            internal readonly SimulationSessionCheckpointPayloadStorage Owner;
            internal readonly int Index;
            internal CanonicalBytePayloadStorage.Page Payload;
            internal byte[] Buffer => Payload.Buffer;
            internal CanonicalWriter Writer => Payload.Writer;
            internal readonly CanonicalReader Reader;
            internal ICanonicalTextResolver Text => Payload;
            internal readonly SimulationSessionCheckpoint PreparedCheckpoint;
            internal SimulationSessionCheckpoint Lease;
            internal Action ClearDescriptors;
            internal int Retainers;

            internal void Retain() => Retainers++;

            internal void Release()
            {
                if (--Retainers != 0)
                    return;
                Lease.ClearPayload();
                ClearDescriptors();
                Reader.SetTextResolver(null);
                Reader.Reset(Array.Empty<byte>());
                Payload.Release();
                Payload = null;
                Lease = null;
            }
        }

        readonly Page[] m_Pages;
        internal CanonicalBytePayloadStorage ByteStorage { get; }

        internal SimulationSessionCheckpointPayloadStorage(
            SimulationSessionCompositionDescriptor descriptor, in GameplayEffectSessionCapacity capacity,
            int maximumDetachedTextPayloads, int stepSnapshotCapacity)
            : this(descriptor, new SimulationSessionCheckpointPayloadLayout(descriptor, in capacity,
                maximumDetachedTextPayloads, stepSnapshotCapacity))
        {
        }

        internal SimulationSessionCheckpointPayloadStorage(SimulationSessionCompositionDescriptor descriptor, int pageCount,
            CanonicalBytePayloadStorage byteStorage)
        {
            ByteStorage = byteStorage;
            m_Pages = new Page[pageCount];
            for (int index = 0; index < m_Pages.Length; index++)
                m_Pages[index] = new Page(this, index, descriptor);
        }

        SimulationSessionCheckpointPayloadStorage(SimulationSessionCompositionDescriptor descriptor,
            SimulationSessionCheckpointPayloadLayout layout)
            : this(descriptor, layout.PayloadPageCount,
                new CanonicalBytePayloadStorage(layout.RetainedPayloadPageCount, layout.MaximumPayloadBytes))
        {
        }

        internal int Count => m_Pages.Length;
        internal Page this[int index] => m_Pages[index];

        internal Page Acquire()
        {
            for (int index = 0; index < m_Pages.Length; index++)
            {
                Page page = m_Pages[index];
                if (page.Lease != null)
                    continue;
                page.Payload = ByteStorage.Acquire();
                page.Lease = page.PreparedCheckpoint;
                page.Retainers = 1;
                return page;
            }
            throw new InvalidOperationException("Session checkpoint encoded payload capacity is exhausted.");
        }

        internal Page Receive(SimulationSessionCheckpoint checkpoint)
        {
            Page page = Acquire();
            if (checkpoint.Payload.Length > page.Buffer.Length)
            {
                page.Release();
                throw new InvalidOperationException("Session checkpoint payload exceeds MaximumSessionCheckpointPayloadBytes.");
            }
            checkpoint.Payload.Span.CopyTo(page.Buffer);
            page.Lease = checkpoint;
            checkpoint.AdoptPayloadPage(page);
            return page;
        }

        internal void Dispose()
        {
            for (int index = 0; index < m_Pages.Length; index++)
            {
                SimulationSessionCheckpoint lease = m_Pages[index].Lease;
                lease?.Dispose();
            }
        }
    }

    public sealed class SimulationSessionCheckpoint : IDisposable
    {
        ReadOnlyMemory<byte> m_Payload;
        SimulationSessionCheckpointPayloadStorage.Page m_Page;
        string m_SnapshotId;
        readonly SimulationRestoreDirective m_RestoreDirective;

        public SimulationSessionCheckpoint(
            SimulationSessionId sessionId,
            SimulationTick tick,
            GameplayContentHash gameplayContentHash,
            SimulationPipelineHash pipelineHash,
            string backendId,
            string backendSemanticVersion,
            string snapshotId,
            StableHash snapshotHash,
            byte[] payload)
        {
            if (!sessionId.IsValid || !tick.IsValid || !gameplayContentHash.IsValid ||
                !pipelineHash.IsValid || !snapshotHash.IsValid || payload == null || payload.Length == 0)
            {
                throw new ArgumentException("Simulation Session checkpoint identity is incomplete.");
            }
            SessionId = sessionId;
            Tick = tick;
            GameplayContentHash = gameplayContentHash;
            PipelineHash = pipelineHash;
            BackendId = SimulationIdentity.Require(backendId, nameof(backendId));
            BackendSemanticVersion = SimulationIdentity.Require(backendSemanticVersion, nameof(backendSemanticVersion));
            m_SnapshotId = SimulationIdentity.Require(snapshotId, nameof(snapshotId));
            SnapshotHash = snapshotHash;
            m_Payload = payload;
            m_RestoreDirective = new SimulationRestoreDirective(this);
        }

        public SimulationSessionCheckpoint(
            SimulationSessionCompositionDescriptor descriptor)
        {
            SessionId = descriptor.SessionId;
            GameplayContentHash = descriptor.GameplayContentHash;
            PipelineHash = descriptor.Pipeline.Hash;
            BackendId = descriptor.ExecutionBackend.ComponentId;
            BackendSemanticVersion = descriptor.ExecutionBackend.SemanticVersion;
            m_RestoreDirective = new SimulationRestoreDirective(this);
        }

        public SimulationSessionId SessionId { get; }
        public SimulationTick Tick { get; private set; }
        public GameplayContentHash GameplayContentHash { get; }
        public SimulationPipelineHash PipelineHash { get; }
        public string BackendId { get; }
        public string BackendSemanticVersion { get; }
        public SimulationSessionCheckpointIdentity Identity => new SimulationSessionCheckpointIdentity(SessionId, Tick);
        public string SnapshotId => m_SnapshotId ??= Identity.ToString();
        public StableHash SnapshotHash { get; private set; }
        public ReadOnlyMemory<byte> Payload => m_Payload;
        internal SimulationSessionCheckpointPayloadStorage.Page PayloadPage => m_Page;
        internal SimulationRestoreDirective RestoreDirective => m_RestoreDirective;

        public void BindRecordedFrame(ulong tick, StableHash hash, ReadOnlyMemory<byte> payload)
        {
            Tick = new SimulationTick(tick);
            SnapshotHash = hash;
            m_Payload = payload;
        }

        internal void CommitPayload(SimulationSessionCheckpointPayloadStorage.Page page, SimulationTick tick, StableHash hash)
        {
            m_Page = page;
            Tick = tick;
            SnapshotHash = hash;
            m_Payload = page.Buffer.AsMemory(0, checked((int)page.Writer.Length));
        }

        internal void AdoptPayloadPage(SimulationSessionCheckpointPayloadStorage.Page page)
        {
            m_Page = page;
            m_Payload = page.Buffer.AsMemory(0, m_Payload.Length);
        }

        internal void ClearPayload()
        {
            m_Page = null;
            m_Payload = ReadOnlyMemory<byte>.Empty;
            m_SnapshotId = null;
        }

        public void Dispose()
        {
            SimulationSessionCheckpointPayloadStorage.Page page = m_Page;
            if (page != null)
                page.Release();
            else
                ClearPayload();
        }
    }

    public interface ISimulationSessionCheckpointRuntime
    {
        SimulationSessionCheckpoint CaptureCheckpoint();
        void RestoreCheckpoint(SimulationSessionCheckpoint checkpoint);
    }

    public interface ISimulationSessionFrameHistoryRuntime
    {
        void PrepareRecordedFrames(int capacity);
        ReadOnlyMemory<byte> RecordFrame(ulong presentationFrame);
        void RestoreRecordedControlInput(ArraySegment<byte> state);
        void RestoreRecordedControlSources(ArraySegment<byte> state);
        bool TryGetRecordedFrameTick(ulong presentationFrame, out ulong tick);
        SimulationSessionCheckpoint CaptureRecordedCheckpoint(ulong presentationFrame);
        void ClearRecordedFrames();
    }

    public interface ISimulationSessionInputReplayRuntime
    {
        bool IsInputRecording { get; }
        bool IsInputReplaying { get; }
        bool TryStartInputRecording(out string error);
        bool TryStopInputRecording(out string error);
        bool TryCancelInputReplay(out string error);
        bool TryReplayInputRange(
            SimulationSessionCheckpoint checkpoint,
            ulong fromTick,
            ulong toTick,
            out string error);
    }
}
