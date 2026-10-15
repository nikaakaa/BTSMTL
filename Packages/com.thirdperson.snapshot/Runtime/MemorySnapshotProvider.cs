using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Snapshots
{
    [ValueImplementationIdentity]
    public partial class MemorySnapshotProvider<TState, TWorld, TSnapshot>
        where TState : ISimulationSnapshotState
        where TWorld : class, ISimulationWorldSnapshot<TState>
        where TSnapshot : class, ISimulationSessionSnapshot<TWorld>
    {
        class WorldPage : IValueResources
        {
            internal WorldPage(GameplayContentHash content, StableHash schema,
                Func<GameplayContentHash, StableHash, IValueResources, TWorld> createWorld) =>
                World = createWorld(content, schema, this);
            internal readonly TWorld World;
            internal int References;

            internal void Begin(in TState state)
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

        sealed class CapturePage : WorldPage
        {
            internal CapturePage(SimulationSessionCompositionDescriptor descriptor, int participantCount,
                TSnapshot snapshot, bool checkpoint, GameplayContentHash content, StableHash schema,
                Func<GameplayContentHash, StableHash, IValueResources, TWorld> createWorld) : base(content, schema, createWorld)
            {
                Participants = new SimulationPipelinePassStateSnapshot[participantCount];
                Pipeline = new SimulationPipelineStateSnapshot(descriptor.Pipeline, descriptor.ExecutionBackend, Participants);
                Snapshot = snapshot;
                if (checkpoint) Checkpoint = new SimulationSessionCheckpoint(descriptor);
            }

            internal readonly SimulationPipelinePassStateSnapshot[] Participants;
            internal readonly SimulationPipelineStateSnapshot Pipeline;
            internal readonly TSnapshot Snapshot;
            internal readonly SimulationSessionCheckpoint Checkpoint;
        }

        readonly Func<GameplayContentHash, StableHash, IValueResources, TWorld> m_CreateWorld;
        readonly Func<SimulationSessionCompositionIdentity, TSnapshot> m_CreateSnapshot;
        readonly bool m_StepParticipantsIncluded;
        WorldPage[] m_Steps = Array.Empty<WorldPage>();
        CapturePage[] m_Checkpoints = Array.Empty<CapturePage>();
        IReadOnlyList<ISimulationPipelineStateParticipant> m_Participants;
        ISimulationPipelineStateParticipant[] m_StepParticipants;

        public MemorySnapshotProvider(Func<GameplayContentHash, StableHash, IValueResources, TWorld> createWorld,
            Func<SimulationSessionCompositionIdentity, TSnapshot> createSnapshot, bool stepParticipantsIncluded)
        {
            m_CreateWorld = createWorld;
            m_CreateSnapshot = createSnapshot;
            m_StepParticipantsIncluded = stepParticipantsIncluded;
            Identity = CreateIdentity(stepParticipantsIncluded);
        }

        public SimulationComponentIdentity Identity { get; }

        static SimulationComponentIdentity CreateIdentity(bool stepParticipantsIncluded)
        {
            Type provider = typeof(MemorySnapshotProvider<TState, TWorld, TSnapshot>);
            string build = string.Concat(ImplementationIdentity, provider.FullName, stepParticipantsIncluded.ToString());
            return new SimulationComponentIdentity(SimulationComponentRole.SnapshotProvider, provider.FullName,
                provider.Assembly.GetName().Version.ToString(), StableHash.Compute(build));
        }

        protected void PrepareCheckpointStorage(SimulationSessionCompositionDescriptor descriptor,
            GameplayContentHash content, StableHash schema, IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            in GameplayEffectSessionCapacity capacity, int stepSnapshotCapacity)
        {
            m_Participants = participants;
            int included = 0;
            if (m_StepParticipantsIncluded)
                for (int index = 0; index < participants.Count; index++)
                    if (participants[index].StepProjectionMode == SimulationPipelineStepProjectionMode.Include) included++;
            m_StepParticipants = new ISimulationPipelineStateParticipant[included];
            if (m_StepParticipantsIncluded)
                for (int index = 0, next = 0; index < participants.Count; index++)
                    if (participants[index].StepProjectionMode == SimulationPipelineStepProjectionMode.Include)
                        m_StepParticipants[next++] = participants[index];
            m_Checkpoints = new CapturePage[checked(capacity.RetainedCheckpoints + capacity.CaptureStates + capacity.RestoreStates)];
            for (int index = 0; index < m_Checkpoints.Length; index++)
                m_Checkpoints[index] = CreatePage(participants.Count, true);
            m_Steps = new WorldPage[stepSnapshotCapacity];
            for (int index = 0; index < m_Steps.Length; index++)
            {
                if (m_StepParticipantsIncluded) m_Steps[index] = CreatePage(included, false);
                else
                {
                    m_Steps[index] = new WorldPage(content, schema, m_CreateWorld);
                }
            }

            CapturePage CreatePage(int count, bool checkpoint)
            {
                return new CapturePage(descriptor, count, m_CreateSnapshot(descriptor.Identity), checkpoint, content, schema, m_CreateWorld);
            }
        }

        public TWorld CaptureStepWorldSnapshot(in TState state)
        {
            WorldPage page = Acquire(m_Steps);
            page.Begin(in state);
            return page.World;
        }

        public TSnapshot CaptureStepSnapshot(in TState state) =>
            Capture((CapturePage)Acquire(m_Steps), m_StepParticipants, in state, null, true).Snapshot;

        public SimulationSessionCheckpoint CaptureCheckpoint(in TState state,
            SimulationPipelinePassStateSnapshot[] recordedParticipants = null)
        {
            CapturePage page = Capture(Acquire(m_Checkpoints), m_Participants, in state, recordedParticipants, false);
            page.Checkpoint.BindState(new SimulationTick(state.LastCompletedTick), page.Snapshot);
            return page.Checkpoint;
        }

        static CapturePage Capture(CapturePage page, IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            in TState state, SimulationPipelinePassStateSnapshot[] recorded, bool step)
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
                    else snapshot = participants[captured].CaptureState();
                    page.Participants[captured] = snapshot;
                }
                page.Pipeline.SetPreparedTick(state.LastCompletedTick);
                page.Snapshot.SetPrepared(page.World, page.Pipeline);
                return page;
            }
            catch
            {
                for (int index = 0; index < captured; index++) page.Participants[index].ReleaseStepStorage();
                Array.Clear(page.Participants, 0, captured);
                page.Release();
                throw;
            }
        }

        public TSnapshot ReadCheckpoint(SimulationSessionCheckpoint checkpoint) => (TSnapshot)checkpoint.State;

        public TState ReadWorldSnapshot(TWorld snapshot)
        {
            TState state = snapshot.State;
            state.Retain();
            return state;
        }

        public void RequireRestore(SimulationSessionCompositionDescriptor descriptor,
            SimulationRestoreDirective directive, TSnapshot snapshot)
        {
            if (!snapshot.CompositionIdentity.Equals(descriptor.Identity) || snapshot.Tick != directive.Tick)
                throw new InvalidOperationException("Memory snapshot does not match the active Session composition or requested Tick.");
        }

        public void DisposeCheckpointStorage()
        {
            m_Steps = Array.Empty<WorldPage>();
            m_Checkpoints = Array.Empty<CapturePage>();
            m_Participants = null;
            m_StepParticipants = null;
        }

        static TPage Acquire<TPage>(TPage[] pages) where TPage : WorldPage
        {
            for (int index = 0; index < pages.Length; index++)
                if (pages[index].References == 0) return pages[index];
            throw new InvalidOperationException("Memory snapshot pages exceed the prepared retention capacity.");
        }
    }

    public sealed class FPMemorySnapshotProvider : MemorySnapshotProvider<FPGameplay.SimulationWorldStateSet,
        FPGameplay.SimulationWorldSnapshot, FPGameplay.FPSimulationSessionSnapshot>, FPGameplay.IFPSimulationSessionSnapshotProvider
    {
        public FPMemorySnapshotProvider() : base((content, schema, storage) => new FPGameplay.SimulationWorldSnapshot(content, schema, storage),
            identity => new FPGameplay.FPSimulationSessionSnapshot(identity), true) { }

        public void PrepareCheckpointStorage(SimulationSessionCompositionDescriptor descriptor, FPGameplay.FPCharacterRuntime runtime,
            IReadOnlyList<ISimulationPipelineStateParticipant> participants, in GameplayEffectSessionCapacity capacity, int stepSnapshotCapacity) =>
            PrepareCheckpointStorage(descriptor, runtime.GameplayContentHash, runtime.StateSchemaHash, participants, in capacity, stepSnapshotCapacity);
    }

    public sealed class Float32MemorySnapshotProvider : MemorySnapshotProvider<SimulationWorldStateSet,
        SimulationWorldSnapshot, Float32SimulationSessionSnapshot>, IFloat32SimulationSessionSnapshotProvider
    {
        public Float32MemorySnapshotProvider() : base((content, schema, storage) => new SimulationWorldSnapshot(content, schema, storage),
            identity => new Float32SimulationSessionSnapshot(identity), false) { }

        public void PrepareCheckpointStorage(SimulationSessionCompositionDescriptor descriptor, Float32CharacterRuntime runtime,
            IReadOnlyList<ISimulationPipelineStateParticipant> participants, in GameplayEffectSessionCapacity capacity, int stepSnapshotCapacity) =>
            PrepareCheckpointStorage(descriptor, runtime.GameplayContentHash, runtime.StateSchemaHash, participants, in capacity, stepSnapshotCapacity);
    }
}
