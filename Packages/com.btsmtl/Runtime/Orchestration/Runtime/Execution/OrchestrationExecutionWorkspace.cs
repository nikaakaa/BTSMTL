using System;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationExecutionWorkspace<TValue> where TValue : unmanaged
    {
        internal readonly TValue[][] Inputs;
        internal readonly TValue[][] Outputs;
        internal readonly bool[] ActiveValues;
        internal readonly bool[] ForceStopVisited;
        internal readonly GraphCallContext[] GraphCallEntries;
        internal readonly StateContext[] States;
        internal readonly OrchestrationStateAncestor[] StateAncestors;
        internal readonly TValue[] InputObservations;
        internal readonly bool[] InputObservationWritten;
        internal readonly TValue[] OutputObservations;
        internal readonly bool[] OutputObservationWritten;
        internal readonly OrchestrationTraceEvent<TValue>[] TraceEvents;
        internal readonly OrchestrationStateTraceFrame[] StateFrames;
        internal readonly OrchestrationOutputRequest[] OutputRequests;
        internal readonly TValue[] OutputValues;
        internal int OutputRequestCount;
        internal int OutputValueCount;

        public OrchestrationExecutionWorkspace(OrchestrationProgram<TValue> program, OrchestrationGraphContent<TValue> content,
            OrchestrationExecutionCapacity capacity, int programIndex, OrchestrationProgramTickCapacity tickCapacity,
            int traceCapacity = 0, int stateTraceFrameCapacity = 0)
        {
            int executionFrameCapacity = capacity.State.FrameCount;
            int depth = checked(executionFrameCapacity * checked(program.OperationCount + 1));
            int maximumInputs = 4;
            int maximumOutputs = 4;
            for (int i = 0; i < program.OperationCount; i++)
            {
                ref readonly OrchestrationOperation operation = ref program.Operation(i);
                maximumInputs = Math.Max(maximumInputs, operation.Inputs.Count);
                maximumOutputs = Math.Max(maximumOutputs, operation.OutputCount);
                foreach (ref readonly OrchestrationHookBinding hook in program.Hooks(i))
                    maximumInputs = Math.Max(maximumInputs, hook.Inputs.Count);
            }
            Inputs = new TValue[depth][];
            Outputs = new TValue[depth][];
            for (int i = 0; i < depth; i++)
            {
                Inputs[i] = new TValue[maximumInputs];
                Outputs[i] = new TValue[maximumOutputs];
            }
            ActiveValues = new bool[program.ValueEvaluationSlotCount];
            ForceStopVisited = new bool[checked(executionFrameCapacity * program.OperationCount)];
            GraphCallEntries = new GraphCallContext[depth];
            States = new StateContext[depth];
            StateAncestors = new OrchestrationStateAncestor[depth];
            InputObservations = new TValue[program.InputBindingCount];
            InputObservationWritten = new bool[program.InputBindingCount];
            OutputObservations = new TValue[program.ValueEvaluationSlotCount];
            OutputObservationWritten = new bool[program.ValueEvaluationSlotCount];
            TraceEvents = new OrchestrationTraceEvent<TValue>[traceCapacity];
            StateFrames = new OrchestrationStateTraceFrame[stateTraceFrameCapacity];
            OrchestrationProgramOutputCapacity output = tickCapacity.CursorOutputs;
            OutputRequests = new OrchestrationOutputRequest[output.RequestCount];
            OutputValues = new TValue[output.ValueCount];
        }

        public ReadOnlySpan<TValue> ObservedInputs => InputObservations;
        public ReadOnlySpan<bool> WrittenInputs => InputObservationWritten;
        public ReadOnlySpan<TValue> ObservedOutputs => OutputObservations;
        public ReadOnlySpan<bool> WrittenOutputs => OutputObservationWritten;
        public int OutputRequestCapacity => OutputRequests.Length;
        public int OutputValueCapacity => OutputValues.Length;

        // 宿主采用候选后消费；下一次最外层入口会重置本批输出。
        public ReadOnlySpan<OrchestrationOutputRequest> EmittedOutputRequests => OutputRequests.AsSpan(0, OutputRequestCount);
        public ReadOnlySpan<TValue> EmittedOutputValues => OutputValues.AsSpan(0, OutputValueCount);

        internal void ResetOutputs()
        {
            OutputRequestCount = 0;
            OutputValueCount = 0;
        }

        internal struct StateContext
        {
            internal int ExecutionFrameIndex;
            internal int OperationIndex;
            internal int ExitCause;
            internal bool HasRootCompletedOverride;
            internal bool RootCompletedOverride;
            internal int TraceFrameIndex;
        }

        internal struct GraphCallContext
        {
            internal int ExecutionFrameIndex;
            internal int OperationIndex;
        }
    }
}
