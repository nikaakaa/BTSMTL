using System;

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

    public sealed class SimulationSessionCheckpoint : IDisposable
    {
        IValueResources m_State;
        string m_SnapshotId;
        readonly SimulationRestoreDirective m_RestoreDirective;

        public SimulationSessionCheckpoint(SimulationSessionCompositionDescriptor descriptor)
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
        public IValueResources State => m_State;
        internal SimulationRestoreDirective RestoreDirective => m_RestoreDirective;

        public void BindState(SimulationTick tick, IValueResources ownedState)
        {
            Tick = tick;
            m_State = ownedState;
            m_SnapshotId = null;
        }

        public void Dispose()
        {
            IValueResources state = m_State;
            m_State = null;
            m_SnapshotId = null;
            state?.Release();
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
        IValueResources RecordFrame(ulong presentationFrame);
        void RestoreRecordedControlInput(IValueResources state);
        void RestoreRecordedControlSources(IValueResources state);
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
