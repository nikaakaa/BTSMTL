#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using FlowCanvas;
using NodeCanvas.Framework;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs
{
    public sealed partial class HostedGraphRuntime<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TNumeric, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TNumeric : struct, IGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion>
        where TContext : struct
    {
        const uint RecordedStateFormat = 0x45564752;
        const uint RecordedStateVersion = 3;
        readonly Dictionary<(string Node, string Port, bool Input), GraphRuntimePort> m_EditorPorts = new();
        GraphRuntimePort[] m_RuntimePorts;
        OrchestrationValueOverride<TValue>[] m_Overrides;
        TValue[] m_ObservedInputs;
        TValue[] m_ObservedOutputs;
        List<(int Operation, int Port, bool Input)> m_Triggers;
        OrchestrationValueOverride<TValue>[] m_CandidateOverrides;
        TValue[] m_CandidateObservedInputs;
        TValue[] m_CandidateObservedOutputs;
        List<(int Operation, int Port, bool Input)> m_CandidateTriggers;

        public event Action<int, int> RecordedStateCapacityRequired;

        public IReadOnlyList<GraphRuntimePort> RuntimePorts => m_RuntimePorts;
        public GraphRuntimePort RequireRuntimePort(string node, string port, bool input) => m_EditorPorts[(node, port, input)];

        void InitializeEditor()
        {
            m_Workspace.PrepareValueCapture();
            OrchestrationProgram<TValue> program = m_Bindings.Program;
            int overrideCapacity = m_Bindings.PublishedSlots.Length;
            foreach (GraphRuntimePort port in m_RuntimePorts)
                if (!port.IsTrigger)
                    overrideCapacity++;
            m_Overrides = new OrchestrationValueOverride<TValue>[overrideCapacity];
            m_ObservedInputs = new TValue[program.InputBindingCount];
            m_ObservedOutputs = new TValue[program.ValueEvaluationSlotCount];
            m_Triggers = new List<(int Operation, int Port, bool Input)>(m_Bindings.Content.Ports.Length);
            m_CandidateOverrides = new OrchestrationValueOverride<TValue>[overrideCapacity];
            m_CandidateObservedInputs = new TValue[m_ObservedInputs.Length];
            m_CandidateObservedOutputs = new TValue[m_ObservedOutputs.Length];
            m_CandidateTriggers = new List<(int Operation, int Port, bool Input)>(m_Triggers.Capacity);
        }

        public void QueuePortTrigger(string node, string port, bool input)
        {
            RequireIdle();
            GraphRuntimePort target = RequireRuntimePort(node, port, input);
            PrepareTriggerCapacity(m_Triggers.Count + 1);
            m_Triggers.Add((target.Operation, target.CompiledPortId, target.IsInput));
        }

        bool HasPendingTrigger(GraphRuntimePort port)
        {
            foreach (var trigger in m_Triggers)
                if (trigger.Operation == port.Operation && trigger.Port == port.CompiledPortId &&
                    trigger.Input == port.IsInput)
                    return true;
            return false;
        }

        void ExecuteTriggers<TDiagnostics>(ref OrchestrationExecutionCursor<TValue, TScalar, TVector2,
            TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics> cursor)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics
        {
            for (int i = 0; i < m_CandidateTriggers.Count; i++)
            {
                var trigger = m_CandidateTriggers[i];
                if (trigger.Input)
                    cursor.Advance(new OrchestrationExecutionEntry(trigger.Operation,
                        OrchestrationExecutionContract.Complete, trigger.Port));
                else
                    cursor.ExecuteOutput(trigger.Operation, trigger.Port, OrchestrationExecutionContract.Complete);
            }
        }

        public void MutateRuntimeVariable(string variableId, GraphHostValue value, GraphVariableMutation operation)
        {
            RequireIdle();
            int slot = m_Bindings.PublishedSlots[VariableContract.Layout.Bind(variableId).Index];
            if (operation != GraphVariableMutation.Release)
                RequireValue(value, m_Bindings.StateTypes[slot]);
            BeginCandidateState();
            try
            {
                int enumTypeContentId = m_Bindings.StateTypes[slot].IsEnum
                    ? default(TValues).Enum(m_Bindings.Program.Content.DefaultValues.Span[slot]).TypeContentId : 0;
                TValue state = operation == GraphVariableMutation.Release ? default :
                    m_Bindings.ToState(in value, enumTypeContentId, m_CandidateTexts);
                MutateCandidateVariable(slot, in state, operation);
                m_CandidateTexts.PrepareOrdinals();
                AdoptCandidateState();
            }
            finally
            {
                ClearCandidateState();
            }
        }

        public GraphHostValue ReadRuntimeVariable(string variableId)
        {
            int slot = m_Bindings.PublishedSlots[VariableContract.Layout.Bind(variableId).Index];
            TValue value = ReadState(slot, StateValues);
            return m_Bindings.FromState(in value, m_Bindings.StateTypes[slot], m_StateTexts);
        }

        int FindVariableOverride(int slot) => FindOverride(m_Overrides, OrchestrationValueOverrideKind.Blackboard, slot);

        bool IsVariableLocked(int slot) => FindVariableOverride(slot) >= 0;

        public bool IsRuntimeVariableLocked(string variableId) =>
            IsVariableLocked(m_Bindings.PublishedSlots[VariableContract.Layout.Bind(variableId).Index]);

        TValue[] PortObservations(GraphRuntimePort port) => port.IsInput ? m_ObservedInputs : m_ObservedOutputs;
        TValue[] CandidatePortObservations(GraphRuntimePort port) =>
            port.IsInput ? m_CandidateObservedInputs : m_CandidateObservedOutputs;

        static int FindOverride(ReadOnlySpan<OrchestrationValueOverride<TValue>> overrides,
            OrchestrationValueOverrideKind kind, int slot)
        {
            for (int index = 0; index < overrides.Length; index++)
                if (overrides[index].Mode != OrchestrationValueOverrideMode.None &&
                    overrides[index].Status != OrchestrationValueOverrideStatus.OwnerRetired &&
                    overrides[index].Address.Kind == kind && overrides[index].Address.Slot == slot)
                    return index;
            return -1;
        }

        int FindPortOverride(GraphRuntimePort port) => FindOverride(m_Overrides,
            port.IsInput ? OrchestrationValueOverrideKind.Input : OrchestrationValueOverrideKind.Output, port.ValueSlot);

        internal GraphHostValue ReadPort(GraphRuntimePort port, bool input)
        {
            int slot = port.ValueSlot;
            int supplied = FindPortOverride(port);
            TValue value = supplied >= 0 ? m_Overrides[supplied].Value :
                input ? default : PortObservations(port)[slot];
            return default(TValues).Kind(in value) == default ? default : m_Bindings.FromState(in value, port.ValueType, m_StateTexts);
        }

        internal bool IsPortLocked(GraphRuntimePort port)
        {
            int index = FindPortOverride(port);
            return index >= 0 && m_Overrides[index].Mode == OrchestrationValueOverrideMode.Locked;
        }

        internal void MutatePort(GraphRuntimePort port, GraphHostValue value, GraphVariableMutation operation)
        {
            RequireIdle();
            BeginCandidateState();
            try
            {
                ApplyCandidatePortMutation(port, in value, operation);
                m_CandidateTexts.PrepareOrdinals();
                AdoptCandidateState();
            }
            finally
            {
                ClearCandidateState();
            }
        }

        void ApplyCandidatePortMutation(GraphRuntimePort port, in GraphHostValue value,
            GraphVariableMutation operation)
        {
            var input = new OrchestrationFrameInput<TValue>(
                m_LastInvocation.Sequence, m_ResetGeneration, default, default, overrides: m_CandidateOverrides);
            var cursor = CreateEditorCursor(in input);
            cursor.BindTraceInvocationScope(port.Operation, m_Bindings.Update);
            OrchestrationValueOverrideAddress address = cursor.ReadPortOverrideAddress(port.Operation,
                port.ValueSlot, port.IsInput);
            if (operation == GraphVariableMutation.Release)
                OrchestrationValueOverride<TValue>.Release(
                    m_CandidateOverrides, in address);
            else
            {
                RequireValue(value, port.ValueType);
                TValue converted =
                    m_Bindings.ToState(in value, port.EnumTypeContentId, m_CandidateTexts);
                OrchestrationValueOverride<TValue>.Set(
                    m_CandidateOverrides, in address, in converted, operation == GraphVariableMutation.Override
                        ? OrchestrationValueOverrideMode.Locked : OrchestrationValueOverrideMode.Once);
            }
        }

        void MutateCandidateVariable(int slot,
            in TValue value, GraphVariableMutation mutation)
        {
            var input = new OrchestrationFrameInput<TValue>(
                m_LastInvocation.Sequence, m_ResetGeneration, default, default, overrides: m_CandidateOverrides);
            var cursor = CreateEditorCursor(in input);
            int operation = m_Bindings.Program.RootOperationIndex;
            OrchestrationValueOverrideAddress address = cursor.ReadBlackboardOverrideAddress(operation, slot);
            cursor.TryMutateBlackboard(operation, slot, in address, in value, mutation switch
            {
                GraphVariableMutation.Assign => OrchestrationValueMutation.Set,
                GraphVariableMutation.Override => OrchestrationValueMutation.Lock,
                _ => OrchestrationValueMutation.Release
            });
        }

        OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, OrchestrationWorkbenchDiagnostics>
            CreateEditorCursor(in OrchestrationFrameInput<TValue> input)
        {
            var state = OrchestrationStateBinding.BindCandidate(m_Layout, m_Candidate);
            OrchestrationIdentityOrdinalView identities = m_CandidateTexts.Ordinals;
            TContext context = default;
            return state.CreateCursor<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, OrchestrationWorkbenchDiagnostics>(
                m_Bindings.Program, m_Workspace, m_Extensions, 0, ReadOnlySpan<int>.Empty,
                null, null, in input, in identities, in context);
        }

        static void RequireValue(GraphHostValue value, Type type)
        {
            GraphHostValueKinds.TryGet(type, out GraphHostValueKind expected);
            if (value.Kind != expected || expected == GraphHostValueKind.Enum && value.EnumType != type)
                throw new InvalidOperationException($"运行编辑值与声明类型 '{type.FullName}' 不匹配。");
        }

        void PrepareEditor()
        {
            var maps = new Dictionary<(string Node, string Port, bool Input), OrchestrationGraphPort>();
            OrchestrationIdentityTable identities = m_Bindings.Prepared.Identities.Table;
            int graphIdentity = identities.Bind(Source.AuthoringId);
            foreach (ref readonly OrchestrationGraphPort map in m_Bindings.Prepared.Ports.Span)
                if (map.GraphIdentityId == graphIdentity)
                    maps.Add((identities.Resolve(map.NodeIdentityId), identities.Resolve(map.PortIdentityId), map.IsInput), map);
            foreach (Node node in Source.allNodes)
            {
                var flow = (FlowNode)node;
                foreach (Port port in flow.GetInputPorts())
                    Add(port, true);
                foreach (Port port in flow.GetOutputPorts())
                    Add(port, false);
            }
            m_RuntimePorts = new GraphRuntimePort[m_EditorPorts.Count];
            m_EditorPorts.Values.CopyTo(m_RuntimePorts, 0);

            void Add(Port port, bool input)
            {
                OrchestrationGraphPort map = maps[(port.parent.UID, port.ID, input)];
                int enumIdentity = port.type.IsEnum ? identities.Bind(port.type.AssemblyQualifiedName) : 0;
                m_EditorPorts.Add((port.parent.UID, port.ID, input),
                    new GraphRuntimePort(Source.AuthoringId, port, input, map.OperationIndex, map.ValueIndex,
                        map.CompiledPortIdentityId, enumIdentity, ReadPort, MutatePort, IsPortLocked,
                        () => QueuePortTrigger(port.parent.UID, port.ID, input), HasPendingTrigger));
            }
        }

        void AdoptEditor()
        {
            CopyObserved(m_Workspace.ObservedInputs, m_Workspace.WrittenInputs, m_CandidateObservedInputs);
            CopyObserved(m_Workspace.ObservedOutputs, m_Workspace.WrittenOutputs, m_CandidateObservedOutputs);
            OrchestrationValueOverride<TValue>
                .CompleteConsumption(m_CandidateOverrides);
            m_CandidateTriggers.Clear();
        }

        void PrepareTriggerCapacity(int count)
        {
            int triggerCapacity = m_Triggers.Capacity;
            int candidateTriggerCapacity = m_CandidateTriggers.Capacity;
            m_Triggers.Capacity = Math.Max(triggerCapacity, count);
            m_CandidateTriggers.Capacity = Math.Max(candidateTriggerCapacity, count);
            if (m_Triggers.Capacity > triggerCapacity || m_CandidateTriggers.Capacity > candidateTriggerCapacity)
            {
                GetRecordedStateCapacity(out int byteCapacity, out int textReferenceCapacity);
                RecordedStateCapacityRequired?.Invoke(byteCapacity, textReferenceCapacity);
            }
        }

        void PrepareEditorCandidateState()
        {
            m_Overrides.AsSpan().CopyTo(m_CandidateOverrides);
            for (int index = 0; index < m_CandidateOverrides.Length; index++)
                RemapValue(ref m_CandidateOverrides[index].Value);
            RemapValues(m_ObservedInputs, m_CandidateObservedInputs);
            RemapValues(m_ObservedOutputs, m_CandidateObservedOutputs);
            m_CandidateTriggers.Clear();
            for (int i = 0; i < m_Triggers.Count; i++)
                m_CandidateTriggers.Add(m_Triggers[i]);
        }

        void RemapValues(ReadOnlySpan<TValue> source, Span<TValue> destination)
        {
            source.CopyTo(destination);
            for (int i = 0; i < source.Length; i++)
                RemapValue(ref destination[i]);
        }

        void AdoptEditorCandidateState()
        {
            Swap(ref m_Overrides, ref m_CandidateOverrides);
            Swap(ref m_ObservedInputs, ref m_CandidateObservedInputs);
            Swap(ref m_ObservedOutputs, ref m_CandidateObservedOutputs);
            Swap(ref m_Triggers, ref m_CandidateTriggers);
        }

        static void Swap<T>(ref T current, ref T candidate)
        {
            T previous = current;
            current = candidate;
            candidate = previous;
        }

        void ClearEditorCandidateState()
        {
            Array.Clear(m_CandidateOverrides, 0, m_CandidateOverrides.Length);
            Array.Clear(m_CandidateObservedInputs, 0, m_CandidateObservedInputs.Length);
            Array.Clear(m_CandidateObservedOutputs, 0, m_CandidateObservedOutputs.Length);
            m_CandidateTriggers.Clear();
        }

        static void CopyObserved(ReadOnlySpan<TValue> observed, ReadOnlySpan<bool> written,
            Span<TValue> destination)
        {
            for (int i = 0; i < written.Length; i++)
                if (written[i])
                    destination[i] = observed[i];
        }

        static Span<OrchestrationValueOverride<TValue>> ActiveOverrides(
            OrchestrationValueOverride<TValue>[] values)
        {
            int length = values.Length;
            while (length > 0 && values[length - 1].Mode == OrchestrationValueOverrideMode.None)
                length--;
            return values.AsSpan(0, length);
        }

        void ResetEditor()
        {
            Array.Clear(m_Overrides, 0, m_Overrides.Length);
            Array.Clear(m_ObservedInputs, 0, m_ObservedInputs.Length);
            Array.Clear(m_ObservedOutputs, 0, m_ObservedOutputs.Length);
            m_Triggers.Clear();
        }

        public void GetRecordedStateCapacity(out int byteCapacity, out int textReferenceCapacity)
        {
            var writer = new RecordedRuntimeStateCountingWriter();
            WriteRecordedFrameState(ref writer, true);
            byteCapacity = writer.WrittenBytes;
            textReferenceCapacity = writer.TextReferenceCount;
        }

        public void WriteRecordedFrameState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            RequireIdle();
            WriteRecordedFrameState(ref writer, false);
        }

        void WriteRecordedFrameState<TWriter>(ref TWriter writer, bool maximumCapacity)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(RecordedStateFormat);
            writer.Write(RecordedStateVersion);
            writer.Write(m_Bindings.Prepared.Identities.Identity);
            writer.Write(m_MemoryLayout.SizeBytes);
            writer.Write(m_MemoryLayout.Alignment);
            m_MemoryLayout.ReadIdentityWords(out ulong identity0, out ulong identity1,
                out ulong identity2, out ulong identity3);
            writer.Write(identity0);
            writer.Write(identity1);
            writer.Write(identity2);
            writer.Write(identity3);
            writer.Write(m_ResetGeneration);
            writer.Write(m_Started);
            writer.Write(m_Faulted);
            writer.Write(m_LastInvocation.SourceIdentityId);
            writer.Write(m_LastInvocation.Sequence);
            if (maximumCapacity || m_Faulted)
            {
                writer.WriteText(maximumCapacity ? null : m_LastFailure.GraphId);
                writer.WriteText(maximumCapacity ? null : m_LastFailure.EventId);
                writer.WriteText(maximumCapacity ? null : m_LastFailure.NodeId);
                writer.WriteText(maximumCapacity ? null : m_LastFailure.Message);
                writer.WriteText(maximumCapacity ? null : m_LastFailure.ExceptionType);
            }
            int triggerCount = maximumCapacity ? Math.Max(m_Triggers.Capacity, m_CandidateTriggers.Capacity) : m_Triggers.Count;
            writer.Write(triggerCount);
            for (int i = 0; i < triggerCount; i++)
            {
                var trigger = maximumCapacity ? default : m_Triggers[i];
                writer.Write(trigger.Operation);
                writer.Write(trigger.Port);
                writer.Write(trigger.Input);
            }
            var state = OrchestrationStateBinding.BindCommitted(m_Layout, m_State);
            var frames = state.Frames;
            writer.WriteSpan<OrchestrationOperationState>(frames.Operations(0));
            writer.WriteSpan<OrchestrationBlackboardMetadata>(frames.BlackboardMetadata(0));
            ReadOnlyOrchestrationStateView memory = m_Layout.Layout.BindCommitted(m_State);
            writer.WriteSpan<OrchestrationTransitionCacheState>(m_Layout.GetTransitionCaches(memory));
            writer.WriteSpan<OrchestrationTransitionSnapshot<TValue>>(m_Layout.GetTransitionSnapshots(memory));
            ReadOnlySpan<TValue> values = frames.Values(0);
            for (int i = 0; i < m_Bindings.Program.ValueSlotCount; i++)
            {
                if (maximumCapacity)
                    GraphHostValue.WriteRecordedStateCapacity(ref writer, m_Bindings.StateTypes[i]);
                else
                {
                    TValue value = values[i];
                    m_Bindings.FromState(in value, m_Bindings.StateTypes[i], m_StateTexts).WriteRecordedFrameState(ref writer);
                }
                bool locked = maximumCapacity || IsVariableLocked(i);
                writer.Write(locked);
                if (locked)
                {
                    if (maximumCapacity)
                        GraphHostValue.WriteRecordedStateCapacity(ref writer, m_Bindings.StateTypes[i]);
                    else
                        m_Bindings.FromState(in m_Overrides[FindVariableOverride(i)].Value, m_Bindings.StateTypes[i], m_StateTexts)
                            .WriteRecordedFrameState(ref writer);
                }
            }
            for (int i = 0; i < m_RuntimePorts.Length; i++)
            {
                GraphRuntimePort port = m_RuntimePorts[i];
                if (port.IsTrigger)
                    continue;
                port.WriteRecordedFrameState(ref writer, maximumCapacity);
                TValue observed = maximumCapacity ? default : PortObservations(port)[port.ValueSlot];
                bool hasObserved = maximumCapacity || default(TValues).Kind(in observed) != default;
                writer.Write(hasObserved);
                if (hasObserved)
                {
                    if (maximumCapacity)
                        GraphHostValue.WriteRecordedStateCapacity(ref writer, port.ValueType);
                    else
                        m_Bindings.FromState(in observed, port.ValueType, m_StateTexts).WriteRecordedFrameState(ref writer);
                }
            }
        }

        public void RestoreRecordedFrameState(ref RecordedRuntimeStateReader reader)
        {
            RequireIdle();
            try
            {
                Array.Clear(m_Candidate, 0, m_Candidate.Length);
                if (reader.Read<uint>() != RecordedStateFormat || reader.Read<uint>() != RecordedStateVersion)
                    throw new InvalidOperationException("事件图记录格式不受支持。");
                StableHash identities = reader.Read<StableHash>();
                if (!identities.Equals(m_Bindings.Prepared.Identities.Identity))
                    throw new InvalidOperationException("事件图记录的身份表与当前准备的内容不匹配。");
                var actualLayout = new OrchestrationStateLayout(reader.Read<int>(), reader.Read<int>(),
                    reader.Read<ulong>(), reader.Read<ulong>(), reader.Read<ulong>(), reader.Read<ulong>());
                OrchestrationStateLayout preparedLayout = m_Layout.Layout;
                OrchestrationStateBinding.AdoptMemory(in preparedLayout, in actualLayout, m_Candidate);
                var state = OrchestrationStateBinding.BindCandidate(m_Layout, m_Candidate);
                ulong resetGeneration = reader.Read<ulong>();
                bool started = reader.Read<bool>();
                bool faulted = reader.Read<bool>();
                int invocationSourceIdentityId = reader.Read<int>();
                ulong sequence = reader.Read<ulong>();
                GraphExecutionFailure failure = faulted
                    ? new GraphExecutionFailure(reader.ReadText(), reader.ReadText(),
                        reader.ReadText(), reader.ReadText(), null, reader.ReadText())
                    : null;
                int triggerCount = reader.Read<int>();
                PrepareTriggerCapacity(triggerCount);
                for (int i = 0; i < triggerCount; i++)
                    m_CandidateTriggers.Add((reader.Read<int>(), reader.Read<int>(), reader.Read<bool>()));
                var frames = state.Frames;
                InitializeRootFrame(ref frames, resetGeneration);
                Span<OrchestrationOperationState> operations = frames.Operations(0);
                for (int i = 0; i < operations.Length; i++)
                    operations[i] = reader.Read<OrchestrationOperationState>();
                Span<OrchestrationBlackboardMetadata> blackboards = frames.BlackboardMetadata(0);
                for (int i = 0; i < blackboards.Length; i++)
                    blackboards[i] = reader.Read<OrchestrationBlackboardMetadata>();
                Span<OrchestrationTransitionCacheState> transitionCaches = frames.TransitionCaches(0);
                reader.ReadSpan<OrchestrationTransitionCacheState>(transitionCaches.Length).CopyTo(transitionCaches);
                Span<OrchestrationTransitionSnapshot<TValue>> transitionSnapshots = frames.TransitionSnapshots(0);
                reader.ReadSpan<OrchestrationTransitionSnapshot<TValue>>(transitionSnapshots.Length).CopyTo(transitionSnapshots);
                Span<TValue> values = frames.Values(0);
                for (int i = 0; i < m_Bindings.Program.ValueSlotCount; i++)
                {
                    int enumIdentity = m_Bindings.StateTypes[i].IsEnum
                        ? default(TValues).Enum(m_Bindings.Program.Content.DefaultValues.Span[i]).TypeContentId : 0;
                    GraphHostValue value = GraphHostValue.ReadRecordedFrameState(ref reader, m_Bindings.StateTypes[i]);
                    values[i] = m_Bindings.ToState(in value, enumIdentity, m_CandidateTexts);
                    if (reader.Read<bool>())
                    {
                        GraphHostValue locked = GraphHostValue.ReadRecordedFrameState(ref reader, m_Bindings.StateTypes[i]);
                        TValue supplied =
                            m_Bindings.ToState(in locked, enumIdentity, m_CandidateTexts);
                        MutateCandidateVariable(i, in supplied, GraphVariableMutation.Override);
                    }
                }
                for (int i = 0; i < m_RuntimePorts.Length; i++)
                {
                    GraphRuntimePort port = m_RuntimePorts[i];
                    if (port.IsTrigger)
                        continue;
                    GraphVariableMutation mutation = GraphRuntimePort.ReadRecordedMutation(
                        ref reader, port.ValueType, out GraphHostValue supplied);
                    ApplyCandidatePortMutation(port, in supplied, mutation);
                    if (reader.Read<bool>())
                    {
                        GraphHostValue observed = GraphHostValue.ReadRecordedFrameState(ref reader, port.ValueType);
                        CandidatePortObservations(port)[port.ValueSlot] =
                            m_Bindings.ToState(in observed, port.EnumTypeContentId, m_CandidateTexts);
                    }
                }
                m_CandidateTexts.PrepareOrdinals();
                m_Output.Invalidate();
                AdoptCandidateState();
                m_ResetGeneration = resetGeneration;
                m_Started = started;
                m_Faulted = faulted;
                m_LastInvocation = sequence == 0 ? default : new GraphInvocationIdentity(invocationSourceIdentityId, sequence);
                m_LastFailure = failure;
            }
            finally
            {
                ClearCandidateState();
            }
        }

        public GraphVariableFrame PublishRecordedFrame()
        {
            RequireIdle();
            m_Output.BeginWrite();
            ReadOnlySpan<TValue> values = StateValues;
            for (int i = 0; i < m_Bindings.PublishedSlots.Length; i++)
            {
                int slot = m_Bindings.PublishedSlots[i];
                TValue value = ReadState(slot, values);
                GraphHostValue published = m_Bindings.FromState(in value, m_Bindings.StateTypes[slot], m_StateTexts);
                m_Output.Write(i, in published);
            }
            return new GraphVariableFrame(VariableContract, m_Output.Publish(m_LastInvocation, m_ResetGeneration));
        }
    }
}
#endif
