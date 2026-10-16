using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.EventGraphs
{
    public sealed partial class HostEventGraphRuntime<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TNumeric> : IDisposable
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TNumeric : struct, IEventGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion>
    {
        enum EvaluationStatus : byte
        {
            Idle,
            Prepared,
            Succeeded,
            Failed
        }

        readonly HostEventGraph m_Source;
        readonly EventGraphHostContract m_Contract;
        readonly EventGraphCompiledBindings<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric> m_Bindings;
        readonly OrchestrationExecutionStateLayout<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> m_Layout;
        readonly OrchestrationStateLayout m_MemoryLayout;
        readonly OrchestrationExecutionWorkspace<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> m_Workspace;
        readonly OrchestrationExtensionBindings<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>, ValueTuple> m_Extensions;
        readonly OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] m_Inputs;
        readonly OrchestrationIdentityValue[] m_TextRemap;
        readonly EventGraphVariableBuffer m_Output;
        byte[] m_State;
        byte[] m_Candidate;
        EventGraphTextStorage m_StateTexts;
        EventGraphTextStorage m_CandidateTexts;
        EventGraphExecutionFailure m_LastFailure;
        EventGraphExecutionFailure m_CandidateFailure;
        EventGraphInvocationContext m_Invocation;
        EventGraphInvocationIdentity m_LastInvocation;
        int m_CurrentOperationIndex = -1;
        ulong m_ResetGeneration;
        bool m_Started;
        bool m_RunStart;
        EvaluationStatus m_Status;
        bool m_Faulted;
        bool m_Disposed;

        public HostEventGraphRuntime(HostEventGraph source, EventGraphHostContract contract,
            IOrchestrationGraphContentEncoding<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> encoding, string numericTarget,
            Func<StableHash, NumericProfileId, TargetAbiVersion, int, int, int, int, int,
                OrchestrationExecutionStateLayout<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>> createStateLayout)
        {
            m_Source = source;
            m_Contract = contract;
            m_Bindings = new EventGraphCompiledBindings<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric>(source, contract, encoding, numericTarget);
            OrchestrationProgram<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> program = m_Bindings.Program;
            OrchestrationGraphContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> content = m_Bindings.Content;
            OrchestrationExecutionCapacity capacity = OrchestrationExecutionFrames<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>.PrepareCapacity(
                new[] { program }, new[] { new OrchestrationRootAdmission(
                    new[] { new OrchestrationExecutionRoot(0, program.RootOperationIndex) }, 1) },
                ReadOnlySpan<OrchestrationTimelineCallCapacity>.Empty);
            _ = OrchestrationExecutionTickCapacity.Prepare<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>(
                new[] { program }, new[] { content.Extensions },
                new[] { new OrchestrationProgramTickBudget(1, 1) }, capacity,
                ReadOnlySpan<OrchestrationTimelineTickContentCapacity>.Empty);
            OrchestrationExecutionStateCapacity stateCapacity = capacity.State;
            m_Layout = createStateLayout(StableHash.Compute(content.Identity.GraphId, content.Identity.SourceRevision),
                content.NumericProfile.Id, content.NumericProfile.AbiVersion,
                program.BlackboardCount, stateCapacity.FrameCount, stateCapacity.OperationsPerFrame,
                stateCapacity.FrameCount, stateCapacity.ValuesPerFrame);
            m_MemoryLayout = m_Layout.Layout;
            m_State = new byte[m_MemoryLayout.SizeBytes];
            m_Candidate = new byte[m_State.Length];
            OrchestrationStateLayout preparedLayout = m_Layout.Layout;
            OrchestrationStateBinding.AdoptMemory(in preparedLayout, in m_MemoryLayout, m_State);
            OrchestrationStateBinding.AdoptMemory(in preparedLayout, in m_MemoryLayout, m_Candidate);
            m_Inputs = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[m_Bindings.InputCapacity];
            m_Output = new EventGraphVariableBuffer(m_Bindings.PublishedSlots.Length, m_Bindings.Prepared.Identities.Table);
            m_Workspace = new OrchestrationExecutionWorkspace<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>(program, content, capacity, 0);
            m_Extensions = OrchestrationExtensionBindings<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>, ValueTuple>.Prepare(
                content.Extensions.Span, ReadOnlySpan<int>.Empty, Array.Empty<IOrchestrationExtension>());
#if UNITY_EDITOR
            InitializeEditor();
            PrepareEditor();
#endif
            int textCapacity = checked(program.ValueSlotCount + m_Inputs.Length
#if UNITY_EDITOR
                + m_VariableOverrides.Length + m_InputOverrides.Length + m_OutputOverrides.Length
                + m_ObservedInputs.Length + m_ObservedOutputs.Length + 1
#endif
                );
            m_StateTexts = new EventGraphTextStorage(m_Bindings.Prepared.Identities, textCapacity);
            m_CandidateTexts = new EventGraphTextStorage(m_Bindings.Prepared.Identities, textCapacity);
            m_TextRemap = new OrchestrationIdentityValue[textCapacity + 1];
            Reset();
        }

        ReadOnlySpan<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> StateValues =>
            OrchestrationStateBinding.BindCommitted(m_Layout, m_State).Frames.Values(0);
        Span<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> CandidateValues =>
            OrchestrationStateBinding.BindCandidate(m_Layout, m_Candidate).Frames.Values(0);

        public HostEventGraph Source => m_Source;
        public EventGraphHostContract Contract => m_Contract;
        public int SourceIdentityId => m_Bindings.SourceIdentityId;
        public EventGraphVariableContract VariableContract => m_Bindings.Variables;
        public ulong ResetGeneration => m_ResetGeneration;
        public bool IsStarted => m_Started;
        public bool IsFaulted => m_Faulted;
        public bool IsPrepared => m_Status != EvaluationStatus.Idle;
        public EventGraphExecutionFailure LastFailure => m_LastFailure;

        public void Prepare(EventGraphInvocationContext invocation, IEventGraphHostContext hostContext)
        {
            RequireIdle();
            m_Status = m_Faulted ? EvaluationStatus.Failed : EvaluationStatus.Prepared;
            m_Invocation = invocation;
            m_CandidateFailure = m_LastFailure;
            m_RunStart = !m_Started;
            if (m_Faulted)
                return;
            m_CurrentOperationIndex = -1;
            if (!invocation.Identity.Equals(hostContext.Invocation.Identity) ||
                invocation.EventId != hostContext.Invocation.EventId ||
                invocation.DeltaSeconds != hostContext.Invocation.DeltaSeconds)
            {
                Reject("Event graph host context does not match the invocation.");
                return;
            }
            if (invocation.EventId != m_Contract.UpdateEventId)
            {
                Reject($"Event graph event '{invocation.EventId}' is not declared by host contract '{m_Source.AuthoringId}'.");
                return;
            }
            if (invocation.Identity.SourceIdentityId != SourceIdentityId)
            {
                Reject("Event graph invocation source does not match the prepared host contract.");
                return;
            }
            if (m_LastInvocation.IsValid && invocation.Identity.Sequence <= m_LastInvocation.Sequence)
            {
                Reject("Event graph invocation identity is not increasing.");
                return;
            }
            try
            {
                BeginCandidateState();
                m_Bindings.PrepareInputs(hostContext, m_Inputs, m_CandidateTexts);
                m_CandidateTexts.PrepareOrdinals();
            }
            catch (Exception exception)
            {
                Reject(exception.Message, exception);
            }
        }

        public void Compute()
        {
            if (m_Status == EvaluationStatus.Failed)
                return;
            if (m_Status != EvaluationStatus.Prepared)
                throw new InvalidOperationException("图实例只能计算本次已准备的输入和候选。");
            var state = OrchestrationStateBinding.BindCandidate(m_Layout, m_Candidate);
            var input = new OrchestrationFrameInput<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>(tick: m_Invocation.Identity.Sequence,
                instanceGeneration: m_ResetGeneration, hostInputs: m_Inputs, requestIdentityIds: default
#if UNITY_EDITOR
                , captureValues: true, useOverrides: true, valueOverrides: m_CandidateVariableOverrides,
                inputOverrides: m_CandidateInputOverrides, outputOverrides: m_CandidateOutputOverrides
#endif
                );
            OrchestrationIdentityOrdinalView identities = m_CandidateTexts.Ordinals;
            ValueTuple context = default;
            var cursor = state.CreateCursor<TScalar, TVector2, TVector3, TQuaternion, TMath,
                OrchestrationValueAccess<TScalar, TVector2, TVector3, TYaw, TQuaternion>, ValueTuple>(
                m_Bindings.Program, m_Workspace, m_Extensions, 0, ReadOnlySpan<int>.Empty,
                null, null, in input, in identities, in context);
            try
            {
                if (m_RunStart && m_Bindings.Start.OperationIndex >= 0)
                    cursor.Advance(m_Bindings.Start);
#if UNITY_EDITOR
                ExecuteTriggers(ref cursor);
#endif
                cursor.Advance(m_Bindings.Update);
                m_CurrentOperationIndex = cursor.CurrentOperationIndex;
                m_Status = EvaluationStatus.Succeeded;
            }
            catch (Exception exception)
            {
                m_CurrentOperationIndex = cursor.CurrentOperationIndex;
                Reject(exception.Message, exception);
            }
        }

        public void Discard()
        {
            ClearCandidateState();
            m_Invocation = default;
            m_CandidateFailure = null;
            m_Status = EvaluationStatus.Idle;
        }

        public EventGraphExecutionResult Adopt()
        {
            if (m_Status == EvaluationStatus.Prepared || m_Status == EvaluationStatus.Idle)
                throw new InvalidOperationException("图 owner 只能采用已完成的本次计算结果。");
            if (m_Status == EvaluationStatus.Failed)
                return AdoptFailure();
            try
            {
                m_Output.BeginWrite();
                Span<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> values = CandidateValues;
                for (int i = 0; i < m_Bindings.PublishedSlots.Length; i++)
                {
                    int slot = m_Bindings.PublishedSlots[i];
#if UNITY_EDITOR
                    OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value = m_CandidateVariableOverrides[slot].Kind != default
                        ? m_CandidateVariableOverrides[slot] : values[slot];
#else
                    OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value = values[slot];
#endif
                    EventGraphValue published = m_Bindings.FromState(in value, m_Bindings.StateTypes[slot], m_CandidateTexts);
                    m_Output.Write(i, in published);
                }
            }
            catch (Exception exception)
            {
                Reject(exception.Message, exception);
                return AdoptFailure();
            }
#if UNITY_EDITOR
            AdoptEditor();
#endif
            AdoptCandidateState();
            m_Started = true;
            m_LastInvocation = m_Invocation.Identity;
            EventGraphValueFrame frame = m_Output.Publish(m_Invocation.Identity, m_ResetGeneration);
            Discard();
            return EventGraphExecutionResult.Success(new EventGraphVariableFrame(VariableContract, frame));
        }

        EventGraphExecutionResult AdoptFailure()
        {
            m_LastFailure = m_CandidateFailure;
            m_Faulted = true;
            ResetState();
            EventGraphExecutionResult failed = EventGraphExecutionResult.Failed(m_LastFailure);
            Discard();
            return failed;
        }

        public EventGraphExecutionResult Execute(EventGraphInvocationContext invocation, IEventGraphHostContext hostContext)
        {
            Prepare(invocation, hostContext);
            Compute();
            return Adopt();
        }

        public void Reset()
        {
            RequireIdle();
            m_ResetGeneration = checked(m_ResetGeneration + 1);
            ResetState();
            m_LastFailure = null;
            m_LastInvocation = default;
            m_Faulted = false;
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            RequireIdle();
            m_Output.Invalidate();
#if UNITY_EDITOR
            ResetEditor();
#endif
            m_StateTexts.Reset();
            m_CandidateTexts.Reset();
            m_LastInvocation = default;
            m_Disposed = true;
        }

        internal OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> ReadState(int slot,
            ReadOnlySpan<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> values)
        {
#if UNITY_EDITOR
            if (IsVariableLocked(slot))
                return m_VariableOverrides[slot];
#endif
            return values[slot];
        }

        void BeginCandidateState()
        {
            Array.Clear(m_TextRemap, 0, m_TextRemap.Length);
            m_State.AsSpan().CopyTo(m_Candidate);
            Span<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> values = CandidateValues;
            for (int i = 0; i < values.Length; i++)
                RemapValue(ref values[i]);
#if UNITY_EDITOR
            PrepareEditorCandidateState();
#endif
        }

        void RemapValue(ref OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value)
        {
            if (value.Kind != OrchestrationValueKind.Identity ||
                value.Identity.Space != OrchestrationIdentitySpace.RuntimeText)
                return;
            int index = value.Identity.Index;
            ref OrchestrationIdentityValue mapped = ref m_TextRemap[index];
            if (mapped.Index == 0)
            {
                mapped = m_CandidateTexts.Capture(m_StateTexts.Resolve(value.Identity));
            }
            value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromIdentity(mapped);
        }

        void AdoptCandidateState()
        {
            byte[] previous = m_State;
            m_State = m_Candidate;
            m_Candidate = previous;
            EventGraphTextStorage previousTexts = m_StateTexts;
            m_StateTexts = m_CandidateTexts;
            m_CandidateTexts = previousTexts;
#if UNITY_EDITOR
            AdoptEditorCandidateState();
#endif
        }

        void ClearCandidateState()
        {
            Array.Clear(m_Inputs, 0, m_Inputs.Length);
            m_CandidateTexts.Reset();
#if UNITY_EDITOR
            ClearEditorCandidateState();
#endif
        }

        void ResetState()
        {
            m_Output.Invalidate();
            Array.Clear(m_State, 0, m_State.Length);
            m_StateTexts.Reset();
            var state = OrchestrationStateBinding.BindCandidate(m_Layout, m_State);
            var frames = state.Frames;
            InitializeRootFrame(ref frames, m_ResetGeneration);
            Span<OrchestrationBlackboardMetadata> blackboards = frames.BlackboardMetadata(0);
            for (int i = 0; i < m_Bindings.Program.BlackboardCount; i++)
            {
                ref readonly OrchestrationBlackboardSlot slot = ref m_Bindings.Program.Blackboard(i);
                if (slot.ScopeKind == OrchestrationScopeKind.Graph &&
                    slot.Lifetime == OrchestrationBlackboardLifetime.GraphInstance && slot.OwnerOperationIndex < 0)
                    blackboards[i] = new OrchestrationBlackboardMetadata
                    {
                        Owner = new OrchestrationBlackboardOwnerToken
                        {
                            ScopeKind = slot.ScopeKind,
                            CompiledOwnerIndex = slot.CompiledOwnerIndex,
                            Generation = m_ResetGeneration
                        },
                        Lifetime = slot.Lifetime
                    };
            }
#if UNITY_EDITOR
            ResetEditor();
#endif
            m_Started = false;
        }

        void InitializeRootFrame(ref OrchestrationExecutionFrames<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> frames,
            ulong generation)
        {
            frames.Insert(new OrchestrationExecutionFrameState
            {
                SkillContentId = SourceIdentityId,
                EntryOperationIndex = m_Bindings.Program.RootOperationIndex,
                OwnerOperationIndex = m_Bindings.Program.RootOperationIndex,
                ParentFrameIndex = -1,
                ParentTimelineOperationIndex = -1,
                Generation = generation
            }, m_Bindings.Program.Content.DefaultValues.Span);
        }

        void Reject(string message, Exception exception = null)
        {
            string node = m_CurrentOperationIndex < 0 ? string.Empty : m_Bindings.OperationNodes[m_CurrentOperationIndex];
            m_CandidateFailure = new EventGraphExecutionFailure(m_Source.AuthoringId, m_Invocation.EventId, node, message, exception);
            m_Status = EvaluationStatus.Failed;
        }

        internal void RequireIdle()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(HostEventGraphRuntime<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TNumeric>));
            if (m_Status != EvaluationStatus.Idle)
                throw new InvalidOperationException("图实例必须完成或丢弃已派发候选后才能改变生命周期。");
        }
    }
}
