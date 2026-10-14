using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Snapshots
{
    public sealed class Float32MemorySnapshotProvider : IFloat32SimulationSessionSnapshotProvider
    {
        class WorldPage : IValueResources
        {
            internal WorldPage(GameplayContentHash content, StableHash schema) =>
                World = new SimulationWorldSnapshot(content, schema, this);

            internal readonly SimulationWorldSnapshot World;
            internal int References;

            internal void Begin(in SimulationWorldStateSet state)
            {
                References = 1;
                state.Retain();
                World.SetPrepared(state);
            }

            public void Retain() => References++;

            public void Release()
            {
                if (--References != 0) return;
                World.State.Release();
                World.SetPrepared(null);
            }
        }

        sealed class CheckpointPage : WorldPage
        {
            internal CheckpointPage(SimulationSessionCompositionDescriptor descriptor, Float32CharacterRuntime runtime,
                int participantCount) : base(runtime.GameplayContentHash, runtime.StateSchemaHash)
            {
                Participants = new SimulationPipelinePassStateSnapshot[participantCount];
                Pipeline = new SimulationPipelineStateSnapshot(descriptor.Pipeline, descriptor.ExecutionBackend, Participants);
                Snapshot = new Float32SimulationSessionSnapshot(descriptor.Identity);
                Checkpoint = new SimulationSessionCheckpoint(descriptor);
            }

            internal readonly SimulationPipelinePassStateSnapshot[] Participants;
            internal readonly SimulationPipelineStateSnapshot Pipeline;
            internal readonly Float32SimulationSessionSnapshot Snapshot;
            internal readonly SimulationSessionCheckpoint Checkpoint;
        }

        WorldPage[] m_Steps = Array.Empty<WorldPage>();
        CheckpointPage[] m_Checkpoints = Array.Empty<CheckpointPage>();
        IReadOnlyList<ISimulationPipelineStateParticipant> m_Participants;

        public SimulationComponentIdentity Identity { get; } = new SimulationComponentIdentity(
            SimulationComponentRole.SnapshotProvider, "float32-memory-snapshot", "1.0.0",
            StableHash.Compute("float32-memory-snapshot/1"));

        public void PrepareCheckpointStorage(SimulationSessionCompositionDescriptor descriptor, Float32CharacterRuntime runtime,
            IReadOnlyList<ISimulationPipelineStateParticipant> participants, in GameplayEffectSessionCapacity capacity,
            int stepSnapshotCapacity)
        {
            m_Participants = participants;
            int checkpoints = checked(capacity.RetainedCheckpoints + capacity.CaptureStates + capacity.RestoreStates);
            m_Checkpoints = new CheckpointPage[checkpoints];
            for (int index = 0; index < m_Checkpoints.Length; index++)
                m_Checkpoints[index] = new CheckpointPage(descriptor, runtime, participants.Count);
            m_Steps = new WorldPage[stepSnapshotCapacity];
            for (int index = 0; index < m_Steps.Length; index++)
                m_Steps[index] = new WorldPage(runtime.GameplayContentHash, runtime.StateSchemaHash);
        }

        public SimulationWorldSnapshot CaptureStepWorldSnapshot(in SimulationWorldStateSet state)
        {
            WorldPage page = Acquire(m_Steps);
            page.Begin(in state);
            return page.World;
        }

        public SimulationSessionCheckpoint CaptureCheckpoint(in SimulationWorldStateSet state,
            SimulationPipelinePassStateSnapshot[] recordedParticipants = null)
        {
            CheckpointPage page = Acquire(m_Checkpoints);
            page.Begin(in state);
            int captured = 0;
            try
            {
                for (; captured < page.Participants.Length; captured++)
                {
                    SimulationPipelinePassStateSnapshot snapshot;
                    if (recordedParticipants == null)
                        snapshot = m_Participants[captured].CaptureState();
                    else
                    {
                        snapshot = recordedParticipants[captured];
                        snapshot.RetainStepStorage();
                    }
                    page.Participants[captured] = snapshot;
                }
                page.Pipeline.SetPreparedTick(state.LastCompletedTick);
                page.Snapshot.SetPrepared(page.World, page.Pipeline);
                page.Checkpoint.BindState(new SimulationTick(state.LastCompletedTick), page.Snapshot);
                return page.Checkpoint;
            }
            catch
            {
                for (int index = 0; index < captured; index++)
                    page.Participants[index].ReleaseStepStorage();
                Array.Clear(page.Participants, 0, captured);
                page.World.ReleaseStateStorage();
                throw;
            }
        }

        public Float32SimulationSessionSnapshot ReadCheckpoint(SimulationSessionCheckpoint checkpoint) =>
            (Float32SimulationSessionSnapshot)checkpoint.State;

        public SimulationWorldStateSet ReadWorldSnapshot(SimulationWorldSnapshot snapshot)
        {
            SimulationWorldStateSet state = snapshot.State;
            state.Retain();
            return state;
        }

        public void RequireRestore(SimulationSessionCompositionDescriptor descriptor,
            SimulationRestoreDirective directive, Float32SimulationSessionSnapshot snapshot)
        {
            if (!snapshot.CompositionIdentity.Equals(descriptor.Identity) || snapshot.Tick != directive.Tick)
                throw new InvalidOperationException("Memory snapshot does not match the active Session composition or requested Tick.");
        }

        public void DisposeCheckpointStorage()
        {
            m_Steps = Array.Empty<WorldPage>();
            m_Checkpoints = Array.Empty<CheckpointPage>();
            m_Participants = null;
        }

        static TPage Acquire<TPage>(TPage[] pages) where TPage : WorldPage
        {
            for (int index = 0; index < pages.Length; index++)
                if (pages[index].References == 0)
                    return pages[index];
            throw new InvalidOperationException("Memory snapshot pages exceed the prepared retention capacity.");
        }
    }
}
