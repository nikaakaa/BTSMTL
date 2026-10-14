using System;
using System.Collections.Generic;
using System.Globalization;

namespace ThirdPersonSimulation
{
    public sealed class OperationControlRuntime<TTarget> : IOperationExecutionLifecycleHost<TTarget>, IOperationCompositeRuntimeHost<TTarget>, IDisposable
        where TTarget : struct, IOperationControlTarget<TTarget>
    {
        readonly OperationExecutionTopology m_Topology;
        TTarget m_Target;
        readonly OperationControlCursor<TTarget> m_Cursor;
        readonly OperationStateMachineRuntime<TTarget> m_StateMachine;
        readonly OperationExecutionLifecycleRuntime<TTarget> m_Lifecycle;
        readonly OperationCompositeRuntime<TTarget> m_Composite;
        readonly Stack<StateExecutionContext> m_StateExecution;
        readonly int m_MaxExecutionCount;
        int m_ExecutionCount;
        CanonicalTextReferences m_ExecutionPathText;
        char[] m_ExecutionPathCharacters;

        public OperationControlRuntime(OperationExecutionTopology topology, TTarget target, int maxExecutionCount)
        {
            m_Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            if (maxExecutionCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExecutionCount));
            m_Target = target;
            m_MaxExecutionCount = maxExecutionCount;
            m_StateExecution = new Stack<StateExecutionContext>(maxExecutionCount);
            m_Cursor = new OperationControlCursor<TTarget>(this);
            m_StateMachine = new OperationStateMachineRuntime<TTarget>(this);
            m_Lifecycle = new OperationExecutionLifecycleRuntime<TTarget>(this, m_StateMachine);
            m_Composite = new OperationCompositeRuntime<TTarget>(this);
        }

        public void PrepareStateExecutionText(CanonicalTextStorage storage, int maximumPathCharacters)
        {
            m_ExecutionPathText = new CanonicalTextReferences(Math.Min(m_MaxExecutionCount, storage.MaximumTextValues), storage);
            m_ExecutionPathCharacters = new char[maximumPathCharacters];
        }

        public void Dispose() => m_ExecutionPathText?.Reset();

        internal void Rebind(TTarget target)
        {
            if (m_StateExecution.Count != 0 || m_Lifecycle.HasTransientState)
                throw new InvalidOperationException("Operation control runtime retained transient execution state across evaluations.");
            m_Target = target;
        }

        public OperationControlCursor<TTarget> Cursor => m_Cursor;
        public bool ControlTraceEnabled => m_Target.ControlTraceEnabled && !IsPredictiveEvaluation;
        public bool IsPredictiveEvaluation
        {
            get
            {
                foreach (StateExecutionContext context in m_StateExecution)
                    if (context.HasRootCompletedOverride)
                        return true;
                return false;
            }
        }

        public void TraceEdge(ProgramControlFlowEdge edge, bool selected, bool passed)
        {
            if (m_Target.DiagnosticsEnabled && !IsPredictiveEvaluation)
                m_Target.TraceEdge(edge, selected, passed);
        }

        public OperationExecutionResult TickEdge(ProgramControlFlowEdge edge)
        {
            TraceEdge(edge, true, true);
            return Tick(edge.Target);
        }

        public OperationExecutionResult Wait(OperationExecutionDescriptor operation, OperationWaitReason reason)
        {
            if (ControlTraceEnabled)
                m_Target.EmitTrace(operation, "operation_waiting", OperationControlTraceSeverity.Detail, OperationTraceText.Wait(reason));
            return OperationExecutionResult.Running;
        }
        public void BeginEvaluation()
        {
            if (m_StateExecution.Count != 0 || m_Lifecycle.HasTransientState)
                throw new InvalidOperationException("Operation control runtime retained transient execution state across evaluations.");
            m_ExecutionPathText.Reset();
            m_ExecutionCount = 0;
        }

        public OperationExecutionResult Tick(OperationHandle handle) => m_Lifecycle.Tick(handle);

        public OperationExecutionResult TickPersistent(OperationHandle handle) => m_Lifecycle.TickPersistent(handle);

        public OperationStopStatus RequestStop(OperationHandle handle, OperationStopContext context) => m_Lifecycle.RequestStop(handle, context);

        public OperationStopStatus ContinueStop(OperationHandle handle) => m_Lifecycle.ContinueStop(handle);

        public void ForceStop(OperationHandle handle, OperationStopContext context) => m_Lifecycle.ForceStop(handle, context);

        public bool IsActive(OperationHandle handle)
        {
            if (!handle.IsValid)
                return false;
            OperationExecutionDescriptor operation = m_Topology.Operation(handle);
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            if (slot < 0)
                return false;
            var status = (OperationRunnableStatus)m_Target.ReadInt32(slot);
            return status == OperationRunnableStatus.Running || status == OperationRunnableStatus.Stopping;
        }

        public OperationRunnableStatus ReadStatus(OperationHandle handle)
        {
            if (!handle.IsValid)
                return OperationRunnableStatus.Dormant;
            OperationExecutionDescriptor operation = m_Topology.Operation(handle);
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            return slot < 0
                ? OperationRunnableStatus.Dormant
                : (OperationRunnableStatus)m_Target.ReadInt32(slot);
        }

        public bool IsRunning(OperationHandle handle)
        {
            if (!handle.IsValid)
                return false;
            OperationExecutionDescriptor operation = m_Topology.Operation(handle);
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            return slot >= 0 && m_Target.ReadInt32(slot) == (int)OperationRunnableStatus.Running;
        }

        public bool IsStopping(OperationHandle handle)
        {
            if (!handle.IsValid)
                return false;
            OperationExecutionDescriptor operation = m_Topology.Operation(handle);
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableLifecycle);
            return slot >= 0 && m_Target.ReadInt32(slot) == (int)OperationRunnableStatus.Stopping;
        }

        public ulong ReadGeneration(OperationHandle handle)
        {
            OperationExecutionDescriptor operation = m_Topology.Operation(handle);
            int slot = FindOperationSlot(operation, ProgramStateSemantic.RunnableActivationGeneration);
            return slot < 0 ? 1UL : m_Target.ReadUInt64(slot);
        }

        public bool CurrentStateRootCompleted()
        {
            if (m_StateExecution.Count == 0)
                return false;
            StateExecutionContext context = m_StateExecution.Peek();
            if (context.HasRootCompletedOverride)
                return context.RootCompletedOverride;
            OperationHandle state = context.State;
            ProgramControlFlowEdge root = m_Topology.StateRoot(state);
            if (root == null)
                return false;
            int lifecycle = FindOperationSlot(m_Topology.Operation(root.Target), ProgramStateSemantic.RunnableLifecycle);
            return lifecycle >= 0 && m_Target.ReadInt32(lifecycle) == (int)OperationRunnableStatus.Success;
        }

        public ProgramControlFlowEdge PredictCurrentStateRootCompletionTransition()
        {
            if (m_StateExecution.Count == 0)
                return null;
            return m_StateMachine.PredictCurrentStateRootCompletionTransition(m_StateExecution.Peek().State);
        }

        public int CurrentStateExitCause()
        {
            return m_StateExecution.Count == 0 ? -1 : m_StateExecution.Peek().ExitCause;
        }

        public CanonicalTextValue FindStateExecutionPath(OperationHandle state)
        {
            OperationHandle owner = m_Topology.StateMachineOwner(state);
            if (!owner.IsValid)
                return string.Empty;
            OperationExecutionDescriptor machine = m_Topology.Operation(owner);
            int active = FindOperationSlot(machine, ProgramStateSemantic.StateMachineActive);
            int exiting = FindOperationSlot(machine, ProgramStateSemantic.StateMachineExiting);
            string stateIdentity = m_Topology.OperationIdentity(state);
            if ((active >= 0 && m_Target.ReadIdentity(active).Equals(stateIdentity)) ||
                (exiting >= 0 && m_Target.ReadIdentity(exiting).Equals(stateIdentity)))
            {
                int path = FindOperationSlot(machine, ProgramStateSemantic.StateMachineExecutionPath);
                return path >= 0 ? m_Target.ReadIdentity(path) : string.Empty;
            }
            return string.Empty;
        }

        public bool TryGetCurrentStateExecutionPath(OperationHandle state, out CanonicalTextValue path)
        {
            foreach (StateExecutionContext context in m_StateExecution)
            {
                if (!context.State.Equals(state))
                    continue;
                path = context.Path;
                return true;
            }
            path = string.Empty;
            return false;
        }

        public bool IsCurrentStateExecution(OperationHandle state)
        {
            foreach (StateExecutionContext context in m_StateExecution)
            {
                if (context.State.Equals(state))
                    return true;
            }
            return false;
        }

        public StateExecutionScope PushStateExecution(OperationHandle state, int exitCause)
        {
            PushStateExecutionContext(state, exitCause);
            return new StateExecutionScope(this);
        }

        StateExecutionScope PushStateExecutionScope(OperationHandle state, int exitCause)
        {
            PushStateExecutionContext(state, exitCause, false, false);
            return new StateExecutionScope(this);
        }

        StateExecutionScope PushStateExecutionScope(
            OperationHandle state,
            int exitCause,
            bool rootCompletedOverride)
        {
            PushStateExecutionContext(state, exitCause, true, rootCompletedOverride);
            return new StateExecutionScope(this);
        }

        void PushStateExecutionContext(OperationHandle state, int exitCause)
        {
            PushStateExecutionContext(state, exitCause, false, false);
        }

        void PushStateExecutionContext(
            OperationHandle state,
            int exitCause,
            bool hasRootCompletedOverride,
            bool rootCompletedOverride)
        {
            CanonicalTextValue path = FindStateExecutionPath(state);
            path.Retain();
            m_StateExecution.Push(new StateExecutionContext(
                state,
                exitCause,
                path,
                hasRootCompletedOverride,
                rootCompletedOverride));
        }

        public void RequireExecution(OperationHandle handle)
        {
            m_Topology.RequireOperation(handle);
            m_ExecutionCount++;
            if (m_ExecutionCount > m_MaxExecutionCount)
                throw new InvalidOperationException($"Program exceeded '{m_MaxExecutionCount}' operation evaluations.");
        }

        OperationExecutionResult Execute(OperationExecutionDescriptor operation)
        {
            switch (operation.Code)
            {
                case SimulationOperationCode.Root:
                case SimulationOperationCode.StateOnEnter:
                case SimulationOperationCode.StateOnExit:
                case SimulationOperationCode.TimelineEnter:
                    return TickSingleChild(operation, ProgramControlFlowKind.Child);
                case SimulationOperationCode.Loop:
                    return m_Composite.TickLoop(operation);
                case SimulationOperationCode.Parallel:
                    return m_Composite.TickParallel(operation);
                case SimulationOperationCode.Sequence:
                    return m_Composite.TickSequence(operation);
                case SimulationOperationCode.Selector:
                    return m_Composite.TickSelector(operation);
                case SimulationOperationCode.Succeed:
                    return OperationExecutionResult.Success;
                case SimulationOperationCode.SubGraph:
                    m_Target.PrepareSubGraph(m_Cursor, operation);
                    return TickSingleChild(operation, ProgramControlFlowKind.Enter);
                case SimulationOperationCode.StateMachine:
                    return m_StateMachine.Tick(operation);
                case SimulationOperationCode.State:
                    return m_StateMachine.TickState(operation);
                default:
                    return m_Target.ExecuteLeaf(m_Cursor, operation);
            }
        }

        OperationExecutionResult TickSingleChild(OperationExecutionDescriptor operation, ProgramControlFlowKind kind)
        {
            IReadOnlyList<ProgramControlFlowEdge> children = Edges(operation.Handle, kind);
            if (children.Count > 1)
                throw new InvalidOperationException(
                    $"Single-child operation '{operation.Handle}' ({operation.Code}) has '{children.Count}' child edges.");
            if (children.Count == 0)
                return OperationExecutionResult.Success;
            ProgramControlFlowEdge edge = children[0];
            if (!EvaluateCondition(edge))
                return OperationExecutionResult.Failure;
            OperationExecutionResult result = TickEdge(edge);
            return result == OperationExecutionResult.Running
                ? Wait(operation, operation.Code == SimulationOperationCode.SubGraph ? OperationWaitReason.SubgraphCompletion : OperationWaitReason.ChildCompletion)
                : result;
        }

        bool EvaluateCondition(ProgramControlFlowEdge edge)
        {
            bool result = !edge.HasCondition || m_Target.EvaluateCondition(m_Cursor, edge);
            if (edge.HasCondition)
                TraceEdge(edge, false, result);
            return result;
        }

        IReadOnlyList<ProgramControlFlowEdge> Edges(OperationHandle source, ProgramControlFlowKind kind)
        {
            return m_Topology.Outgoing(source, kind);
        }

        int FindOperationSlot(OperationExecutionDescriptor operation, ProgramStateSemantic semantic)
        {
            return m_Topology.FindOperationStateSlot(operation.Handle, semantic);
        }

        int RequireOperationSlot(OperationExecutionDescriptor operation, ProgramStateSemantic semantic)
        {
            return m_Topology.RequireOperationStateSlot(operation.Handle, semantic);
        }

        CanonicalTextValue BuildStateExecutionPath(CanonicalTextValue parent, ulong machine, ulong state, ulong generation)
        {
            int length = parent.Length + "/sm:".Length + CountDigits(machine)
                + "/state:".Length + CountDigits(state) + "@".Length + CountDigits(generation);
            Span<char> characters = m_ExecutionPathCharacters.AsSpan(0, length);
            int position = 0;
            WriteLiteral(parent.Span, characters, ref position);
            WriteLiteral("/sm:", characters, ref position);
            WriteNumber(machine, characters, ref position);
            WriteLiteral("/state:", characters, ref position);
            WriteNumber(state, characters, ref position);
            WriteLiteral("@", characters, ref position);
            WriteNumber(generation, characters, ref position);
            return m_ExecutionPathText.CaptureCharacters(characters);
        }

        static void WriteLiteral(ReadOnlySpan<char> literal, Span<char> characters, ref int position)
        {
            literal.CopyTo(characters.Slice(position));
            position += literal.Length;
        }

        static void WriteNumber(ulong value, Span<char> characters, ref int position)
        {
            value.TryFormat(characters.Slice(position), out int written, provider: CultureInfo.InvariantCulture);
            position += written;
        }

        static int CountDigits(ulong value)
        {
            int count = 1;
            while (value >= 10)
            {
                value /= 10;
                count++;
            }
            return count;
        }

        OperationExecutionTopology IOperationStateMachineHost<TTarget>.Topology => m_Topology;
        int IOperationStateMachineHost<TTarget>.ReadInt32(int slotIndex) => m_Target.ReadInt32(slotIndex);
        void IOperationStateMachineHost<TTarget>.WriteInt32(int slotIndex, int value) => m_Target.WriteInt32(slotIndex, value);
        CanonicalTextValue IOperationStateMachineHost<TTarget>.ReadIdentity(int slotIndex) => m_Target.ReadIdentity(slotIndex);
        void IOperationStateMachineHost<TTarget>.WriteIdentity(int slotIndex, CanonicalTextValue value) => m_Target.WriteIdentity(slotIndex, value);
        bool IOperationStateMachineHost<TTarget>.EvaluateCondition(ProgramControlFlowEdge edge) => EvaluateCondition(edge);
        int IOperationStateMachineHost<TTarget>.RequireOperationSlot(OperationExecutionDescriptor operation, ProgramStateSemantic semantic) => RequireOperationSlot(operation, semantic);
        void IOperationStateMachineHost<TTarget>.NotifyStateLifecycle(OperationExecutionDescriptor machine, OperationHandle state, OperationStateLifecyclePhase phase) => m_Target.NotifyStateLifecycle(machine, state, phase);
        void IOperationStateMachineHost<TTarget>.NotifyStateTransition(OperationExecutionDescriptor machine, OperationHandle exitingState, OperationHandle targetState) => m_Target.NotifyStateTransition(machine, exitingState, targetState);
        CanonicalTextValue IOperationStateMachineHost<TTarget>.BuildStateExecutionPath(CanonicalTextValue parent, ulong machine, ulong state, ulong generation) =>
            BuildStateExecutionPath(parent, machine, state, generation);
        CanonicalTextValue IOperationStateMachineHost<TTarget>.CurrentStateExecutionPath => m_StateExecution.Count == 0 ? string.Empty : m_StateExecution.Peek().Path;
        StateExecutionScope IOperationStateMachineHost<TTarget>.PushStateScope(OperationHandle state, int exitCause) => PushStateExecutionScope(state, exitCause);
        StateExecutionScope IOperationStateMachineHost<TTarget>.PushStateScope(OperationHandle state, int exitCause, bool hasRootCompletedOverride, bool rootCompletedOverride) =>
            hasRootCompletedOverride
                ? PushStateExecutionScope(state, exitCause, rootCompletedOverride)
                : PushStateExecutionScope(state, exitCause);
        OperationControlCursor<TTarget> IOperationExecutionLifecycleHost<TTarget>.Cursor => m_Cursor;
        bool IOperationExecutionLifecycleHost<TTarget>.DiagnosticsEnabled => m_Target.DiagnosticsEnabled;
        void IOperationExecutionLifecycleHost<TTarget>.EmitTrace(OperationExecutionDescriptor operation, string code, OperationControlTraceSeverity severity, string detail) =>
            m_Target.EmitTrace(operation, code, severity, detail);
        ulong IOperationExecutionLifecycleHost<TTarget>.ReadUInt64(int slotIndex) => m_Target.ReadUInt64(slotIndex);
        void IOperationExecutionLifecycleHost<TTarget>.WriteUInt64(int slotIndex, ulong value) => m_Target.WriteUInt64(slotIndex, value);
        void IOperationExecutionLifecycleHost<TTarget>.ActivateScopes(OperationControlCursor<TTarget> cursor, OperationExecutionDescriptor operation, ulong generation) => m_Target.ActivateScopes(cursor, operation, generation);
        void IOperationExecutionLifecycleHost<TTarget>.ResetOperationState(OperationExecutionDescriptor operation) => m_Target.ResetOperationState(operation);
        OperationStopStatus IOperationExecutionLifecycleHost<TTarget>.ContinueLeafStop(OperationControlCursor<TTarget> cursor, OperationExecutionDescriptor operation, OperationStopContext context) => m_Target.ContinueLeafStop(cursor, operation, context);
        void IOperationExecutionLifecycleHost<TTarget>.ForceStopLeaf(OperationControlCursor<TTarget> cursor, OperationExecutionDescriptor operation, OperationStopContext context) => m_Target.ForceStopLeaf(cursor, operation, context);
        OperationExecutionResult IOperationExecutionLifecycleHost<TTarget>.Execute(OperationExecutionDescriptor operation) => Execute(operation);
        void IOperationExecutionLifecycleHost<TTarget>.RequireExecution(OperationHandle handle) => RequireExecution(handle);

        void IOperationCompositeRuntimeHost<TTarget>.EmitLoopTrace(OperationExecutionDescriptor operation, string code, string detail)
        {
            if (m_Target.DiagnosticsEnabled && !IsPredictiveEvaluation)
                m_Target.EmitTrace(operation, code, OperationControlTraceSeverity.Detail, detail);
        }

        OperationExecutionTopology IOperationCompositeRuntimeHost<TTarget>.Topology => m_Topology;
        int IOperationCompositeRuntimeHost<TTarget>.ReadInt32(int slotIndex) => m_Target.ReadInt32(slotIndex);
        void IOperationCompositeRuntimeHost<TTarget>.WriteInt32(int slotIndex, int value) => m_Target.WriteInt32(slotIndex, value);
        bool IOperationCompositeRuntimeHost<TTarget>.EvaluateCondition(ProgramControlFlowEdge edge) => EvaluateCondition(edge);
        OperationStopContext IOperationCompositeRuntimeHost<TTarget>.ReadStopContext(OperationExecutionDescriptor operation) => m_Lifecycle.ReadStopContext(operation);
        void IOperationCompositeRuntimeHost<TTarget>.WriteStopContext(OperationExecutionDescriptor operation, OperationStopContext context) => m_Lifecycle.WriteStopContext(operation, context);
        void IOperationCompositeRuntimeHost<TTarget>.ClearStopContext(OperationExecutionDescriptor operation) => m_Lifecycle.ClearStopContext(operation);

        readonly struct StateExecutionContext
        {
            public StateExecutionContext(
                OperationHandle state,
                int exitCause,
                CanonicalTextValue path,
                bool hasRootCompletedOverride,
                bool rootCompletedOverride)
            {
                State = state;
                ExitCause = exitCause;
                Path = path.IsNull ? CanonicalTextValue.Empty : path;
                HasRootCompletedOverride = hasRootCompletedOverride;
                RootCompletedOverride = rootCompletedOverride;
            }

            public OperationHandle State { get; }
            public int ExitCause { get; }
            public CanonicalTextValue Path { get; }
            public bool HasRootCompletedOverride { get; }
            public bool RootCompletedOverride { get; }
        }

        public readonly struct StateExecutionScope : IDisposable
        {
            readonly OperationControlRuntime<TTarget> m_Owner;

            internal StateExecutionScope(OperationControlRuntime<TTarget> owner)
            {
                m_Owner = owner;
            }

            public void Dispose()
            {
                if (m_Owner == null)
                    return;
                m_Owner.m_StateExecution.Pop().Path.Release();
            }
        }

    }
}
