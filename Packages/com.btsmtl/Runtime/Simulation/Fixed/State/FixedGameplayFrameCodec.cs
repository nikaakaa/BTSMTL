using System;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedGameplayFrameCodec : IDisposable
    {
        readonly FixedCharacterRuntime m_Runtime;
        readonly SimulationWorldStateSet m_Published;
        readonly SimulationWorldStateSetStorage m_ActorStorage;
        readonly WorldSimulationStateStorage m_WorldStorage;
        readonly SimulationWorldSnapshot m_Snapshot;
        readonly CanonicalReader m_Reader = new CanonicalReader(Array.Empty<byte>());
        readonly CanonicalWriter m_HashWriter;

        public FixedGameplayFrameCodec(
            FixedCharacterRuntime runtime,
            in SimulationWorldStateSet published,
            in GameplayEffectSessionCapacity capacity,
            int maximumPayloadBytes,
            int maximumSolverPayloadBytes)
        {
            m_Runtime = runtime;
            published.Retain();
            m_Published = published;
            FixedCharacterRuntimeStateCodec.PrepareStorage(runtime,
                capacity.StatePageCount, capacity.DecodedStatePageCount, maximumPayloadBytes);
            m_ActorStorage = new SimulationWorldStateSetStorage(runtime.Roster.Count);
            m_ActorStorage.PrepareCapacity(capacity.StatePageCount);
            m_WorldStorage = new WorldSimulationStateStorage(runtime.Roster.Count, maximumSolverPayloadBytes);
            m_WorldStorage.PrepareCapacity(capacity.StatePageCount);
            m_HashWriter = new CanonicalWriter(new byte[checked(maximumPayloadBytes + FixedCharacterRuntimeStateCodec.HashPrefixBytes)]);
            var actors = new SimulationActorSnapshot[runtime.Roster.Count];
            for (int index = 0; index < actors.Length; index++)
                actors[index] = new SimulationActorSnapshot(runtime.Roster[index], m_HashWriter);
            m_Snapshot = new SimulationWorldSnapshot(actors, published.WorldState);
        }

        public void Write(CanonicalWriter writer, in SimulationWorldStateSet state)
        {
#if UNITY_EDITOR
            SimulationSnapshotCaptureProfile profile = default;
#endif
            SimulationWorldSnapshotCodec.CapturePrepared(writer, m_Runtime, in state,
                true, m_Snapshot, null
#if UNITY_EDITOR
                , ref profile
#endif
                );
            m_Snapshot.ClearPreparedPayload();
        }

        public SimulationWorldStateSet Read(CanonicalReader reader)
        {
            ArraySegment<byte> bytes = reader.ReadBytesSegment();
            try
            {
                SimulationWorldSnapshotCodec.ReadPrepared(bytes.Array.AsMemory(bytes.Offset, bytes.Count),
                    m_Reader, m_Snapshot, reader.TextResolver);
                return SimulationWorldStateStore.DecodeSnapshot(m_Snapshot, m_Runtime, in m_Published,
                    m_ActorStorage, m_WorldStorage);
            }
            finally
            {
                m_Snapshot.ClearPreparedPayload();
                m_Reader.Reset(Array.Empty<byte>());
                m_Reader.SetTextResolver(null);
            }
        }

        public void Dispose()
        {
            m_Snapshot.ClearPreparedPayload();
            m_Published.Release();
            for (int index = 0; index < m_Runtime.Roster.Count; index++)
                m_Runtime.Roster[index].RuntimeStatePages.ReleaseEncodingStorage();
            m_HashWriter.Dispose();
        }
    }
}
