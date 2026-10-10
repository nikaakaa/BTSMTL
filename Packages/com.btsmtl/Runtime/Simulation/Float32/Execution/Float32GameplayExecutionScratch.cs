using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal sealed class Float32GameplayEffectExecutionScratch : IExecutionWorkspaceScratch
    {
        readonly List<PortableActiveEffectState> m_AvailableActiveStates = new List<PortableActiveEffectState>();
        readonly List<PortableActiveEffectState> m_RetiredActiveStates = new List<PortableActiveEffectState>();
        readonly List<PortableEffectSpecState> m_AvailableSpecs = new List<PortableEffectSpecState>();
        readonly List<PortableEffectSpecState> m_CreatedSpecLeases = new List<PortableEffectSpecState>();
        readonly List<PortableEffectSpecState> m_DeferredSpecReleases = new List<PortableEffectSpecState>();
        readonly List<PortablePredictionRecord> m_AvailablePredictions = new List<PortablePredictionRecord>();
        readonly List<PortablePredictionRecord> m_DeferredPredictionReleases = new List<PortablePredictionRecord>();
        GameplayEffectListBank<SimulationSetByCallerValue> m_AdditionalValues;
        GameplayEffectListBank<SimulationAttributeCapture> m_AdditionalAttributes;
        readonly Dictionary<IReadOnlyList<SimulationSetByCallerValue>, List<SimulationAttributeCapture>> m_AdditionalLeases =
            new Dictionary<IReadOnlyList<SimulationSetByCallerValue>, List<SimulationAttributeCapture>>();

        public List<PortableEffectRuntimeChange> Changes { get; } = new List<PortableEffectRuntimeChange>();
        public ulong[] RemovedHandles { get; private set; } = Array.Empty<ulong>();
        public int RemovedHandleCount { get; set; }
        public Dictionary<ulong, PortableEffectCause> Causes { get; } = new Dictionary<ulong, PortableEffectCause>();
        public GameplayEffectListBank<PortableActiveEffectState> ActiveEffects { get; private set; }
        public GameplayEffectListBank<ulong> PredictionKeys { get; private set; }
        internal GameplayEffectListBank<PortablePredictionRecord> JournalGroups { get; private set; }
        public Dictionary<CanonicalTextValue, Float32Scalar> SuppliedSourceAttributes { get; } =
            new Dictionary<CanonicalTextValue, Float32Scalar>();
        public HashSet<ulong> ActiveHandles { get; } = new HashSet<ulong>();
        public HashSet<ulong> ActiveInstances { get; } = new HashSet<ulong>();
        public List<GameplayEffectActiveIdentity> ActiveIdentities { get; } = new List<GameplayEffectActiveIdentity>();
        public HashSet<string> FrozenOwnedTags { get; } = new HashSet<string>(StringComparer.Ordinal);
        internal GameplayEffectRuntimeCapacityLayout Capacity { get; private set; }
        internal int CommandsThisTick;
        internal GameplayCommandBatchAdmission ReceiveCommandBatch(ActorId actorId, ulong tick, int commands, SimulationExecutionSource source)
        {
            var admission = new GameplayCommandBatchAdmission(actorId, tick, commands,
                (long)CommandsThisTick + commands, commands, Capacity.Actor.CommandsPerTick, source);
            if (admission.Accepted)
                CommandsThisTick += commands;
            return admission;
        }

        internal int SpecApplicationsThisTick;
        internal int LifecycleExecutionsThisTick;

        public Float32GameplayEffectExecutionScratch()
        {
            Target = new Float32GameplayEffectTarget(this);
        }

        internal Float32GameplayEffectTarget Target { get; }


        internal void Prepare(Float32GameplayEffectRuntimeCatalog catalog, GameplayEffectRuntimeCapacityLayout capacity)
        {
            Capacity = capacity;
            m_AvailableActiveStates.Capacity = capacity.ActiveObjectCount;
            m_RetiredActiveStates.Capacity = capacity.ActiveObjectCount;
            for (int i = 0; i < capacity.ActiveObjectCount; i++)
                m_AvailableActiveStates.Add(new PortableActiveEffectState());
            m_AvailableSpecs.Capacity = capacity.SpecCount;
            m_CreatedSpecLeases.Capacity = capacity.SpecCount;
            m_DeferredSpecReleases.Capacity = capacity.SpecCount;
            for (int i = 0; i < capacity.SpecCount; i++)
                m_AvailableSpecs.Add(new PortableEffectSpecState(this, capacity));
            m_AvailablePredictions.Capacity = capacity.PredictionRecordCount;
            m_DeferredPredictionReleases.Capacity = capacity.PredictionRecordCount;
            for (int i = 0; i < capacity.PredictionRecordCount; i++)
                m_AvailablePredictions.Add(new PortablePredictionRecord(this, capacity.AttributeWidth, catalog.ContentLayout.MaximumCues));
            RemovedHandles = new ulong[capacity.ActiveEffectWidth];
            ActiveEffects = new GameplayEffectListBank<PortableActiveEffectState>(capacity.Session.SavepointDepth + 1, capacity.ActiveEffectWidth);
            PredictionKeys = new GameplayEffectListBank<ulong>(capacity.Session.SavepointDepth + 1, capacity.Actor.PendingPredictionKeys);
            JournalGroups = new GameplayEffectListBank<PortablePredictionRecord>(
                capacity.Actor.PendingPredictionKeys * capacity.StatePageCount, capacity.Actor.PredictionRecordsPerKey);
            m_AdditionalValues = new GameplayEffectListBank<SimulationSetByCallerValue>(capacity.AdditionalQueueWidth, capacity.ParameterWidth);
            m_AdditionalAttributes = new GameplayEffectListBank<SimulationAttributeCapture>(capacity.AdditionalQueueWidth, capacity.SourceCaptureWidth);
            m_AdditionalLeases.EnsureCapacity(capacity.AdditionalQueueWidth);
            Changes.Capacity = capacity.TentativeChangeWidth;
            Causes.EnsureCapacity(capacity.ActiveObjectCount);
            ActiveIdentities.Capacity = capacity.ActiveEffectWidth;
            SuppliedSourceAttributes.EnsureCapacity(capacity.SourceCaptureWidth);
            ActiveHandles.EnsureCapacity(capacity.ActiveEffectWidth);
            ActiveInstances.EnsureCapacity(capacity.ActiveEffectWidth);
            FrozenOwnedTags.EnsureCapacity(capacity.FrozenTagWidth);
            Target.Prepare(capacity);
        }

        internal int AvailableSpecCount => m_AvailableSpecs.Count;
        internal int AvailableActiveCount => m_AvailableActiveStates.Count;
        internal int AvailablePredictionCount => m_AvailablePredictions.Count;

        internal PortableEffectSpecState AcquireSpec(PortableEffectDefinition definition, SimulationGameplayEffectContext context)
        {
            PortableEffectSpecState spec = AcquireDecodedSpec(definition, context);
            m_CreatedSpecLeases.Add(spec);
            return spec;
        }

        internal PortableEffectSpecState AcquireDecodedSpec(PortableEffectDefinition definition, SimulationGameplayEffectContext context)
        {
            int index = m_AvailableSpecs.Count - 1;
            PortableEffectSpecState spec = m_AvailableSpecs[index];
            m_AvailableSpecs.RemoveAt(index);
            spec.Definition = definition;
            spec.Context = context;
            spec.Retain();
            return spec;
        }

        internal void ReleaseSpec(PortableEffectSpecState spec)
        {
            spec.Clear();
            m_AvailableSpecs.Add(spec);
        }

        internal void DeferSpecRelease(PortableEffectSpecState spec) => m_DeferredSpecReleases.Add(spec);

        internal PortableActiveEffectState AcquireActiveState()
        {
            int index = m_AvailableActiveStates.Count - 1;
            PortableActiveEffectState value = m_AvailableActiveStates[index];
            m_AvailableActiveStates.RemoveAt(index);
            return value;
        }

        internal void RetireActiveState(PortableActiveEffectState value) => m_RetiredActiveStates.Add(value);
        internal void ReturnDecodedActiveState(PortableActiveEffectState value)
        {
            value.Spec.Release();
            value.Spec = null;
            m_AvailableActiveStates.Add(value);
        }
        internal void RetireActiveStates(List<PortableActiveEffectState> values) => m_RetiredActiveStates.AddRange(values);

        internal PortablePredictionRecord AcquirePredictionRecord()
        {
            int index = m_AvailablePredictions.Count - 1;
            PortablePredictionRecord record = m_AvailablePredictions[index];
            m_AvailablePredictions.RemoveAt(index);
            return record;
        }

        internal void CancelPredictionRecord(PortablePredictionRecord record)
        {
            record.Clear();
            m_AvailablePredictions.Add(record);
        }

        internal void DeferPredictionRelease(PortablePredictionRecord record) => m_DeferredPredictionReleases.Add(record);

        internal void AcquireAdditionalApplicationInputs(
            out List<SimulationSetByCallerValue> values,
            out List<SimulationAttributeCapture> attributes)
        {
            values = m_AdditionalValues.Acquire();
            attributes = m_AdditionalAttributes.Acquire();
            m_AdditionalLeases.Add(values, attributes);
        }

        internal void ReleaseAdditionalApplicationInputs(IReadOnlyList<SimulationSetByCallerValue> values)
        {
            List<SimulationAttributeCapture> attributes = m_AdditionalLeases[values];
            m_AdditionalLeases.Remove(values);
            m_AdditionalAttributes.Release(attributes);
            m_AdditionalValues.Release((List<SimulationSetByCallerValue>)values);
        }
        public void Reset()
        {
            Changes.Clear();
            CommandsThisTick = 0;
            SpecApplicationsThisTick = 0;
            LifecycleExecutionsThisTick = 0;
            RemovedHandleCount = 0;
            Causes.Clear();
            for (int i = 0; i < m_RetiredActiveStates.Count; i++)
            {
                PortableActiveEffectState active = m_RetiredActiveStates[i];
                active.Spec.Release();
                active.Spec = null;
                m_AvailableActiveStates.Add(active);
            }
            m_RetiredActiveStates.Clear();
            for (int i = 0; i < m_DeferredPredictionReleases.Count; i++)
                m_DeferredPredictionReleases[i].Release();
            m_DeferredPredictionReleases.Clear();
            for (int i = 0; i < m_DeferredSpecReleases.Count; i++)
                m_DeferredSpecReleases[i].Release();
            m_DeferredSpecReleases.Clear();
            for (int i = 0; i < m_CreatedSpecLeases.Count; i++)
                m_CreatedSpecLeases[i].Release();
            m_CreatedSpecLeases.Clear();
            SuppliedSourceAttributes.Clear();
            ActiveHandles.Clear();
            ActiveInstances.Clear();
            ActiveIdentities.Clear();
            FrozenOwnedTags.Clear();
        }
    }
}
