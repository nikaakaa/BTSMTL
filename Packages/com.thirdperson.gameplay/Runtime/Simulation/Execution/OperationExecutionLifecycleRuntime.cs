using System;
using System.Collections.Generic;
using System.Globalization;

namespace ThirdPersonSimulation
{
    internal interface IOperationExecutionLifecycleHost<TTarget> : IOperationStateMachineHost<TTarget>
        where TTarget : struct, IOperationControlTarget<TTarget>
    {
        OperationControlCursor<TTarget> Cursor { get; }
        ulong ReadUInt64(int slotIndex);
        void WriteUInt64(int slotIndex, ulong value);
        void PrepareActivation(OperationExecutionDescriptor operation);
        void ActivateScopes(OperationControlCursor<TTarget> cursor, OperationExecutionDescriptor operation, ulong generation);
        void CompleteScopes(OperationExecutionDescriptor operation);
        void ResetOperationState(OperationExecutionDescriptor operation);
        OperationStopStatus ContinueLeafStop(OperationControlCursor<TTarget> cursor, OperationExecutionDescriptor operation, OperationStopContext context);
        void ForceStopLeaf(OperationControlCursor<TTarget> cursor, OperationExecutionDescriptor operation, OperationStopContext context);
        OperationExecutionResult Execute(OperationExecutionDescriptor operation);
        void RequireExecution(OperationHandle handle);
        bool DiagnosticsEnabled { get; }
        bool ControlTraceEnabled { get; }
        void EmitTrace(OperationExecutionDescriptor operation, string code, OperationControlTraceSeverity severity, string detail);
    }

    internal sealed class OperationExecutionLifecycleRuntime<TTarget>
        where TTarget : struct, IOperationControlTarget<TTarget>
    {
        readonly IOperationExecutionLifecycleHost<TTarget> m_Host;
        readonly OperationStateMachineRuntime<TTarget> m_StateMachine;
        readonly HashSet<int> m_ForceStopVisited;
        int m_ForceStopDepth;

        public OperationExecutionLifecycleRuntime(
            IOperationExecutionLifecycleHost<TTarget> host,
            OperationStateMachineRuntime<TTarget> stateMachine)
        {
            m_Host = host ?? throw new ArgumentNullException(nameof(host));
            m_StateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            m_ForceStopVisited = new HashSet<int>(m_Host.Topology.Operations.Count);
        }

        public bool HasTransientState => m_ForceStopDepth != 0 || m_ForceStopVisited.Count != 0;

        public OperationExecutionResult Tick(OperationHandle handle)
        {
            m_Host.RequireExecution(handle);
            OperationExecutionDescriptor operation = m_Host.Topology.Operation(handle);
            int lifecycleSlot = FindOperationSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            if (lifecycleSlot < 0)
                return m_Host.Execute(operation);
            var status = (OperationRunnableStatus)m_Host.ReadInt32(lifecycleSlot);
            if (status == OperationRunnableStatus.Stopping)
                return OperationExecutionResult.Running;
            bool entering = status != OperationRunnableStatus.Running;
            if (entering)
            {
                PrepareActivation(operation);
                ulong generation = IncrementGeneration(operation);
                m_Host.ActivateScopes(m_Host.Cursor, operation, generation);
                m_Host.WriteInt32(lifecycleSlot, (int)OperationRunnableStatus.Running);
                if (m_Host.DiagnosticsEnabled)
                    m_Host.EmitTrace(operation, "operation_enter", OperationControlTraceSeverity.Detail, operation.CodeName);
            }
            if (!entering && m_Host.ControlTraceEnabled)
                m_Host.EmitTrace(operation, "operation_running", OperationControlTraceSeverity.Detail, string.Empty);
            OperationExecutionResult result = m_Host.Execute(operation);
            if (result == OperationExecutionResult.Success || result == OperationExecutionResult.Failure)
            {
                m_Host.WriteInt32(
                    lifecycleSlot,
                    result == OperationExecutionResult.Success
                        ? (int)OperationRunnableStatus.Success
                        : (int)OperationRunnableStatus.Failure);
                m_Host.CompleteScopes(operation);
                if (m_Host.DiagnosticsEnabled)
                    m_Host.EmitTrace(operation, "operation_complete", OperationControlTraceSeverity.Detail, OperationTraceText.Result(result));
            }
            return result;
        }

        public OperationExecutionResult TickPersistent(OperationHandle handle)
        {
            OperationExecutionDescriptor operation = m_Host.Topology.Operation(handle);
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            if (slot >= 0)
            {
                var status = (OperationRunnableStatus)m_Host.ReadInt32(slot);
                if (status == OperationRunnableStatus.Success)
                    return OperationExecutionResult.Success;
                if (status == OperationRunnableStatus.Failure)
                    return OperationExecutionResult.Failure;
            }
            return Tick(handle);
        }

        public OperationStopStatus RequestStop(OperationHandle handle, OperationStopContext context)
        {
            if (!handle.IsValid)
                return OperationStopStatus.Completed;
            OperationExecutionDescriptor operation = m_Host.Topology.Operation(handle);
            int lifecycle = FindOperationSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            if (lifecycle < 0)
                return OperationStopStatus.Completed;
            var status = (OperationRunnableStatus)m_Host.ReadInt32(lifecycle);
            if (status == OperationRunnableStatus.Dormant ||
                status == OperationRunnableStatus.Success ||
                status == OperationRunnableStatus.Failure)
            {
                m_Host.ResetOperationState(operation);
                return OperationStopStatus.Completed;
            }
            if (status == OperationRunnableStatus.Running)
            {
                m_Host.WriteInt32(lifecycle, (int)OperationRunnableStatus.Stopping);
                WriteStopContext(operation, context);
                if (m_Host.DiagnosticsEnabled)
                    m_Host.EmitTrace(operation, "operation_stop_requested", OperationControlTraceSeverity.Detail, OperationTraceText.StopCause(context.Cause));
            }
            else if (context.IsValid)
            {
                WriteStopContext(operation, context);
            }
            return ContinueStop(handle);
        }

        public OperationStopStatus ContinueStop(OperationHandle handle)
        {
            OperationExecutionDescriptor operation = m_Host.Topology.Operation(handle);
            int lifecycle = FindOperationSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            if (lifecycle < 0 || m_Host.ReadInt32(lifecycle) != (int)OperationRunnableStatus.Stopping)
                return OperationStopStatus.Completed;
            OperationStopContext context = ReadStopContext(operation);
            OperationStopStatus status;
            switch (operation.Code)
            {
                case SimulationOperationCode.State:
                    status = m_StateMachine.ContinueStateStop(operation, context);
                    break;
                case SimulationOperationCode.StateMachine:
                    status = m_StateMachine.ContinueStateMachineStop(operation, context);
                    break;
                case SimulationOperationCode.Timeline:
                    status = m_Host.ContinueLeafStop(m_Host.Cursor, operation, context);
                    break;
                case SimulationOperationCode.SubGraph:
                    status = ContinueDirectChildStop(operation, context, ProgramControlFlowKind.Enter);
                    break;
                default:
                    status = ContinueDirectChildStop(operation, context, ProgramControlFlowKind.Child);
                    break;
            }
            if (status == OperationStopStatus.Running)
                return status;
            m_Host.CompleteScopes(operation);
            if (m_Host.DiagnosticsEnabled)
            {
                m_Host.EmitTrace(
                    operation,
                    "operation_stopped",
                    status == OperationStopStatus.Failed ? OperationControlTraceSeverity.Error : OperationControlTraceSeverity.Detail,
                    OperationTraceText.StopCause(context.Cause));
            }
            m_Host.ResetOperationState(operation);
            return status;
        }

        public void ForceStop(OperationHandle handle, OperationStopContext context)
        {
            bool ownsVisited = m_ForceStopDepth == 0;
            m_ForceStopDepth++;
            try
            {
                ForceStopCore(handle, context);
            }
            finally
            {
                m_ForceStopDepth--;
                if (ownsVisited)
                    m_ForceStopVisited.Clear();
            }
        }

        OperationStopStatus ContinueDirectChildStop(
            OperationExecutionDescriptor operation,
            OperationStopContext context,
            ProgramControlFlowKind kind = ProgramControlFlowKind.Child)
        {
            OperationStopStatus aggregate = OperationStopStatus.Completed;
            IReadOnlyList<ProgramControlFlowEdge> children = Edges(operation.Handle, kind);
            for (int i = 0; i < children.Count; i++)
            {
                if (!m_Host.IsActive(children[i].Target))
                    continue;
                OperationStopStatus status = RequestStop(children[i].Target, context);
                if (status == OperationStopStatus.Failed)
                    return status;
                if (status == OperationStopStatus.Running)
                    aggregate = status;
            }
            return aggregate;
        }

        void ForceStopCore(OperationHandle handle, OperationStopContext context)
        {
            if (!handle.IsValid || !m_ForceStopVisited.Add(handle.Value))
                return;
            OperationExecutionDescriptor operation = m_Host.Topology.Operation(handle);
            bool active = IsActive(handle);
            if (operation.Code == SimulationOperationCode.Timeline)
            {
                m_Host.ForceStopLeaf(m_Host.Cursor, operation, context);
            }
            else if (operation.Code == SimulationOperationCode.StateMachine)
            {
                m_StateMachine.ForceStopStateMachine(operation, context);
            }
            else if (operation.Code == SimulationOperationCode.State)
            {
                m_StateMachine.ForceStopState(operation, context);
            }
            else
            {
                ProgramControlFlowKind kind = operation.Code == SimulationOperationCode.SubGraph
                    ? ProgramControlFlowKind.Enter
                    : ProgramControlFlowKind.Child;
                IReadOnlyList<ProgramControlFlowEdge> children = Edges(operation.Handle, kind);
                for (int i = 0; i < children.Count; i++)
                {
                    if (IsActive(children[i].Target))
                        ForceStopCore(children[i].Target, context);
                }
            }
            if (active && m_Host.DiagnosticsEnabled)
                m_Host.EmitTrace(operation, "operation_force_stopped", OperationControlTraceSeverity.Detail, OperationTraceText.StopCause(context.Cause));
            m_Host.CompleteScopes(operation);
            m_Host.ResetOperationState(operation);
        }

        bool IsActive(OperationHandle handle)
        {
            if (!handle.IsValid)
                return false;
            OperationExecutionDescriptor operation = m_Host.Topology.Operation(handle);
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            if (slot < 0)
                return false;
            var status = (OperationRunnableStatus)m_Host.ReadInt32(slot);
            return status == OperationRunnableStatus.Running || status == OperationRunnableStatus.Stopping;
        }

        void PrepareActivation(OperationExecutionDescriptor operation)
        {
            m_Host.ResetOperationState(operation);
            int cursor = FindOperationSlot(operation, ProgramStateSemantic.RunnableChildCursor);
            if (cursor >= 0)
                m_Host.WriteInt32(cursor, operation.Code == SimulationOperationCode.Selector ? -1 : 0);
            m_Host.PrepareActivation(operation);
        }

        ulong IncrementGeneration(OperationExecutionDescriptor operation)
        {
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableActivationGeneration);
            if (slot < 0)
                return 1;
            ulong generation = checked(m_Host.ReadUInt64(slot) + 1);
            if (generation == 0)
                generation = 1;
            m_Host.WriteUInt64(slot, generation);
            return generation;
        }

        IReadOnlyList<ProgramControlFlowEdge> Edges(OperationHandle source, ProgramControlFlowKind kind) =>
            m_Host.Topology.Outgoing(source, kind);

        int FindOperationSlot(OperationExecutionDescriptor operation, ProgramStateSemantic semantic) =>
            m_Host.Topology.FindOperationStateSlot(operation.Handle, semantic);

        internal void WriteStopContext(OperationExecutionDescriptor operation, OperationStopContext context)
        {
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableStopBarrier);
            if (slot < 0)
                return;
            int replacement = context.Replacement.IsValid ? checked(context.Replacement.Value + 1) : 0;
            if (replacement > 0x007fffff)
                throw new InvalidOperationException($"Stop replacement operation '{context.Replacement}' exceeds the portable barrier range.");
            int encoded = ((int)context.Cause & 0xff) | (replacement << 8);
            m_Host.WriteInt32(slot, encoded);
        }

        internal OperationStopContext ReadStopContext(OperationExecutionDescriptor operation)
        {
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableStopBarrier);
            if (slot < 0)
                return default;
            int encoded = m_Host.ReadInt32(slot);
            var cause = (OperationStopCause)(encoded & 0xff);
            int replacement = (encoded >> 8) - 1;
            return new OperationStopContext(
                cause,
                operation.Handle,
                replacement >= 0 ? new OperationHandle(replacement) : OperationHandle.Invalid);
        }

        internal void ClearStopContext(OperationExecutionDescriptor operation)
        {
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableStopBarrier);
            if (slot >= 0)
                m_Host.WriteInt32(slot, 0);
        }
    }
}
