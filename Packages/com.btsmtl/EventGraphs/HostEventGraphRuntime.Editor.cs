#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using FlowCanvas;
using NodeCanvas.Framework;
using ThirdPersonSimulation;

namespace BTSMTL.EventGraphs
{
    public sealed partial class HostEventGraphRuntime<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TNumeric>
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TNumeric : struct, IEventGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion>
    {
        const uint RecordedStateFormat = 0x45564752;
        const uint RecordedStateVersion = 2;
        readonly Dictionary<(string Node, string Port, bool Input), EventGraphRuntimePort> m_EditorPorts = new();
        EventGraphRuntimePort[] m_RuntimePorts;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_VariableOverrides;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_InputOverrides;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_OutputOverrides;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_ObservedInputs;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_ObservedOutputs;
        byte[] m_InputFlags;
        byte[] m_OutputFlags;
        List<(int Operation, int Port, bool Input)> m_Triggers;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_CandidateVariableOverrides;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_CandidateInputOverrides;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_CandidateOutputOverrides;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_CandidateObservedInputs;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_CandidateObservedOutputs;
        byte[] m_CandidateInputFlags;
        byte[] m_CandidateOutputFlags;
        List<(int Operation, int Port, bool Input)> m_CandidateTriggers;

        public IReadOnlyList<EventGraphRuntimePort> RuntimePorts => m_RuntimePorts;
        public EventGraphRuntimePort RequireRuntimePort(string node, string port, bool input) => m_EditorPorts[(node, port, input)];

        void InitializeEditor()
        {
            OrchestrationProgram<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> program = m_Bindings.Program;
            m_VariableOverrides = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[program.ValueSlotCount];
            m_InputOverrides = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[program.InputBindingCount];
            m_OutputOverrides = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[program.ValueEvaluationSlotCount];
            m_ObservedInputs = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[m_InputOverrides.Length];
            m_ObservedOutputs = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[m_OutputOverrides.Length];
            m_InputFlags = new byte[m_InputOverrides.Length];
            m_OutputFlags = new byte[m_OutputOverrides.Length];
            m_Triggers = new List<(int Operation, int Port, bool Input)>(m_Bindings.Content.Ports.Length);
            m_CandidateVariableOverrides = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[m_VariableOverrides.Length];
            m_CandidateInputOverrides = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[m_InputOverrides.Length];
            m_CandidateOutputOverrides = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[m_OutputOverrides.Length];
            m_CandidateObservedInputs = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[m_ObservedInputs.Length];
            m_CandidateObservedOutputs = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[m_ObservedOutputs.Length];
            m_CandidateInputFlags = new byte[m_InputFlags.Length];
            m_CandidateOutputFlags = new byte[m_OutputFlags.Length];
            m_CandidateTriggers = new List<(int Operation, int Port, bool Input)>(m_Triggers.Capacity);
        }

        public void QueuePortTrigger(string node, string port, bool input)
        {
            RequireIdle();
            EventGraphRuntimePort target = RequireRuntimePort(node, port, input);
            PrepareTriggerCapacity(m_Triggers.Count + 1);
            m_Triggers.Add((target.Operation, target.CompiledPortId, target.IsInput));
        }

        void ExecuteTriggers(ref OrchestrationExecutionCursor<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>, TScalar, TVector2,
            TVector3, TQuaternion, TMath, OrchestrationValueAccess<TScalar, TVector2, TVector3, TYaw, TQuaternion>, ValueTuple> cursor)
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

        public void MutateRuntimeVariable(string variableId, EventGraphValue value, EventGraphVariableMutation operation)
        {
            RequireIdle();
            int slot = m_Bindings.PublishedSlots[VariableContract.Layout.Bind(variableId).Index];
            if (operation != EventGraphVariableMutation.Release)
                RequireValue(value, m_Bindings.StateTypes[slot]);
            BeginCandidateState();
            try
            {
                OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> state = operation == EventGraphVariableMutation.Release ? default :
                    m_Bindings.ToState(in value, m_Bindings.Program.Content.DefaultValues.Span[slot].Enum.TypeContentId,
                        m_CandidateTexts);
                if (operation == EventGraphVariableMutation.Assign)
                    CandidateValues[slot] = state;
                m_CandidateVariableOverrides[slot] = operation == EventGraphVariableMutation.Override ? state : default;
                m_CandidateTexts.PrepareOrdinals();
                AdoptCandidateState();
            }
            finally
            {
                ClearCandidateState();
            }
        }

        public EventGraphValue ReadRuntimeVariable(string variableId)
        {
            int slot = m_Bindings.PublishedSlots[VariableContract.Layout.Bind(variableId).Index];
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value = ReadState(slot, StateValues);
            return m_Bindings.FromState(in value, m_Bindings.StateTypes[slot], m_StateTexts);
        }

        bool IsVariableLocked(int slot) => m_VariableOverrides[slot].Kind != default;

        public bool IsRuntimeVariableLocked(string variableId) =>
            IsVariableLocked(m_Bindings.PublishedSlots[VariableContract.Layout.Bind(variableId).Index]);

        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] PortOverrides(EventGraphRuntimePort port) => port.IsInput ? m_InputOverrides : m_OutputOverrides;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] PortObservations(EventGraphRuntimePort port) => port.IsInput ? m_ObservedInputs : m_ObservedOutputs;
        byte[] PortFlags(EventGraphRuntimePort port) => port.IsInput ? m_InputFlags : m_OutputFlags;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] CandidatePortOverrides(EventGraphRuntimePort port) =>
            port.IsInput ? m_CandidateInputOverrides : m_CandidateOutputOverrides;
        OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] CandidatePortObservations(EventGraphRuntimePort port) =>
            port.IsInput ? m_CandidateObservedInputs : m_CandidateObservedOutputs;
        byte[] CandidatePortFlags(EventGraphRuntimePort port) =>
            port.IsInput ? m_CandidateInputFlags : m_CandidateOutputFlags;

        internal EventGraphValue ReadPort(EventGraphRuntimePort port, bool input)
        {
            int slot = port.ValueSlot;
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value = PortFlags(port)[slot] != 0 ? PortOverrides(port)[slot] :
                input ? default : PortObservations(port)[slot];
            return value.Kind == default ? default : m_Bindings.FromState(in value, port.ValueType, m_StateTexts);
        }

        internal bool IsPortLocked(EventGraphRuntimePort port) => PortFlags(port)[port.ValueSlot] == 3;

        internal void MutatePort(EventGraphRuntimePort port, EventGraphValue value, EventGraphVariableMutation operation)
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

        void ApplyCandidatePortMutation(EventGraphRuntimePort port, in EventGraphValue value,
            EventGraphVariableMutation operation)
        {
            int slot = port.ValueSlot;
            if (operation == EventGraphVariableMutation.Release)
            {
                CandidatePortOverrides(port)[slot] = default;
                CandidatePortFlags(port)[slot] = 0;
            }
            else
            {
                RequireValue(value, port.ValueType);
                CandidatePortOverrides(port)[slot] = m_Bindings.ToState(in value, port.EnumTypeContentId, m_CandidateTexts);
                CandidatePortFlags(port)[slot] = operation == EventGraphVariableMutation.Override ? (byte)3 : (byte)1;
            }
        }

        static void RequireValue(EventGraphValue value, Type type)
        {
            EventGraphValueKinds.TryGet(type, out EventGraphValueKind expected);
            if (value.Kind != expected || expected == EventGraphValueKind.Enum && value.EnumType != type)
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
            m_RuntimePorts = new EventGraphRuntimePort[m_EditorPorts.Count];
            m_EditorPorts.Values.CopyTo(m_RuntimePorts, 0);

            void Add(Port port, bool input)
            {
                OrchestrationGraphPort map = maps[(port.parent.UID, port.ID, input)];
                int enumIdentity = port.type.IsEnum ? identities.Bind(port.type.AssemblyQualifiedName) : 0;
                m_EditorPorts.Add((port.parent.UID, port.ID, input),
                    new EventGraphRuntimePort(Source.AuthoringId, port, input, map.OperationIndex, map.ValueIndex,
                        map.CompiledPortIdentityId, enumIdentity, ReadPort, MutatePort, IsPortLocked,
                        () => QueuePortTrigger(port.parent.UID, port.ID, input)));
            }
        }

        void AdoptEditor()
        {
            CopyObserved(m_Workspace.ObservedInputs, m_Workspace.WrittenInputs, m_CandidateObservedInputs);
            CopyObserved(m_Workspace.ObservedOutputs, m_Workspace.WrittenOutputs, m_CandidateObservedOutputs);
            CompleteOverrides(m_CandidateInputOverrides, m_CandidateInputFlags);
            CompleteOverrides(m_CandidateOutputOverrides, m_CandidateOutputFlags);
            m_CandidateTriggers.Clear();
        }

        void PrepareTriggerCapacity(int count)
        {
            m_Triggers.Capacity = Math.Max(m_Triggers.Capacity, count);
            m_CandidateTriggers.Capacity = Math.Max(m_CandidateTriggers.Capacity, count);
        }

        void PrepareEditorCandidateState()
        {
            RemapValues(m_VariableOverrides, m_CandidateVariableOverrides);
            RemapValues(m_InputOverrides, m_CandidateInputOverrides);
            RemapValues(m_OutputOverrides, m_CandidateOutputOverrides);
            RemapValues(m_ObservedInputs, m_CandidateObservedInputs);
            RemapValues(m_ObservedOutputs, m_CandidateObservedOutputs);
            m_InputFlags.AsSpan().CopyTo(m_CandidateInputFlags);
            m_OutputFlags.AsSpan().CopyTo(m_CandidateOutputFlags);
            m_CandidateTriggers.Clear();
            for (int i = 0; i < m_Triggers.Count; i++)
                m_CandidateTriggers.Add(m_Triggers[i]);
        }

        void RemapValues(ReadOnlySpan<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> source, Span<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> destination)
        {
            source.CopyTo(destination);
            for (int i = 0; i < source.Length; i++)
                RemapValue(ref destination[i]);
        }

        void AdoptEditorCandidateState()
        {
            Swap(ref m_VariableOverrides, ref m_CandidateVariableOverrides);
            Swap(ref m_InputOverrides, ref m_CandidateInputOverrides);
            Swap(ref m_OutputOverrides, ref m_CandidateOutputOverrides);
            Swap(ref m_ObservedInputs, ref m_CandidateObservedInputs);
            Swap(ref m_ObservedOutputs, ref m_CandidateObservedOutputs);
            Swap(ref m_InputFlags, ref m_CandidateInputFlags);
            Swap(ref m_OutputFlags, ref m_CandidateOutputFlags);
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
            Array.Clear(m_CandidateVariableOverrides, 0, m_CandidateVariableOverrides.Length);
            Array.Clear(m_CandidateInputOverrides, 0, m_CandidateInputOverrides.Length);
            Array.Clear(m_CandidateOutputOverrides, 0, m_CandidateOutputOverrides.Length);
            Array.Clear(m_CandidateObservedInputs, 0, m_CandidateObservedInputs.Length);
            Array.Clear(m_CandidateObservedOutputs, 0, m_CandidateObservedOutputs.Length);
            Array.Clear(m_CandidateInputFlags, 0, m_CandidateInputFlags.Length);
            Array.Clear(m_CandidateOutputFlags, 0, m_CandidateOutputFlags.Length);
            m_CandidateTriggers.Clear();
        }

        static void CopyObserved(ReadOnlySpan<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> observed, ReadOnlySpan<bool> written,
            Span<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> destination)
        {
            for (int i = 0; i < written.Length; i++)
                if (written[i])
                    destination[i] = observed[i];
        }

        static void CompleteOverrides(OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] values, byte[] flags)
        {
            for (int i = 0; i < flags.Length; i++)
                if (flags[i] == 1)
                {
                    values[i] = default;
                    flags[i] = 0;
                }
        }

        void ResetEditor()
        {
            Array.Clear(m_VariableOverrides, 0, m_VariableOverrides.Length);
            Array.Clear(m_InputOverrides, 0, m_InputOverrides.Length);
            Array.Clear(m_OutputOverrides, 0, m_OutputOverrides.Length);
            Array.Clear(m_ObservedInputs, 0, m_ObservedInputs.Length);
            Array.Clear(m_ObservedOutputs, 0, m_ObservedOutputs.Length);
            Array.Clear(m_InputFlags, 0, m_InputFlags.Length);
            Array.Clear(m_OutputFlags, 0, m_OutputFlags.Length);
            m_Triggers.Clear();
        }

        public void WriteRecordedFrameState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            RequireIdle();
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
            if (m_Faulted)
            {
                writer.WriteText(m_LastFailure.GraphId);
                writer.WriteText(m_LastFailure.EventId);
                writer.WriteText(m_LastFailure.NodeId);
                writer.WriteText(m_LastFailure.Message);
                writer.WriteText(m_LastFailure.ExceptionType);
            }
            writer.Write(m_Triggers.Count);
            for (int i = 0; i < m_Triggers.Count; i++)
            {
                var trigger = m_Triggers[i];
                writer.Write(trigger.Operation);
                writer.Write(trigger.Port);
                writer.Write(trigger.Input);
            }
            var state = OrchestrationStateBinding.BindCommitted(m_Layout, m_State);
            var frames = state.Frames;
            foreach (ref readonly OrchestrationOperationState operation in frames.Operations(0))
                writer.Write(operation);
            foreach (ref readonly OrchestrationBlackboardMetadata metadata in frames.BlackboardMetadata(0))
                writer.Write(metadata);
            ReadOnlySpan<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> values = frames.Values(0);
            for (int i = 0; i < m_Bindings.Program.ValueSlotCount; i++)
            {
                OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value = values[i];
                m_Bindings.FromState(in value, m_Bindings.StateTypes[i], m_StateTexts).WriteRecordedFrameState(ref writer);
                writer.Write(IsVariableLocked(i));
                if (IsVariableLocked(i))
                    m_Bindings.FromState(in m_VariableOverrides[i], m_Bindings.StateTypes[i], m_StateTexts)
                        .WriteRecordedFrameState(ref writer);
            }
            for (int i = 0; i < m_RuntimePorts.Length; i++)
            {
                EventGraphRuntimePort port = m_RuntimePorts[i];
                if (port.IsTrigger)
                    continue;
                port.WriteRecordedFrameState(ref writer);
                OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> observed = PortObservations(port)[port.ValueSlot];
                writer.Write(observed.Kind != default);
                if (observed.Kind != default)
                    m_Bindings.FromState(in observed, port.ValueType, m_StateTexts).WriteRecordedFrameState(ref writer);
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
                EventGraphExecutionFailure failure = faulted
                    ? new EventGraphExecutionFailure(reader.ReadText(), reader.ReadText(),
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
                Span<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> values = frames.Values(0);
                for (int i = 0; i < m_Bindings.Program.ValueSlotCount; i++)
                {
                    int enumIdentity = m_Bindings.Program.Content.DefaultValues.Span[i].Enum.TypeContentId;
                    EventGraphValue value = EventGraphValue.ReadRecordedFrameState(ref reader, m_Bindings.StateTypes[i]);
                    values[i] = m_Bindings.ToState(in value, enumIdentity, m_CandidateTexts);
                    if (reader.Read<bool>())
                    {
                        EventGraphValue locked = EventGraphValue.ReadRecordedFrameState(ref reader, m_Bindings.StateTypes[i]);
                        m_CandidateVariableOverrides[i] = m_Bindings.ToState(in locked, enumIdentity, m_CandidateTexts);
                    }
                }
                for (int i = 0; i < m_RuntimePorts.Length; i++)
                {
                    EventGraphRuntimePort port = m_RuntimePorts[i];
                    if (port.IsTrigger)
                        continue;
                    EventGraphVariableMutation mutation = EventGraphRuntimePort.ReadRecordedMutation(
                        ref reader, port.ValueType, out EventGraphValue supplied);
                    ApplyCandidatePortMutation(port, in supplied, mutation);
                    if (reader.Read<bool>())
                    {
                        EventGraphValue observed = EventGraphValue.ReadRecordedFrameState(ref reader, port.ValueType);
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
                m_LastInvocation = sequence == 0 ? default : new EventGraphInvocationIdentity(invocationSourceIdentityId, sequence);
                m_LastFailure = failure;
            }
            finally
            {
                ClearCandidateState();
            }
        }

        public EventGraphVariableFrame PublishRecordedFrame()
        {
            RequireIdle();
            m_Output.BeginWrite();
            ReadOnlySpan<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> values = StateValues;
            for (int i = 0; i < m_Bindings.PublishedSlots.Length; i++)
            {
                int slot = m_Bindings.PublishedSlots[i];
                OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value = ReadState(slot, values);
                EventGraphValue published = m_Bindings.FromState(in value, m_Bindings.StateTypes[slot], m_StateTexts);
                m_Output.Write(i, in published);
            }
            return new EventGraphVariableFrame(VariableContract, m_Output.Publish(m_LastInvocation, m_ResetGeneration));
        }
    }
}
#endif
