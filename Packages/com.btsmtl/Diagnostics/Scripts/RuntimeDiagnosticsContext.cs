using System;
using System.Collections.Generic;

namespace BTSMTL.Diagnostics
{
    public sealed class RuntimeDiagnosticsContext
    {
        readonly Stack<RuntimeInstanceKey> m_InstanceStack = new Stack<RuntimeInstanceKey>();
        readonly Dictionary<RuntimeInstanceKey, RuntimeSourceElementHandle> m_InstanceSources = new Dictionary<RuntimeInstanceKey, RuntimeSourceElementHandle>();
        readonly Dictionary<RuntimeContentRevision, IDebugSourceMap> m_SourceMaps = new();
        readonly double m_LogicTickDurationSeconds;
        ulong m_Sequence;
        ulong m_LogicTick;
        ulong m_PresentationFrame;
        double m_PresentationTimeSeconds;
        ulong m_RuntimeEpoch;
        Guid m_ExecutionBranchId;
        string m_SourceClockId = string.Empty;
        string m_SourceTickKind = string.Empty;

        public RuntimeDiagnosticsContext(
            Guid characterRuntimeId,
            Guid sessionId,
            RuntimeContentRevision revision,
            IDebugSourceMap sourceMap,
            RuntimeDiagnosticsStore store,
            int tickRate)
        {
            if (characterRuntimeId == Guid.Empty)
                throw new ArgumentException("Character runtime identity is required.", nameof(characterRuntimeId));
            if (sessionId == Guid.Empty)
                throw new ArgumentException("Runtime diagnostics session identity is required.", nameof(sessionId));
            CharacterRuntimeId = characterRuntimeId;
            SessionId = sessionId;
            AbilityExecutionRuntimeId = Guid.NewGuid();
            Revision = revision;
            SourceMap = sourceMap ?? throw new ArgumentNullException(nameof(sourceMap));
            Store = store ?? throw new ArgumentNullException(nameof(store));
            m_LogicTickDurationSeconds = 1d / tickRate;
            RegisterSourceMap(SourceMap);
        }

        public Guid CharacterRuntimeId { get; }
        public Guid SessionId { get; }
        public Guid AbilityExecutionRuntimeId { get; }
        public RuntimeContentRevision Revision { get; private set; }
        public ulong RuntimeEpoch => m_RuntimeEpoch;
        public Guid ExecutionBranchId => m_ExecutionBranchId;
        public IDebugSourceMap SourceMap { get; private set; }
        public Dictionary<RuntimeContentRevision, IDebugSourceMap>.ValueCollection SourceMaps => m_SourceMaps.Values;
        public int SourceMapCapacity => m_SourceMaps.EnsureCapacity(0);

        public void ReserveSourceMaps(int additionalCapacity) =>
            m_SourceMaps.EnsureCapacity(m_SourceMaps.Count + additionalCapacity);

        public bool TryGetSourceMap(RuntimeContentRevision revision, out IDebugSourceMap sourceMap) =>
            m_SourceMaps.TryGetValue(revision, out sourceMap);

        public IDebugSourceMap GetSourceMap(RuntimeContentRevision revision) => m_SourceMaps[revision];

        public void RegisterSourceMap(IDebugSourceMap sourceMap)
        {
            m_SourceMaps.TryAdd(sourceMap.Revision, sourceMap);
        }

        public void RetireSourceMap(RuntimeContentRevision revision)
        {
            m_SourceMaps.Remove(revision);
        }
        public RuntimeDiagnosticsStore Store { get; }
        public ulong LogicTick => m_LogicTick;
        public ulong PresentationFrame => m_PresentationFrame;
        public double LogicTimeSeconds => m_LogicTick * m_LogicTickDurationSeconds;
        public double PresentationTimeSeconds => m_PresentationTimeSeconds;
        public RuntimeInstanceKey CurrentRuntimeInstance => m_InstanceStack.Count > 0
            ? m_InstanceStack.Peek()
            : RuntimeInstanceKey.Character(CharacterRuntimeId);

        public void AdoptRuntime(RuntimeContentRevision revision, IDebugSourceMap sourceMap)
        {
            Revision = revision;
            SourceMap = sourceMap ?? throw new ArgumentNullException(nameof(sourceMap));
            RegisterSourceMap(SourceMap);
            m_InstanceStack.Clear();
            m_InstanceSources.Clear();
            m_SourceClockId = string.Empty;
            m_SourceTickKind = string.Empty;
            Store.ClearLiveState();
        }

        public void SetRuntimeEpoch(ulong runtimeEpoch)
        {
            if (runtimeEpoch == 0)
                throw new ArgumentOutOfRangeException(nameof(runtimeEpoch));
            if (m_RuntimeEpoch != 0 && runtimeEpoch < m_RuntimeEpoch)
                throw new InvalidOperationException("Runtime diagnostics Runtime Epoch cannot move backwards.");
            m_RuntimeEpoch = runtimeEpoch;
        }

        public void SetExecutionBranch(Guid executionBranchId)
        {
            if (executionBranchId == Guid.Empty)
                throw new ArgumentException("Runtime diagnostics Execution Branch identity is required.", nameof(executionBranchId));
            if (m_ExecutionBranchId == executionBranchId)
                return;
            m_ExecutionBranchId = executionBranchId;
            m_SourceClockId = string.Empty;
            m_SourceTickKind = string.Empty;
            Store.ClearLiveState();
        }

        public void BeginLogicTick(
            ulong localLogicTick,
            string sourceClockId = "",
            string sourceTickKind = "")
        {
            m_LogicTick = localLogicTick;
            if (!string.IsNullOrEmpty(sourceClockId))
                m_SourceClockId = sourceClockId;
            if (!string.IsNullOrEmpty(sourceTickKind))
                m_SourceTickKind = sourceTickKind;
        }

        public void BeginPresentationFrame(ulong presentationFrame, float deltaSeconds)
        {
            m_PresentationFrame = presentationFrame;
            m_PresentationTimeSeconds += deltaSeconds;
        }

        public void RestoreRecordedTime(ulong logicTick, double presentationTimeSeconds)
        {
            m_LogicTick = logicTick;
            m_PresentationTimeSeconds = presentationTimeSeconds;
        }

        public bool ShouldPublish(RuntimeTraceChannel channel, RuntimeTraceEventKind kind) => Store.ShouldPublish(channel, kind);

        public bool ShouldCapture(RuntimeTraceChannel channel, RuntimeTraceEventKind kind) => Store.ShouldCapture(channel, kind);

        public RuntimeSourceElementHandle ResolveSourceHandle(RuntimeSourceElementKey source) =>
            ResolveSourceHandle(SourceMap, source);

        static RuntimeSourceElementHandle ResolveSourceHandle(IDebugSourceMap sourceMap, RuntimeSourceElementKey source)
        {
            if (!sourceMap.TryGetHandle(source, out RuntimeSourceElementHandle handle))
                throw new InvalidOperationException($"Runtime diagnostics source is absent from the exact Source Map: {source.Kind}/{source.GraphAuthoringId}/{source.ElementAuthoringId}/{source.TimelineAuthoringId}/{source.TrackAuthoringId}/{source.ClipAuthoringId}.");
            return handle;
        }

        public void PushRuntimeInstance(RuntimeInstanceKey instance)
        {
            if (!instance.IsValid || instance.CharacterRuntimeId != CharacterRuntimeId)
                throw new InvalidOperationException("Runtime diagnostics instance does not belong to this Character target.");
            m_InstanceStack.Push(instance);
        }

        public void PopRuntimeInstance(RuntimeInstanceKey instance)
        {
            if (m_InstanceStack.Count == 0 || !m_InstanceStack.Peek().Equals(instance))
                throw new InvalidOperationException("Runtime diagnostics instance stack is unbalanced.");
            m_InstanceStack.Pop();
        }

        public bool Publish(
            RuntimeTraceChannel channel,
            RuntimeTraceDomain domain,
            RuntimeTraceEventKind kind,
            RuntimeSourceElementKey source,
            RuntimeInstanceKey runtimeInstance,
            in RuntimeTracePayload payload)
        {
            if (!Store.ShouldPublish(channel, kind))
                return false;
            RuntimeSourceElementHandle handle = ResolveSourceHandle(source);
            if (runtimeInstance.IsValid)
                m_InstanceSources[runtimeInstance] = handle;
            return PublishCore(Revision, channel, domain, kind, handle, runtimeInstance, payload);
        }

        public bool Publish(
            IDebugSourceMap sourceMap,
            RuntimeTraceChannel channel,
            RuntimeTraceDomain domain,
            RuntimeTraceEventKind kind,
            RuntimeSourceElementKey source,
            RuntimeInstanceKey runtimeInstance,
            in RuntimeTracePayload payload)
        {
            if (!Store.ShouldPublish(channel, kind))
                return false;
            return PublishCore(sourceMap.Revision, channel, domain, kind,
                ResolveSourceHandle(sourceMap, source), runtimeInstance, payload);
        }

        public bool Publish(
            RuntimeTraceChannel channel,
            RuntimeTraceDomain domain,
            RuntimeTraceEventKind kind,
            RuntimeSourceElementHandle source,
            RuntimeInstanceKey runtimeInstance,
            in RuntimeTracePayload payload)
        {
            if (!Store.ShouldPublish(channel, kind))
                return false;
            if (!source.IsValid && runtimeInstance.IsValid)
                m_InstanceSources.TryGetValue(runtimeInstance, out source);
            return PublishCore(Revision, channel, domain, kind, source, runtimeInstance, payload);
        }

        bool PublishCore(
            RuntimeContentRevision revision,
            RuntimeTraceChannel channel,
            RuntimeTraceDomain domain,
            RuntimeTraceEventKind kind,
            RuntimeSourceElementHandle source,
            RuntimeInstanceKey runtimeInstance,
            in RuntimeTracePayload sourcePayload)
        {
            m_Sequence++;
            if (m_Sequence == 0)
                m_Sequence++;
            ulong position = domain == RuntimeTraceDomain.Presentation ? m_PresentationFrame : m_LogicTick;
            RuntimeTraceEvent traceEvent = new RuntimeTraceEvent(
                SessionId,
                revision,
                m_RuntimeEpoch,
                m_ExecutionBranchId,
                domain,
                channel,
                position,
                domain == RuntimeTraceDomain.Presentation ? PresentationTimeSeconds : LogicTimeSeconds,
                m_Sequence,
                runtimeInstance.IsValid ? runtimeInstance : CurrentRuntimeInstance,
                source,
                kind,
                in sourcePayload,
                m_SourceClockId,
                m_SourceTickKind);
            return Store.Publish(in traceEvent);
        }

        public bool PublishTarget(RuntimeTraceEventKind kind, in RuntimeTracePayload payload)
        {
            return Publish(
                RuntimeTraceChannel.Graph,
                RuntimeTraceDomain.Lifecycle,
                kind,
                RuntimeSourceElementHandle.Invalid,
                RuntimeInstanceKey.Character(CharacterRuntimeId),
                payload);
        }
    }
}
