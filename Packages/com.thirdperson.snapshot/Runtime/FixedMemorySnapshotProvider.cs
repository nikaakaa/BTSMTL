using System;
using System.Collections.Generic;
using FixedRuntime = ThirdPersonSimulation.Fixed.FixedCharacterRuntime;
using FixedSessionSnapshot = ThirdPersonSimulation.Fixed.FixedSimulationSessionSnapshot;
using FixedWorldSnapshot = ThirdPersonSimulation.Fixed.SimulationWorldSnapshot;
using FixedStateSet = ThirdPersonSimulation.Fixed.SimulationWorldStateSet;

namespace ThirdPersonSimulation.Snapshots
{
    public sealed class FixedMemorySnapshotProvider : Fixed.IFixedSimulationSessionSnapshotProvider
    {
        sealed class Page : IValueResources
        {
            internal Page(SimulationSessionCompositionDescriptor descriptor, FixedRuntime runtime,
                int participantCount, bool checkpoint)
            {
                World = new FixedWorldSnapshot(runtime.GameplayContentHash, runtime.StateSchemaHash, this);
                Participants = new SimulationPipelinePassStateSnapshot[participantCount];
                Pipeline = new SimulationPipelineStateSnapshot(descriptor.Pipeline, descriptor.ExecutionBackend, Participants);
                Snapshot = new FixedSessionSnapshot(descriptor.Identity);
                if (checkpoint) Checkpoint = new SimulationSessionCheckpoint(descriptor);
            }

            internal readonly FixedWorldSnapshot World;
            internal readonly SimulationPipelinePassStateSnapshot[] Participants;
            internal readonly SimulationPipelineStateSnapshot Pipeline;
            internal readonly FixedSessionSnapshot Snapshot;
            internal readonly SimulationSessionCheckpoint Checkpoint;
            internal int References;

            internal void Begin(in FixedStateSet state)
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
                World.SetPrepared(default);
            }
        }

        Page[] m_Steps = Array.Empty<Page>();
        Page[] m_Checkpoints = Array.Empty<Page>();
        IReadOnlyList<ISimulationPipelineStateParticipant> m_Participants;
        ISimulationPipelineStateParticipant[] m_StepParticipants;

        public SimulationComponentIdentity Identity { get; } = new SimulationComponentIdentity(
            SimulationComponentRole.SnapshotProvider, "fixed-memory-snapshot", "1.0.0",
            StableHash.Compute("fixed-memory-snapshot/1"));

        public void PrepareCheckpointStorage(SimulationSessionCompositionDescriptor descriptor, FixedRuntime runtime,
            IReadOnlyList<ISimulationPipelineStateParticipant> participants, in GameplayEffectSessionCapacity capacity,
            int stepSnapshotCapacity)
        {
            m_Participants = participants;
            int included = 0;
            for (int index = 0; index < participants.Count; index++)
                if (participants[index].StepProjectionMode == SimulationPipelineStepProjectionMode.Include)
                    included++;
            m_StepParticipants = new ISimulationPipelineStateParticipant[included];
            for (int index = 0, next = 0; index < participants.Count; index++)
                if (participants[index].StepProjectionMode == SimulationPipelineStepProjectionMode.Include)
                    m_StepParticipants[next++] = participants[index];
            int checkpoints = checked(capacity.RetainedCheckpoints + capacity.CaptureStates + capacity.RestoreStates);
            m_Checkpoints = new Page[checkpoints];
            for (int index = 0; index < m_Checkpoints.Length; index++)
                m_Checkpoints[index] = new Page(descriptor, runtime, participants.Count, true);
            m_Steps = new Page[stepSnapshotCapacity];
            for (int index = 0; index < m_Steps.Length; index++)
                m_Steps[index] = new Page(descriptor, runtime, included, false);
        }

        public FixedSessionSnapshot CaptureStepSnapshot(in FixedStateSet state)
        {
            Page page = Capture(Acquire(m_Steps), m_StepParticipants, in state, null, true);
            return page.Snapshot;
        }

        public SimulationSessionCheckpoint CaptureCheckpoint(in FixedStateSet state,
            SimulationPipelinePassStateSnapshot[] recordedParticipants = null)
        {
            Page page = Capture(Acquire(m_Checkpoints), m_Participants, in state, recordedParticipants, false);
            page.Checkpoint.BindState(new SimulationTick(state.LastCompletedTick), page.Snapshot);
            return page.Checkpoint;
        }

        static Page Capture(Page page, IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            in FixedStateSet state, SimulationPipelinePassStateSnapshot[] recorded, bool step)
        {
            page.Begin(in state);
            int captured = 0;
            try
            {
                for (; captured < page.Participants.Length; captured++)
                {
                    SimulationPipelinePassStateSnapshot snapshot;
                    if (recorded != null)
                    {
                        snapshot = recorded[captured];
                        snapshot.RetainStepStorage();
                    }
                    else if (step && participants[captured] is ISimulationPipelineStepStateStorage storage)
                        snapshot = storage.CaptureStepState();
                    else
                        snapshot = participants[captured].CaptureState();
                    page.Participants[captured] = snapshot;
                }
                page.Pipeline.SetPreparedTick(state.LastCompletedTick);
                page.Snapshot.SetPrepared(page.World, page.Pipeline);
                return page;
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

        public FixedSessionSnapshot ReadCheckpoint(SimulationSessionCheckpoint checkpoint) =>
            (FixedSessionSnapshot)checkpoint.State;

        public FixedStateSet ReadWorldSnapshot(FixedWorldSnapshot snapshot)
        {
            FixedStateSet state = snapshot.State;
            state.Retain();
            return state;
        }

        public void RequireRestore(SimulationSessionCompositionDescriptor descriptor,
            SimulationRestoreDirective directive, FixedSessionSnapshot snapshot)
        {
            if (!snapshot.CompositionIdentity.Equals(descriptor.Identity) || snapshot.Tick != directive.Tick)
                throw new InvalidOperationException("Memory snapshot does not match the active Session composition or requested Tick.");
        }

        public void DisposeCheckpointStorage()
        {
            m_Steps = Array.Empty<Page>();
            m_Checkpoints = Array.Empty<Page>();
            m_Participants = null;
            m_StepParticipants = null;
        }

        static Page Acquire(Page[] pages)
        {
            for (int index = 0; index < pages.Length; index++)
                if (pages[index].References == 0)
                    return pages[index];
            throw new InvalidOperationException("Memory snapshot pages exceed the prepared retention capacity.");
        }
    }
}
