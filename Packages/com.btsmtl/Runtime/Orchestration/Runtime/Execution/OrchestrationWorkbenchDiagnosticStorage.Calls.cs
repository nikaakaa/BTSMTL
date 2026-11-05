using System;

namespace BTSMTL.Orchestration
{
    public sealed partial class OrchestrationWorkbenchDiagnosticStorage<TValue> where TValue : unmanaged
    {
        struct CallOrigin
        {
            internal ulong Sequence;
            internal ulong ActionInstanceId;
            internal ulong ExecutionGeneration;
            internal ulong InvocationGeneration;
            internal ulong PlaybackIdentity;
            internal Guid ExecutionBranchId;
            internal ulong RuntimeEpoch;

            internal readonly bool Matches(in OrchestrationExecutionFrameState frame, ulong invocationGeneration) =>
                Sequence != 0 && ActionInstanceId == frame.ActionInstanceId && ExecutionGeneration == frame.Generation &&
                PlaybackIdentity == frame.TimelineStart.PlaybackIdentity && InvocationGeneration == invocationGeneration;

            internal readonly bool Matches(in OrchestrationExecutionFrameState frame) =>
                Sequence != 0 && ActionInstanceId == frame.ActionInstanceId && ExecutionGeneration == frame.Generation &&
                PlaybackIdentity == frame.TimelineStart.PlaybackIdentity;
        }

        OrchestrationWorkbenchDiagnosticStorage<TValue>[] m_CallPrograms;
        CallOrigin[] m_CallOrigins;
        int[] m_OperationCallOrigins;
        Guid m_CallBranch;
        ulong m_CallEpoch;
        ulong m_CallSequence;

        public void BindCallOrigins(OrchestrationWorkbenchDiagnosticStorage<TValue>[] programs)
        {
            PrepareInvocationBindings();
            m_CallPrograms = programs;
            m_CallOrigins = new CallOrigin[checked(m_ExecutionFrameCapacity * m_TraceInvocations.Length)];
            m_OperationCallOrigins = new int[checked(m_ExecutionFrameCapacity * m_Program.OperationCount)];
            m_OperationCallOrigins.AsSpan().Fill(-1);
        }

        public void SetCallOrigin(Guid executionBranchId, ulong runtimeEpoch)
        {
            if (m_CallBranch == executionBranchId && m_CallEpoch == runtimeEpoch)
                return;
            m_CallBranch = executionBranchId;
            m_CallEpoch = runtimeEpoch;
            Array.Clear(m_CallOrigins, 0, m_CallOrigins.Length);
            m_OperationCallOrigins.AsSpan().Fill(-1);
        }

        void ObserveCall<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, OrchestrationTraceKind kind)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            if (m_CallPrograms == null || cursor.IsBorrowedOperation(operationIndex))
                return;
            bool lifecycleOwner = kind is OrchestrationTraceKind.Enter or OrchestrationTraceKind.Running or
                OrchestrationTraceKind.Complete or OrchestrationTraceKind.StopRequested or
                OrchestrationTraceKind.Stopped or OrchestrationTraceKind.ForceStopped;
            int invocationIndex = ResolveTraceInvocation(ref cursor, operationIndex, lifecycleOwner);
            ref readonly TraceInvocation invocation = ref m_TraceInvocations[invocationIndex];
            int frameSlot = cursor.ExecutionFrameOf(operationIndex);
            ref readonly OrchestrationExecutionFrameState frame = ref cursor.ReadExecutionFrame(operationIndex)[0];
            ulong generation = ReadInvocationGeneration(ref cursor, invocationIndex, operationIndex, out _);
            int originIndex = checked(frameSlot * m_TraceInvocations.Length + invocationIndex);
            ref CallOrigin origin = ref m_CallOrigins[originIndex];
            if (kind == OrchestrationTraceKind.Enter && operationIndex == invocation.EntryOperationIndex &&
                !origin.Matches(in frame, generation))
            {
                origin = new CallOrigin
                {
                    Sequence = checked(++m_CallSequence), ActionInstanceId = frame.ActionInstanceId,
                    ExecutionGeneration = frame.Generation, InvocationGeneration = generation,
                    PlaybackIdentity = frame.TimelineStart.PlaybackIdentity,
                    ExecutionBranchId = m_CallBranch, RuntimeEpoch = m_CallEpoch
                };
            }
            m_OperationCallOrigins[checked(frameSlot * m_Program.OperationCount + operationIndex)] =
                origin.Matches(in frame, generation) ? originIndex : -1;
        }

        void ReadCall<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, int invocationIndex, int parentIndex, ref OrchestrationTraceContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            context.CallSequence = 0;
            context.CallExecutionBranchId = Guid.Empty;
            context.CallRuntimeEpoch = 0;
            context.ParentCallSequence = 0;
            context.ParentProgramIndex = -1;
            context.ParentExecutionBranchId = Guid.Empty;
            context.ParentRuntimeEpoch = 0;
            context.ParentCallAvailability = OrchestrationTraceAvailability.Unavailable;
            if (m_CallPrograms == null || cursor.IsBorrowedOperation(operationIndex))
                return;
            int frameSlot = cursor.ExecutionFrameOf(operationIndex);
            ref readonly OrchestrationExecutionFrameState frame = ref cursor.ReadExecutionFrame(operationIndex)[0];
            ref readonly CallOrigin own = ref m_CallOrigins[checked(frameSlot * m_TraceInvocations.Length + invocationIndex)];
            if (own.Matches(in frame, context.GraphInvocationGeneration))
            {
                context.CallSequence = own.Sequence;
                context.CallExecutionBranchId = own.ExecutionBranchId;
                context.CallRuntimeEpoch = own.RuntimeEpoch;
            }
            if (parentIndex >= 0)
            {
                int parentOperation = m_TraceInvocations[parentIndex].OwnerOperationIndex;
                int parentFrame = cursor.ExecutionFrameOf(parentOperation);
                ref readonly OrchestrationExecutionFrameState header = ref cursor.ReadExecutionFrame(parentOperation)[0];
                ref readonly CallOrigin parent = ref m_CallOrigins[checked(parentFrame * m_TraceInvocations.Length + parentIndex)];
                if (parent.Matches(in header, context.ParentInvocationGeneration))
                    SetParent(ref context, in parent, cursor.DiagnosticProgramIndex(parentFrame));
                return;
            }
            if (frame.ParentTimelineOperationIndex < 0)
            {
                context.ParentCallAvailability = OrchestrationTraceAvailability.NotApplicable;
                return;
            }
            int callerFrame = frame.ParentFrameIndex;
            int callerProgram = cursor.DiagnosticProgramIndex(callerFrame);
            OrchestrationWorkbenchDiagnosticStorage<TValue> diagnostics = m_CallPrograms[callerProgram];
            int callerIndex = diagnostics.m_OperationCallOrigins[checked(callerFrame *
                diagnostics.m_Program.OperationCount + frame.ParentTimelineOperationIndex)];
            if (callerIndex < 0)
                return;
            ref readonly CallOrigin caller = ref diagnostics.m_CallOrigins[callerIndex];
            if (caller.Matches(in cursor.FrameHeader(callerFrame)))
                SetParent(ref context, in caller, callerProgram);
        }

        ulong ReadObservedCall<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            if (m_CallPrograms == null || cursor.IsBorrowedOperation(operationIndex))
                return 0;
            int frame = cursor.ExecutionFrameOf(operationIndex);
            int index = m_OperationCallOrigins[checked(frame * m_Program.OperationCount + operationIndex)];
            if (index < 0)
                return 0;
            ref readonly CallOrigin origin = ref m_CallOrigins[index];
            return origin.Matches(in cursor.ReadExecutionFrame(operationIndex)[0]) ? origin.Sequence : 0;
        }

        static void SetParent(ref OrchestrationTraceContext context, in CallOrigin parent, int programIndex)
        {
            context.ParentCallSequence = parent.Sequence;
            context.ParentProgramIndex = programIndex;
            context.ParentExecutionBranchId = parent.ExecutionBranchId;
            context.ParentRuntimeEpoch = parent.RuntimeEpoch;
            context.ParentCallAvailability = OrchestrationTraceAvailability.Available;
        }
    }
}
