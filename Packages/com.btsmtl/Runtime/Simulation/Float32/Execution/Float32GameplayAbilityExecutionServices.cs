using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    internal sealed class Float32StateAccessPolicy
    {
        readonly bool[] m_Allowed;

        public Float32StateAccessPolicy(params ProgramStateSemantic[] semantics)
        {
            Array values = Enum.GetValues(typeof(ProgramStateSemantic));
            int maximum = 0;
            for (int i = 0; i < values.Length; i++)
                maximum = Math.Max(maximum, Convert.ToInt32(values.GetValue(i)));
            m_Allowed = new bool[maximum + 1];
            if (semantics == null)
                throw new ArgumentNullException(nameof(semantics));
            for (int i = 0; i < semantics.Length; i++)
                m_Allowed[(int)semantics[i]] = true;
        }

        public bool Allows(ProgramStateSemantic semantic)
        {
            int index = (int)semantic;
            return index >= 0 && index < m_Allowed.Length && m_Allowed[index];
        }
    }

    internal sealed class Float32GameplayAbilityExecutionServices : IGameplayAbilityExecutionServices
    {
        readonly GameplayAbilityExecutionLayout m_Layout;
        readonly string[] m_OperationSourcePaths;
        readonly string[] m_OperationSourceIdentities;
        readonly Float32GameplayAbilityCurve[] m_ExecutionCurves;
        readonly PortableTagQuery[] m_TagQueries;
        readonly SimulationSetByCallerValue[][] m_SetByCallerValues;
        readonly IReadOnlyDictionary<GameplayCueProducerKey, ProgramProducer> m_GameplayCueProducers;
        readonly SimulationBlackboardSlotGroup[] m_BlackboardGroups;
        readonly IReadOnlyList<SimulationBlackboardSlotGroup>[] m_ScopeBlackboardGroups;
        readonly ActionAdmissionProfile[] m_AdmissionProfilesByOperation;
        readonly IReadOnlyDictionary<string, ActionAdmissionProfile> m_AdmissionProfilesById;

        public Float32GameplayAbilityExecutionServices(
            Float32GameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout,
            string[] operationSourcePaths,
            Float32GameplayEffectRuntimeCatalog gameplayEffectCatalog)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (operationSourcePaths == null || operationSourcePaths.Length != data.Operations.Count)
                throw new ArgumentException("Ability execution services SourceMap index is incomplete.", nameof(operationSourcePaths));
            if (!ReferenceEquals(layout.Topology, data.Topology))
                throw new InvalidOperationException("Ability execution layout topology does not match its data.");

            m_Layout = layout;
            m_OperationSourcePaths = operationSourcePaths;
            m_OperationSourceIdentities = BuildOperationSourceIdentities(operationSourcePaths);
            Identity = new GameplayAbilityExecutionIdentity(
                data.AbilityId,
                data.ContentHash,
                data.StateSchemaHash,
                data.OperationSetVersion,
                data.NumericProfile);
            Topology = data.Topology;
            m_ExecutionCurves = BuildExecutionCurves(data);
            m_TagQueries = BuildTagQueries(data, gameplayEffectCatalog);
            m_SetByCallerValues = BuildSetByCallerValues(data);
            GameplayEffectCatalog = gameplayEffectCatalog;
            m_GameplayCueProducers = gameplayEffectCatalog == null
                ? new Dictionary<GameplayCueProducerKey, ProgramProducer>()
                : BuildGameplayCueProducers(data, gameplayEffectCatalog);
            BuildAdmissionProfiles(data, layout, gameplayEffectCatalog, out m_AdmissionProfilesByOperation, out m_AdmissionProfilesById);
            BuildBlackboardGroups(
                data,
                out m_BlackboardGroups,
                out m_ScopeBlackboardGroups);
            ControlPolicy = new Float32StateAccessPolicy(
                ProgramStateSemantic.RunnableLifecycle,
                ProgramStateSemantic.RunnableChildCursor,
                ProgramStateSemantic.RunnableStopBarrier,
                ProgramStateSemantic.RunnableActivationGeneration,
                ProgramStateSemantic.LocomotionMotionElapsedTicks,
                ProgramStateSemantic.TimelinePlayback,
                ProgramStateSemantic.GameplayEffectAppliedHandle,
                ProgramStateSemantic.StateMachineActive,
                ProgramStateSemantic.StateMachinePending,
                ProgramStateSemantic.StateMachineExiting,
                ProgramStateSemantic.StateMachineTransition,
                ProgramStateSemantic.StateMachineExecutionPath);
            BlackboardPolicy = new Float32StateAccessPolicy(
                ProgramStateSemantic.BlackboardValue,
                ProgramStateSemantic.BlackboardOwnerToken,
                ProgramStateSemantic.BlackboardLifetime,
                ProgramStateSemantic.BlackboardWriteStamp);
            Access = new Float32GameplayAbilityExecutionAccess(data, layout, this);
        }

        public Float32GameplayAbilityExecutionAccess Access { get; }
        public GameplayAbilityExecutionIdentity Identity { get; }
        public OperationExecutionTopology Topology { get; }
        public Float32GameplayEffectRuntimeCatalog GameplayEffectCatalog { get; }
        public Float32StateAccessPolicy ControlPolicy { get; }
        public Float32StateAccessPolicy BlackboardPolicy { get; }

        public string SourcePath(OperationHandle operation)
        {
            if (!operation.IsValid || operation.Value >= m_OperationSourcePaths.Length)
                throw new ArgumentOutOfRangeException(nameof(operation));
            return m_OperationSourcePaths[operation.Value];
        }

        public string SkillOperationIdentity(OperationHandle operation)
        {
            if (!operation.IsValid || operation.Value >= m_OperationSourceIdentities.Length)
                throw new ArgumentOutOfRangeException(nameof(operation));
            return m_OperationSourceIdentities[operation.Value];
        }

        static string[] BuildOperationSourceIdentities(string[] sourcePaths)
        {
            var identities = new string[sourcePaths.Length];
            for (int i = 0; i < sourcePaths.Length; i++)
                identities[i] = $"skill-operation:{sourcePaths[i]}";
            return identities;
        }

        public void RequireIdentity(GameplayAbilityExecutionIdentity identity)
        {
            Identity.Require(identity);
        }

        public Float32GameplayAbilityCurve RequireTimelineCurve(ProgramConstant constant, string identity)
        {
            if (constant == null)
                throw new ArgumentNullException(nameof(constant));
            Float32GameplayAbilityCurve curve = m_ExecutionCurves[constant.Index];
            if (curve == null)
                throw new InvalidDataException($"Ability execution curve '{identity}' was not compiled into Ability execution services.");
            return curve;
        }

        public PortableTagQuery RequireTagQuery(OperationHandle operation)
        {
            if (!operation.IsValid || operation.Value >= m_TagQueries.Length)
                throw new ArgumentOutOfRangeException(nameof(operation));
            return m_TagQueries[operation.Value] ??
                throw new InvalidOperationException($"Operation '{SourcePath(operation)}' has no compiled Gameplay Tag query.");
        }

        public SimulationSetByCallerValue[] SetByCallerValues(OperationHandle operation)
        {
            if (!operation.IsValid || operation.Value >= m_SetByCallerValues.Length)
                throw new ArgumentOutOfRangeException(nameof(operation));
            return m_SetByCallerValues[operation.Value];
        }

        public ActionAdmissionProfile RequireAdmissionProfile(OperationHandle operation)
        {
            if (!operation.IsValid || operation.Value >= m_AdmissionProfilesByOperation.Length)
                throw new ArgumentOutOfRangeException(nameof(operation));
            return m_AdmissionProfilesByOperation[operation.Value] ??
                throw new InvalidOperationException($"Operation '{SourcePath(operation)}' has no compiled Action profile.");
        }

        public ActionAdmissionProfile RequireAdmissionProfile(string actionId)
        {
            string identity = SimulationIdentity.Require(actionId, nameof(actionId));
            return m_AdmissionProfilesById.TryGetValue(identity, out ActionAdmissionProfile profile)
                ? profile
                : throw new InvalidOperationException($"Action profile '{identity}' is absent from the Ability catalog.");
        }

        public ProgramProducer RequireGameplayCueProducer(CanonicalTextValue effectId, CanonicalTextValue cueId)
        {
            if (!TryGetGameplayCueProducer(effectId, cueId, out ProgramProducer producer))
                throw new InvalidOperationException($"Gameplay Cue '{effectId}/{cueId}' has no compiled Program producer.");
            return producer;
        }

        public bool TryGetGameplayCueProducer(CanonicalTextValue effectId, CanonicalTextValue cueId, out ProgramProducer producer) =>
            m_GameplayCueProducers.TryGetValue(new GameplayCueProducerKey(effectId, cueId), out producer);

        public SimulationBlackboardSlotGroup BlackboardGroup(int stateSlot) => m_BlackboardGroups[stateSlot];

        public IReadOnlyList<SimulationBlackboardSlotGroup> BlackboardGroups(ProgramScopeLayout scope) =>
            m_ScopeBlackboardGroups[scope.CompiledOwnerIndex];

        public bool TryGetInitialBlackboardOwnerToken(int stateSlot, out BlackboardOwnerToken token)
        {
            SimulationBlackboardSlotGroup group = m_BlackboardGroups[stateSlot];
            if (group.Scope != null && group.OwnerToken == stateSlot &&
                (group.Scope.Kind == ProgramScopeKind.Character ||
                 group.Scope.Kind == ProgramScopeKind.Graph && group.LifetimeKind == ProgramBlackboardLifetime.Config))
            {
                token = new BlackboardOwnerToken(group.Scope.Kind, group.CompiledOwnerIndex, 1);
                return true;
            }
            token = default;
            return false;
        }

        static Float32GameplayAbilityCurve[] BuildExecutionCurves(Float32GameplayAbilityExecutionData data)
        {
            var result = new Float32GameplayAbilityCurve[data.Constants.Count];
            for (int entryIndex = 0; entryIndex < data.CatalogEntries.Count; entryIndex++)
            {
                ProgramCatalogEntry entry = data.CatalogEntries[entryIndex];
                if (entry.Kind != ProgramCatalogEntryKind.TimelineClip &&
                    entry.Kind != ProgramCatalogEntryKind.MotionCurve)
                {
                    continue;
                }
                for (int fieldIndex = 0; fieldIndex < entry.Fields.Count; fieldIndex++)
                {
                    ProgramCatalogField field = entry.Fields[fieldIndex];
                    if (field.Kind != ProgramCatalogFieldKind.Constant)
                        continue;
                    ProgramConstant constant = data.Constants[field.ConstantIndex];
                    if (constant.Kind != ProgramConstantKind.Bytes || result[constant.Index] != null)
                        continue;
                    byte[] bytes = constant.Bytes.ToArray();
                    if (bytes.Length == 0)
                        throw new InvalidDataException($"Timeline curve '{entry.Identity}/{field.Name}' is empty.");
                    result[constant.Index] = Float32GameplayAbilityCurveCodec.Read(bytes);
                }
            }
            for (int i = 0; i < data.Constants.Count; i++)
            {
                ProgramConstant constant = data.Constants[i];
                if (constant.Kind != ProgramConstantKind.Bytes || result[constant.Index] != null ||
                    !OperationNamedConstantSchema.TryParseIdentity(constant.Identity, out OperationNamedConstant field) ||
                    field != OperationNamedConstant.ActionMotionPositionX && field != OperationNamedConstant.ActionMotionPositionZ)
                    continue;
                byte[] bytes = constant.Bytes.ToArray();
                if (bytes.Length == 0)
                    throw new InvalidDataException($"Ability execution curve '{constant.Identity}' is empty.");
                result[constant.Index] = Float32GameplayAbilityCurveCodec.Read(bytes);
            }
            return result;
        }

        static void BuildAdmissionProfiles(
            Float32GameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout,
            Float32GameplayEffectRuntimeCatalog catalog,
            out ActionAdmissionProfile[] byOperation,
            out IReadOnlyDictionary<string, ActionAdmissionProfile> byId)
        {
            var profiles = new Dictionary<string, ActionAdmissionProfile>(StringComparer.Ordinal);
            for (int entryIndex = 0; entryIndex < data.CatalogEntries.Count; entryIndex++)
            {
                ProgramCatalogEntry entry = data.CatalogEntries[entryIndex];
                if (entry.Kind != ProgramCatalogEntryKind.Action)
                    continue;
                ActionAdmissionProfile profile = ActionAdmissionProfileCompiler.Compile(
                    entry,
                    constantIndex => ReadActionTargetRequirement(data, constantIndex),
                    Float32GameplayEffectRuntimeCatalog.NormalizeTag);
                if (catalog != null)
                {
                    for (int tag = 0; tag < profile.GrantedTags.Length; tag++)
                        if (!catalog.ContainsTag(profile.GrantedTags[tag]))
                            throw new InvalidDataException($"Action '{profile.ActionId}' grants unregistered Tag '{profile.GrantedTags[tag]}'.");
                    if (profile.GrantedTags.Length > catalog.ActorCapacity.TagsPerExternalSource)
                        throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                            GameplayEffectPreparationFailureKind.ContentRequirementExceedsCapacity,
                            GameplayEffectCapacityDimension.TagsPerExternalSource, profile.ActionId,
                            profile.GrantedTags.Length, catalog.ActorCapacity.TagsPerExternalSource));
                }
                if (!profiles.TryAdd(profile.ActionId, profile))
                    throw new InvalidDataException($"Action profile '{profile.ActionId}' is duplicated.");
                if (layout.ActionCapacity(profile.ActionId) != profile.MaxConcurrentInstances)
                    throw new InvalidDataException($"Action profile '{profile.ActionId}' declares capacity '{profile.MaxConcurrentInstances}', but Program layout provides '{layout.ActionCapacity(profile.ActionId)}' capacity.");
            }
            byOperation = new ActionAdmissionProfile[data.Operations.Count];
            for (int operationIndex = 0; operationIndex < byOperation.Length; operationIndex++)
            {
                ProgramCatalogEntry entry = layout.FindCatalog(new OperationHandle(operationIndex), ProgramCatalogEntryKind.Action);
                if (entry == null)
                    continue;
                byOperation[operationIndex] = profiles[entry.Identity.Substring("action:".Length)];
            }
            byId = profiles;
        }

        static int ReadActionTargetRequirement(Float32GameplayAbilityExecutionData data, int constantIndex)
        {
            if (constantIndex < 0 || constantIndex >= data.Constants.Count)
                throw new InvalidDataException($"Action target requirement constant '{constantIndex}' is out of range.");
            ProgramConstant constant = data.Constants[constantIndex];
            if (constant.Kind != ProgramConstantKind.Int32)
                throw new InvalidDataException($"Action target requirement constant '{constant.Identity}' has kind '{constant.Kind}'.");
            return constant.Int32;
        }

        internal static bool RequiresTagCatalog(Float32GameplayAbilityExecutionData data)
        {
            if (data.Capabilities.HasGameplayCapability("GameplayEffect") ||
                data.Binding.ActivationRules.Count > 0 || data.Binding.Tags.Count > 0)
                return true;
            for (int index = 0; index < data.Operations.Count; index++)
            {
                SimulationOperationCode code = data.Operations[index].Code;
                if (code == SimulationOperationCode.AbilityActive ||
                    code == SimulationOperationCode.SelectedSourceWindowActive)
                    return true;
            }
            for (int index = 0; index < data.Binding.ActivationRules.Count; index++)
                if (data.Binding.ActivationRules[index].SourceQuery.IsValid)
                    return true;
            return false;
        }

        static PortableTagQuery[] BuildTagQueries(Float32GameplayAbilityExecutionData data, Float32GameplayEffectRuntimeCatalog catalog)
        {
            var result = new PortableTagQuery[data.Operations.Count];
            for (int operationIndex = 0; operationIndex < data.Operations.Count; operationIndex++)
            {
                SimulationOperation operation = data.Operations[operationIndex];
                if (operation.Code != SimulationOperationCode.GameplayEffectMatchTags &&
                    operation.Code != SimulationOperationCode.GameplayEffectRemove &&
                    operation.Code != SimulationOperationCode.AbilityActive &&
                    operation.Code != SimulationOperationCode.SelectedSourceWindowActive)
                    continue;
                var all = new List<string>();
                var any = new List<string>();
                var none = new List<string>();
                for (int i = 0; i < operation.ConstantReferences.Count; i++)
                {
                    ProgramConstant constant = data.Constants[operation.ConstantReferences[i]];
                    if (!OperationNamedConstantSchema.TryGetDynamicField(constant.Identity, out string field))
                        continue;
                    if (!field.StartsWith("Query:", StringComparison.Ordinal))
                        continue;
                    if (constant.Kind != ProgramConstantKind.String)
                        throw new InvalidDataException($"Gameplay Tag query field '{field}' is not String.");
                    if (!field.StartsWith("Query:All:", StringComparison.Ordinal) &&
                        !field.StartsWith("Query:Any:", StringComparison.Ordinal) &&
                        !field.StartsWith("Query:None:", StringComparison.Ordinal))
                        continue;
                    if (!catalog.TryResolveTagIdentity(constant.Text, out string tagId))
                        throw new InvalidDataException($"Gameplay Tag query field '{field}' is absent from the prepared Tag Catalog.");
                    if (field.StartsWith("Query:All:", StringComparison.Ordinal))
                        all.Add(tagId);
                    else if (field.StartsWith("Query:Any:", StringComparison.Ordinal))
                        any.Add(tagId);
                    else if (field.StartsWith("Query:None:", StringComparison.Ordinal))
                        none.Add(tagId);
                }
                result[operationIndex] = new PortableTagQuery(all, any, none);
            }
            return result;
        }

        static SimulationSetByCallerValue[][] BuildSetByCallerValues(Float32GameplayAbilityExecutionData data)
        {
            var result = new SimulationSetByCallerValue[data.Operations.Count][];
            for (int operationIndex = 0; operationIndex < data.Operations.Count; operationIndex++)
            {
                SimulationOperation operation = data.Operations[operationIndex];
                if (operation.Code != SimulationOperationCode.GameplayEffectApply)
                {
                    result[operationIndex] = Array.Empty<SimulationSetByCallerValue>();
                    continue;
                }
                var values = new List<SimulationSetByCallerValue>();
                for (int i = 0; i < operation.ConstantReferences.Count; i++)
                {
                    ProgramConstant constant = data.Constants[operation.ConstantReferences[i]];
                    if (!OperationNamedConstantSchema.TryGetDynamicField(constant.Identity, out string field) ||
                        !field.StartsWith("SetByCaller:", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (constant.Kind != ProgramConstantKind.Scalar)
                        throw new InvalidDataException($"Gameplay Effect SetByCaller field '{field}' is not Scalar.");
                    string parameterId = field.Substring("SetByCaller:".Length);
                    values.Add(new SimulationSetByCallerValue(parameterId, constant.Scalar));
                }
                values.Sort((left, right) => CanonicalTextValue.CompareOrdinal(left.ParameterId, right.ParameterId));
                for (int i = 1; i < values.Count; i++)
                {
                    if (CanonicalTextValue.Equals(values[i - 1].ParameterId, values[i].ParameterId, StringComparison.Ordinal))
                        throw new InvalidDataException($"Gameplay Effect operation '{operation.Handle}' contains duplicate SetByCaller parameter '{values[i].ParameterId}'.");
                }
                result[operationIndex] = values.ToArray();
            }
            return result;
        }

        static IReadOnlyDictionary<GameplayCueProducerKey, ProgramProducer> BuildGameplayCueProducers(
            Float32GameplayAbilityExecutionData data,
            Float32GameplayEffectRuntimeCatalog gameplayEffects)
        {
            var result = new Dictionary<GameplayCueProducerKey, ProgramProducer>();
            for (int i = 0; i < data.Producers.Count; i++)
            {
                ProgramProducer producer = data.Producers[i];
                if (producer.ChannelKind != ProgramOutputChannelKind.Presentation ||
                    !gameplayEffects.Effects.ContainsKey(producer.SourceIdentity))
                {
                    continue;
                }
                int separator = producer.Identity.LastIndexOf(':');
                if (separator < 0 || separator == producer.Identity.Length - 1)
                    throw new InvalidDataException($"Gameplay Effect producer '{producer.Identity}' has no Cue identity suffix.");
                var key = new GameplayCueProducerKey(
                    producer.SourceIdentity,
                    producer.Identity.Substring(separator + 1));
                if (result.ContainsKey(key))
                    throw new InvalidDataException($"Gameplay Cue '{key.EffectId}/{key.CueId}' has multiple Program producers.");
                result.Add(key, producer);
            }
            return result;
        }

        readonly struct GameplayCueProducerKey : IEquatable<GameplayCueProducerKey>
        {
            public GameplayCueProducerKey(CanonicalTextValue effectId, CanonicalTextValue cueId)
            {
                EffectId = effectId;
                CueId = cueId;
            }

            public CanonicalTextValue EffectId { get; }
            public CanonicalTextValue CueId { get; }

            public bool Equals(GameplayCueProducerKey other) =>
                CanonicalTextValue.Equals(EffectId, other.EffectId, StringComparison.Ordinal) &&
                CanonicalTextValue.Equals(CueId, other.CueId, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is GameplayCueProducerKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (EffectId.GetHashCode() * 397) ^
                        CueId.GetHashCode();
                }
            }
        }

        static void BuildBlackboardGroups(
            Float32GameplayAbilityExecutionData data,
            out SimulationBlackboardSlotGroup[] groups,
            out IReadOnlyList<SimulationBlackboardSlotGroup>[] scopeGroups)
        {
            ProgramCatalogEntry[] declarations = data.CatalogIndex.BindBlackboardDeclarations(data.StateSlots, data.References);
            var byOwner = new Dictionary<string, SimulationBlackboardSlotGroup>(StringComparer.Ordinal);
            var byScope = new IReadOnlyList<SimulationBlackboardSlotGroup>[data.Scopes.Count];
            var bySlot = new SimulationBlackboardSlotGroup[data.StateSlots.Count];
            for (int scopeIndex = 0; scopeIndex < data.Scopes.Count; scopeIndex++)
            {
                ProgramScopeLayout scope = data.Scopes[scopeIndex];
                if (scope.CompiledOwnerIndex != scopeIndex)
                    throw new InvalidOperationException($"Program scope '{scope.Identity}' compiled owner index is not canonical.");
                var owners = new HashSet<string>(StringComparer.Ordinal);
                var values = new List<SimulationBlackboardSlotGroup>();
                for (int slotIndex = 0; slotIndex < scope.StateSlots.Count; slotIndex++)
                {
                    ProgramStateSlot slot = data.StateSlots[scope.StateSlots[slotIndex]];
                    if (slot.Semantic != ProgramStateSemantic.BlackboardValue || !owners.Add(slot.OwnerIdentity))
                        continue;
                    SimulationBlackboardSlotGroup group = BuildBlackboardGroup(
                        data,
                        scope,
                        scope.CompiledOwnerIndex,
                        slot.OwnerIdentity,
                        declarations[slot.Index]);
                    if (byOwner.ContainsKey(slot.OwnerIdentity))
                        throw new InvalidOperationException($"Blackboard state group '{slot.OwnerIdentity}' belongs to multiple scopes.");
                    byOwner.Add(slot.OwnerIdentity, group);
                    bySlot[group.Value] = group;
                    bySlot[group.OwnerToken] = group;
                    bySlot[group.Lifetime] = group;
                    bySlot[group.WriteStamp] = group;
                    values.Add(group);
                }
                byScope[scopeIndex] = values.Count == 0
                    ? Array.Empty<SimulationBlackboardSlotGroup>()
                    : Array.AsReadOnly(values.ToArray());
            }
            for (int i = 0; i < data.StateSlots.Count; i++)
            {
                ProgramStateSlot slot = data.StateSlots[i];
                if (slot.Semantic == ProgramStateSemantic.BlackboardValue && !byOwner.ContainsKey(slot.OwnerIdentity))
                    throw new InvalidOperationException($"Blackboard state group '{slot.OwnerIdentity}' has no compiled scope owner.");
            }
            groups = bySlot;
            scopeGroups = byScope;
        }

        static SimulationBlackboardSlotGroup BuildBlackboardGroup(
            Float32GameplayAbilityExecutionData data,
            ProgramScopeLayout scope,
            int compiledOwnerIndex,
            string ownerIdentity,
            ProgramCatalogEntry declaration)
        {
            int value = -1;
            int ownerToken = -1;
            int lifetime = -1;
            int writeStamp = -1;
            for (int i = 0; i < scope.StateSlots.Count; i++)
            {
                int stateSlot = scope.StateSlots[i];
                ProgramStateSlot slot = data.StateSlots[stateSlot];
                if (!CanonicalTextValue.Equals(slot.OwnerIdentity, ownerIdentity, StringComparison.Ordinal))
                    continue;
                switch (slot.Semantic)
                {
                    case ProgramStateSemantic.BlackboardValue: value = UniqueSlot(value, stateSlot, ownerIdentity); break;
                    case ProgramStateSemantic.BlackboardOwnerToken: ownerToken = UniqueSlot(ownerToken, stateSlot, ownerIdentity); break;
                    case ProgramStateSemantic.BlackboardLifetime: lifetime = UniqueSlot(lifetime, stateSlot, ownerIdentity); break;
                    case ProgramStateSemantic.BlackboardWriteStamp: writeStamp = UniqueSlot(writeStamp, stateSlot, ownerIdentity); break;
                }
            }
            if (value < 0 || ownerToken < 0 || lifetime < 0 || writeStamp < 0)
                throw new InvalidOperationException($"Blackboard state group '{ownerIdentity}' is incomplete.");
            ProgramStateSlot lifetimeSlot = data.StateSlots[lifetime];
            if (lifetimeSlot.DefaultConstantIndex < 0)
                throw new InvalidOperationException($"Blackboard state group '{ownerIdentity}' has no lifetime constant.");
            ProgramConstant lifetimeConstant = data.Constants[lifetimeSlot.DefaultConstantIndex];
            if (lifetimeConstant.Kind != ProgramConstantKind.Int32 ||
                lifetimeConstant.Int32 < byte.MinValue || lifetimeConstant.Int32 > byte.MaxValue ||
                !Enum.IsDefined(typeof(ProgramBlackboardLifetime), (byte)lifetimeConstant.Int32))
            {
                throw new InvalidOperationException($"Blackboard state group '{ownerIdentity}' lifetime is invalid.");
            }
            return new SimulationBlackboardSlotGroup(
                value,
                ownerToken,
                lifetime,
                writeStamp,
                scope,
                compiledOwnerIndex,
                (ProgramBlackboardLifetime)(byte)lifetimeConstant.Int32,
                declaration);
        }

        static int UniqueSlot(int current, int incoming, string ownerIdentity)
        {
            if (current >= 0)
                throw new InvalidOperationException($"Blackboard state group '{ownerIdentity}' contains duplicate semantics.");
            return incoming;
        }
    }

}
