using System;

namespace BTSMTL.Diagnostics.Editor
{
    public enum RuntimeNodeExecutionPhase
    {
        Running,
        Succeeded,
        Failed,
        Stopping,
        Stopped,
        ForceStopped,
        Waiting
    }

    public readonly struct RuntimeNodeExecutionObservation
    {
        RuntimeNodeExecutionObservation(in RuntimeDebugEventView source, RuntimeNodeExecutionPhase phase)
        {
            Event = source;
            Phase = phase;
        }

        public RuntimeDebugEventView Event { get; }
        public RuntimeNodeExecutionPhase Phase { get; }
        public bool IsTerminal => Phase is RuntimeNodeExecutionPhase.Succeeded or RuntimeNodeExecutionPhase.Failed or
            RuntimeNodeExecutionPhase.Stopped or RuntimeNodeExecutionPhase.ForceStopped;

        public static bool TryCreate(in RuntimeDebugEventView source, out RuntimeNodeExecutionObservation observation)
        {
            observation = default;
            RuntimeNodeExecutionPhase phase;
            switch (source.Event.Kind)
            {
                case RuntimeTraceEventKind.NodeEntered:
                case RuntimeTraceEventKind.NodeRunning:
                    phase = RuntimeNodeExecutionPhase.Running;
                    break;
                case RuntimeTraceEventKind.NodeWaiting:
                    phase = RuntimeNodeExecutionPhase.Waiting;
                    break;
                case RuntimeTraceEventKind.NodeCompleted:
                    if (string.Equals(source.Event.GetDetail(), "Success", StringComparison.Ordinal))
                        phase = RuntimeNodeExecutionPhase.Succeeded;
                    else if (string.Equals(source.Event.GetDetail(), "Failure", StringComparison.Ordinal))
                        phase = RuntimeNodeExecutionPhase.Failed;
                    else
                        return false;
                    break;
                case RuntimeTraceEventKind.NodeStopRequested:
                case RuntimeTraceEventKind.NodeStopping:
                    phase = RuntimeNodeExecutionPhase.Stopping;
                    break;
                case RuntimeTraceEventKind.NodeStopped:
                    phase = RuntimeNodeExecutionPhase.Stopped;
                    break;
                case RuntimeTraceEventKind.NodeForceStopped:
                    phase = RuntimeNodeExecutionPhase.ForceStopped;
                    break;
                default:
                    return false;
            }
            observation = new RuntimeNodeExecutionObservation(source, phase);
            return true;
        }
    }
}
