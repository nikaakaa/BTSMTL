using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs
{
    // TContext 是宿主交给扩展实现的调用句柄；没有扩展领域的宿主使用 ValueTuple。
    public sealed partial class HostedGraphRuntime<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TNumeric, TContext> : IDisposable
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
        enum EvaluationStatus : byte
        {
            Idle,
            Prepared,
            Succeeded,
            Failed
        }

        readonly BtsmtlGraphRoot m_Source;
        readonly GraphHostContract m_Contract;
        readonly HostedGraphBindings<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TNumeric> m_Bindings;
        readonly OrchestrationPreparedStateLayout<TValue> m_Layout;
        readonly OrchestrationStateRegion<TValue> m_PrivateValues;
        readonly OrchestrationStateLayout m_MemoryLayout;
        readonly OrchestrationExecutionWorkspace<TValue> m_Workspace;
        readonly OrchestrationExtensionBindings<TValue, TContext> m_Extensions;
        readonly TValue[] m_Inputs;
        readonly OrchestrationOutputRequest[] m_OutputRequests;
        readonly TValue[] m_OutputValues;
        int m_OutputRequestCount;
        int m_OutputValueCount;
        readonly OrchestrationIdentityValue[] m_TextRemap;
        readonly GraphVariableBuffer m_Output;
        byte[] m_State;
        byte[] m_Candidate;
        GraphTextStorage m_StateTexts;
        GraphTextStorage m_CandidateTexts;
        GraphExecutionFailure m_LastFailure;
        GraphExecutionFailure m_CandidateFailure;
        GraphInvocationContext m_Invocation;
        GraphInvocationIdentity m_LastInvocation;
        int m_CurrentOperationIndex = -1;
        ulong m_ResetGeneration;
        bool m_Started;
        bool m_RunStart;
        EvaluationStatus m_Status;
        bool m_Faulted;
        bool m_Disposed;

        public HostedGraphRuntime(BtsmtlGraphRoot source, GraphHostContract contract,
            IOrchestrationGraphContentEncoding<TValue> encoding,
            Func<StableHash, NumericProfileId, TargetAbiVersion, int, int, int, int, int, int, int, int,
                OrchestrationExecutionStateLayout<TValue>> createStateLayout,
            IReadOnlyList<IOrchestrationExtension> extensions)
        {
            m_Source = source;
            m_Contract = contract;
            m_Bindings = new HostedGraphBindings<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TNumeric>(source, contract, encoding);
            OrchestrationProgram<TValue> program = m_Bindings.Program;
            OrchestrationGraphContent<TValue> content = m_Bindings.Content;
            OrchestrationExecutionCapacity capacity = OrchestrationExecutionFrames<TValue>.PrepareCapacity(
                new[] { program }, new[] { new OrchestrationRootAdmission(
                    new[] { new OrchestrationExecutionRoot(0, program.RootOperationIndex) }, 1) },
                ReadOnlySpan<OrchestrationTimelineCallCapacity>.Empty);
            OrchestrationExecutionTickCapacity tickCapacity = OrchestrationExecutionTickCapacity.Prepare<TValue>(
                new[] { program }, new[] { content.Extensions },
                new[] { new OrchestrationProgramTickBudget(1, 1) }, capacity,
                ReadOnlySpan<OrchestrationTimelineTickContentCapacity>.Empty);
            OrchestrationExecutionStateCapacity stateCapacity = capacity.State;
            OrchestrationExecutionStateLayout<TValue> executionLayout = createStateLayout(
                StableHash.Compute(content.Identity.GraphId, content.Identity.SourceRevision),
                content.NumericProfile.Id, content.NumericProfile.AbiVersion,
                stateCapacity.ValueCount, stateCapacity.FrameCount, stateCapacity.OperationCount,
                1, stateCapacity.SavedOutputCount, stateCapacity.TransitionCacheCount,
                stateCapacity.TransitionSnapshotCount, stateCapacity.ValueCount);
            m_PrivateValues = executionLayout.Values;
            m_Layout = new OrchestrationPreparedStateLayout<TValue>(executionLayout.Layout,
                executionLayout.StateSegments, stateCapacity.FrameCount,
                new[] { new OrchestrationPreparedProgramStorage<TValue>(m_Bindings.SourceIdentityId,
                    program.ExecutionStorage, program.Content.ValueSlotTypes,
                    program.ValueSchedule.TransitionCacheCount, program.ValueSchedule.TransitionSnapshotCount) },
                ReadOnlyMemory<OrchestrationValueSlotType>.Empty,
                new OrchestrationStateRegion<byte>(executionLayout.Layout.SizeBytes, 0), default);
            m_MemoryLayout = m_Layout.Layout;
            m_State = new byte[m_MemoryLayout.SizeBytes];
            m_Candidate = new byte[m_State.Length];
            OrchestrationStateLayout preparedLayout = m_Layout.Layout;
            OrchestrationStateBinding.AdoptMemory(in preparedLayout, in m_MemoryLayout, m_State);
            OrchestrationStateBinding.AdoptMemory(in preparedLayout, in m_MemoryLayout, m_Candidate);
            m_Inputs = new TValue[m_Bindings.InputCapacity];
            m_Output = new GraphVariableBuffer(m_Bindings.PublishedSlots.Length, m_Bindings.Prepared.Identities.Table);
            m_Workspace = new OrchestrationExecutionWorkspace<TValue>(
                program, content, capacity, tickCapacity.Programs.Span[0]);
            m_Extensions = OrchestrationExtensionBindings<TValue, TContext>.Prepare(
                content.Extensions.Span, OrchestrationExtensionRequirements.Collect(program, content.Extensions.Length),
                extensions ?? throw new ArgumentNullException(nameof(extensions)));
            // Start 与 Update 在同一次计算中各自推进，输出按两次调用的工作区容量保留。
            m_OutputRequests = new OrchestrationOutputRequest[checked(m_Workspace.OutputRequestCapacity * 2)];
            m_OutputValues = new TValue[checked(m_Workspace.OutputValueCapacity * 2)];
#if UNITY_EDITOR
            PrepareEditor();
            InitializeEditor();
#endif
            int textCapacity = checked(program.ValueSlotCount + m_Inputs.Length
#if UNITY_EDITOR
                + m_Overrides.Length
                + m_ObservedInputs.Length + m_ObservedOutputs.Length + 1
#endif
                );
            m_StateTexts = new GraphTextStorage(m_Bindings.Prepared.Identities, textCapacity);
            m_CandidateTexts = new GraphTextStorage(m_Bindings.Prepared.Identities, textCapacity);
            m_TextRemap = new OrchestrationIdentityValue[textCapacity + 1];
            Reset();
        }

        ReadOnlySpan<TValue> StateValues =>
            m_Layout.Layout.BindCommitted(m_State).Get(m_PrivateValues);
        Span<TValue> CandidateValues =>
            m_Layout.Layout.BindCandidate(m_Candidate).Get(m_PrivateValues);

        public BtsmtlGraphRoot Source => m_Source;
        public GraphHostContract Contract => m_Contract;
        public int SourceIdentityId => m_Bindings.SourceIdentityId;
        public GraphVariableContract VariableContract => m_Bindings.Variables;
        public ulong ResetGeneration => m_ResetGeneration;
        public bool IsStarted => m_Started;
        public bool IsFaulted => m_Faulted;
        public bool IsPrepared => m_Status != EvaluationStatus.Idle;
        public GraphExecutionFailure LastFailure => m_LastFailure;

        public void Prepare(GraphInvocationContext invocation, IGraphHostContext hostContext)
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

        public void Compute(in TContext context)
        {
#if UNITY_EDITOR
            Compute<OrchestrationWorkbenchDiagnostics>(in context);
#else
            Compute<OrchestrationProductDiagnostics>(in context);
#endif
        }

        void Compute<TDiagnostics>(in TContext context)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics
        {
            if (m_Status == EvaluationStatus.Failed)
                return;
            if (m_Status != EvaluationStatus.Prepared)
                throw new InvalidOperationException("图实例只能计算本次已准备的输入和候选。");
            var state = OrchestrationStateBinding.BindCandidate(m_Layout, m_Candidate);
            var input = new OrchestrationFrameInput<TValue>(tick: m_Invocation.Identity.Sequence,
                instanceGeneration: m_ResetGeneration, hostInputs: m_Inputs, requestIdentityIds: default
#if UNITY_EDITOR
                , captureValues: true, overrides: ActiveOverrides(m_CandidateOverrides)
#endif
                );
            OrchestrationIdentityOrdinalView identities = m_CandidateTexts.Ordinals;
            var cursor = state.CreateCursor<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
                TDiagnostics, OrchestrationArrayValueStorage<TValue>>(
                m_Bindings.Program, m_Workspace, m_Extensions, 0, ReadOnlySpan<int>.Empty,
                null, null, in input, in identities, in context);
            m_OutputRequestCount = 0;
            m_OutputValueCount = 0;
            try
            {
                if (m_RunStart && m_Bindings.Start.OperationIndex >= 0)
                {
                    cursor.Advance(m_Bindings.Start);
                    CollectOutputs();
                }
#if UNITY_EDITOR
                ExecuteTriggers(ref cursor);
#endif
                cursor.Advance(m_Bindings.Update);
                CollectOutputs();
                m_CurrentOperationIndex = cursor.CurrentOperationIndex;
                m_Status = EvaluationStatus.Succeeded;
            }
            catch (Exception exception)
            {
                m_CurrentOperationIndex = cursor.CurrentOperationIndex;
                Reject(exception.Message, exception);
            }
        }

        // 本次计算发出的编排输出，在 Adopt 或 Discard 前有效；值中的运行文本用 ResolveOutputText 读取。
        public ReadOnlySpan<OrchestrationOutputRequest> OutputRequests => m_OutputRequests.AsSpan(0, m_OutputRequestCount);
        public ReadOnlySpan<TValue> OutputValues =>
            m_OutputValues.AsSpan(0, m_OutputValueCount);
        public ref readonly OrchestrationOutputTemplate OutputTemplate(int templateIndex) =>
            ref m_Bindings.Program.OutputTemplate(templateIndex);
        public string ResolveOutputText(OrchestrationIdentityValue identity) => m_CandidateTexts.Resolve(identity);

        void CollectOutputs()
        {
            ReadOnlySpan<OrchestrationOutputRequest> requests = m_Workspace.EmittedOutputRequests;
            ReadOnlySpan<TValue> values = m_Workspace.EmittedOutputValues;
            values.CopyTo(m_OutputValues.AsSpan(m_OutputValueCount));
            for (int i = 0; i < requests.Length; i++)
            {
                ref readonly OrchestrationOutputRequest request = ref requests[i];
                m_OutputRequests[m_OutputRequestCount++] = new OrchestrationOutputRequest(request.OperationIndex,
                    request.OperationGeneration, request.InstanceGeneration, request.TemplateIndex,
                    new OrchestrationRange(request.Values.Offset + m_OutputValueCount, request.Values.Count),
                    request.HasTimelineInvocation, request.TimelineInvocation, request.TimelineHook,
                    request.TimelineSourceIdentityId, request.TimelineSourceGeneration, request.TimelineTime);
            }
            m_OutputValueCount += values.Length;
        }

        public void Discard()
        {
            m_OutputRequestCount = 0;
            m_OutputValueCount = 0;
            ClearCandidateState();
            m_Invocation = default;
            m_CandidateFailure = null;
            m_Status = EvaluationStatus.Idle;
        }

        public GraphExecutionResult Adopt()
        {
            if (m_Status == EvaluationStatus.Prepared || m_Status == EvaluationStatus.Idle)
                throw new InvalidOperationException("图 owner 只能采用已完成的本次计算结果。");
            if (m_Status == EvaluationStatus.Failed)
                return AdoptFailure();
            try
            {
                m_Output.BeginWrite();
                Span<TValue> values = CandidateValues;
                for (int i = 0; i < m_Bindings.PublishedSlots.Length; i++)
                {
                    int slot = m_Bindings.PublishedSlots[i];
                    int valueIndex = m_Bindings.Program.ExecutionStorage.Values.Span[slot].ValueIndex;
#if UNITY_EDITOR
                    int supplied = FindOverride(m_CandidateOverrides, OrchestrationValueOverrideKind.Blackboard, slot);
                    TValue value = supplied >= 0
                        ? m_CandidateOverrides[supplied].Value : values[valueIndex];
#else
                    TValue value = values[valueIndex];
#endif
                    GraphHostValue published = m_Bindings.FromState(in value, m_Bindings.StateTypes[slot], m_CandidateTexts);
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
            GraphHostValueFrame frame = m_Output.Publish(m_Invocation.Identity, m_ResetGeneration);
            Discard();
            return GraphExecutionResult.Success(new GraphVariableFrame(VariableContract, frame));
        }

        GraphExecutionResult AdoptFailure()
        {
            m_LastFailure = m_CandidateFailure;
            m_Faulted = true;
            ResetState();
            GraphExecutionResult failed = GraphExecutionResult.Failed(m_LastFailure);
            Discard();
            return failed;
        }

        public GraphExecutionResult Execute(GraphInvocationContext invocation, IGraphHostContext hostContext, in TContext context)
        {
            Prepare(invocation, hostContext);
            Compute(in context);
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

        internal TValue ReadState(int slot, ReadOnlySpan<TValue> values)
        {
#if UNITY_EDITOR
            int supplied = FindVariableOverride(slot);
            if (supplied >= 0)
                return m_Overrides[supplied].Value;
#endif
            return values[m_Bindings.Program.ExecutionStorage.Values.Span[slot].ValueIndex];
        }

        void BeginCandidateState()
        {
            Array.Clear(m_TextRemap, 0, m_TextRemap.Length);
            OrchestrationStateBinding.CopyMemory(m_Layout, m_State, m_Candidate);
            Span<TValue> values = CandidateValues;
            for (int i = 0; i < values.Length; i++)
                RemapValue(ref values[i]);
            var frames = OrchestrationStateBinding.BindCandidate(m_Layout, m_Candidate).Frames;
            Span<TValue> savedOutputs = frames.SavedOutputs(0);
            for (int i = 0; i < savedOutputs.Length; i++)
                RemapValue(ref savedOutputs[i]);
            var snapshots = frames.TransitionSnapshots(0);
            for (int i = 0; i < snapshots.Length; i++)
                RemapValue(ref snapshots[i].Value);
#if UNITY_EDITOR
            PrepareEditorCandidateState();
#endif
        }

        void RemapValue(ref TValue value)
        {
            TValues values = default;
            if (values.Kind(in value) != OrchestrationValueKind.Identity)
                return;
            OrchestrationIdentityValue identity = values.Identity(in value);
            if (identity.Space != OrchestrationIdentitySpace.RuntimeText)
                return;
            int index = identity.Index;
            ref OrchestrationIdentityValue mapped = ref m_TextRemap[index];
            if (mapped.Index == 0)
            {
                mapped = m_CandidateTexts.Capture(m_StateTexts.Resolve(identity));
            }
            value = values.FromIdentity(mapped);
        }

        void AdoptCandidateState()
        {
            byte[] previous = m_State;
            m_State = m_Candidate;
            m_Candidate = previous;
            GraphTextStorage previousTexts = m_StateTexts;
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
                    blackboards[m_Bindings.Program.ExecutionStorage.Values.Span[i].MetadataIndex] =
                        new OrchestrationBlackboardMetadata
                        {
                            Owner = new OrchestrationBlackboardOwnerToken
                            {
                                ScopeKind = slot.ScopeKind,
                                CompiledOwnerIndex = slot.CompiledOwnerIndex,
                                Generation = m_ResetGeneration
                            }
                        };
            }
#if UNITY_EDITOR
            ResetEditor();
#endif
            m_Started = false;
        }

        void InitializeRootFrame(ref OrchestrationExecutionFrames<TValue> frames,
            ulong generation)
        {
            frames.Insert<OrchestrationArrayValueStorage<TValue>>(new OrchestrationExecutionFrameState
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
            m_CandidateFailure = new GraphExecutionFailure(m_Source.AuthoringId, m_Invocation.EventId, node, message, exception);
            m_Status = EvaluationStatus.Failed;
        }

        internal void RequireIdle()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(HostedGraphRuntime<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TNumeric, TContext>));
            if (m_Status != EvaluationStatus.Idle)
                throw new InvalidOperationException("图实例必须完成或丢弃已派发候选后才能改变生命周期。");
        }
    }
}
