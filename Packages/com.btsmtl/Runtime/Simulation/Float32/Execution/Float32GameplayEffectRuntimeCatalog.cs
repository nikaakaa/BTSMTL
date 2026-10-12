using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThirdPersonSimulation
{
    internal enum PortableEffectDurationPolicy : byte
    {
        Instant = 0,
        Duration = 1,
        Infinite = 2
    }

    internal enum PortableMagnitudeSource : byte
    {
        Constant = 0,
        SetByCaller = 1,
        SourceAttributeSnapshot = 2,
        TargetAttributeSnapshot = 3,
        TargetAttributeLive = 4
    }

    internal enum PortableEffectStackingPolicy : byte
    {
        Independent = 0,
        AggregateBySource = 1,
        AggregateByTarget = 2
    }

    internal enum PortableEffectDurationUpdatePolicy : byte
    {
        Keep = 0,
        Refresh = 1,
        Extend = 2
    }

    internal enum PortableEffectPeriodUpdatePolicy : byte
    {
        Keep = 0,
        Reset = 1
    }

    internal enum PortableEffectOverflowPolicy : byte
    {
        Reject = 0,
        ReplaceOldest = 1,
        ApplyOverflowEffects = 2
    }

    internal enum PortableModifierApplication : byte
    {
        BaseValue = 0,
        CurrentValue = 1
    }

    internal enum PortableModifierOperation : byte
    {
        Additive = 0,
        Multiplicative = 1,
        Override = 2,
        Clamp = 3
    }

    internal enum PortableClampBound : byte
    {
        Minimum = 0,
        Maximum = 1
    }

    internal enum PortableRequirementPhase : byte
    {
        Application = 0,
        Ongoing = 1,
        Removal = 2
    }

    internal enum PortableAttributeSource : byte
    {
        SourceSnapshot = 0,
        Target = 1
    }

    internal enum PortableAttributeComparison : byte
    {
        Less = 0,
        LessOrEqual = 1,
        Equal = 2,
        GreaterOrEqual = 3,
        Greater = 4,
        NotEqual = 5
    }

    internal enum PortableAdditionalEffectTrigger : byte
    {
        Applied = 0,
        Period = 1,
        Removed = 2,
        Overflow = 3
    }

    internal enum PortableAdditionalParameterSource : byte
    {
        ParentSetByCaller = 0,
        Constant = 1
    }

    internal enum PortableAttackCollisionKind : byte
    {
        Box = 0,
        BoxContinuous = 1,
        FanWithHeight = 2,
        GeometrySweep = 3
    }

    internal enum PortableCueTrigger : byte
    {
        OnActive = 0,
        Executed = 1,
        WhileActive = 2,
        Removed = 3,
        Expired = 4
    }

    internal readonly struct PortableMagnitude
    {
        public PortableMagnitude(
            PortableMagnitudeSource source,
            Float32Scalar constant,
            string setByCallerParameterId,
            string attributeId,
            Float32Scalar coefficient,
            Float32Scalar postAdd)
        {
            Source = source;
            Constant = constant;
            SetByCallerParameterId = setByCallerParameterId ?? string.Empty;
            AttributeId = attributeId ?? string.Empty;
            Coefficient = coefficient;
            PostAdd = postAdd;
        }

        public PortableMagnitudeSource Source { get; }
        public Float32Scalar Constant { get; }
        public string SetByCallerParameterId { get; }
        public string AttributeId { get; }
        public Float32Scalar Coefficient { get; }
        public Float32Scalar PostAdd { get; }
    }

    internal sealed class PortableTagQuery
    {
        public PortableTagQuery(IEnumerable<string> all, IEnumerable<string> any, IEnumerable<string> none)
        {
            All = Canonical(all);
            Any = Canonical(any);
            None = Canonical(none);
        }

        public string[] All { get; }
        public string[] Any { get; }
        public string[] None { get; }
        public bool IsEmpty => All.Length == 0 && Any.Length == 0 && None.Length == 0;

        static string[] Canonical(IEnumerable<string> values)
        {
            var result = values == null
                ? new List<string>()
                : values.Select(Float32GameplayEffectRuntimeCatalog.NormalizeTag).ToList();
            result.Sort(StringComparer.Ordinal);
            for (int i = 0; i < result.Count; i++)
            {
                if (i > 0 && string.Equals(result[i - 1], result[i], StringComparison.Ordinal))
                    throw new InvalidDataException($"Gameplay Tag query contains duplicate '{result[i]}'.");
            }
            return result.ToArray();
        }
    }

    internal readonly struct PortableAttributeBound
    {
        public PortableAttributeBound(bool enabled, bool fromAttribute, Float32Scalar constant, string attributeId)
        {
            Enabled = enabled;
            FromAttribute = fromAttribute;
            Constant = constant;
            AttributeId = attributeId ?? string.Empty;
        }

        public bool Enabled { get; }
        public bool FromAttribute { get; }
        public Float32Scalar Constant { get; }
        public string AttributeId { get; }
    }

    [CanonicalPublishedReference]
    internal sealed class PortableAttributeDefinition
    {
        public PortableAttributeDefinition(string id, Float32Scalar initialBase, PortableAttributeBound minimum, PortableAttributeBound maximum)
        {
            Id = id;
            InitialBase = initialBase;
            Minimum = minimum;
            Maximum = maximum;
        }

        public string Id { get; }
        public Float32Scalar InitialBase { get; }
        public PortableAttributeBound Minimum { get; }
        public PortableAttributeBound Maximum { get; }
        [CanonicalReferenceKey]
        public int Index { get; internal set; }
        public int MinimumAttributeIndex { get; internal set; }
        public int MaximumAttributeIndex { get; internal set; }
    }

    internal abstract class PortableEffectComponent
    {
    }

    internal sealed class PortableModifierComponent : PortableEffectComponent
    {
        public PortableModifierComponent(string attributeId, PortableModifierApplication application, PortableModifierOperation operation, PortableMagnitude magnitude, int priority, PortableClampBound clampBound, bool scaleWithStack)
        {
            AttributeId = Float32GameplayEffectRuntimeCatalog.NormalizeAttribute(attributeId);
            Application = application;
            Operation = operation;
            Magnitude = magnitude;
            Priority = priority;
            ClampBound = clampBound;
            ScaleWithStack = scaleWithStack;
        }

        public string AttributeId { get; }
        public PortableModifierApplication Application { get; }
        public PortableModifierOperation Operation { get; }
        public PortableMagnitude Magnitude { get; }
        public int Priority { get; }
        public PortableClampBound ClampBound { get; }
        public bool ScaleWithStack { get; }
    }

    internal sealed class PortableGrantedTagsComponent : PortableEffectComponent
    {
        public PortableGrantedTagsComponent(string[] tags)
        {
            Tags = tags;
        }

        public string[] Tags { get; }
    }

    internal sealed class PortableBlockAbilitiesComponent : PortableEffectComponent
    {
        public PortableBlockAbilitiesComponent(PortableTagQuery query)
        {
            Query = query;
        }

        public PortableTagQuery Query { get; }
    }

    internal sealed class PortableReplacementSourceRequirementComponent : PortableEffectComponent
    {
        public PortableReplacementSourceRequirementComponent(PortableTagQuery candidateQuery, PortableTagQuery selectedSourceQuery)
        {
            CandidateQuery = candidateQuery;
            SelectedSourceQuery = selectedSourceQuery;
        }

        public PortableTagQuery CandidateQuery { get; }
        public PortableTagQuery SelectedSourceQuery { get; }
    }

    internal sealed class PortableTagRequirementsComponent : PortableEffectComponent
    {
        public PortableTagRequirementsComponent(PortableRequirementPhase phase, PortableTagQuery source, PortableTagQuery target)
        {
            Phase = phase;
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public PortableRequirementPhase Phase { get; }
        public PortableTagQuery Source { get; }
        public PortableTagQuery Target { get; }
    }

    internal sealed class PortableAttributeRequirementsComponent : PortableEffectComponent
    {
        public PortableAttributeRequirementsComponent(PortableRequirementPhase phase, PortableAttributeSource source, string attributeId, PortableAttributeComparison comparison, PortableMagnitude threshold)
        {
            Phase = phase;
            Source = source;
            AttributeId = Float32GameplayEffectRuntimeCatalog.NormalizeAttribute(attributeId);
            Comparison = comparison;
            Threshold = threshold;
        }

        public PortableRequirementPhase Phase { get; }
        public PortableAttributeSource Source { get; }
        public string AttributeId { get; }
        public PortableAttributeComparison Comparison { get; }
        public PortableMagnitude Threshold { get; }
    }

    internal readonly struct PortableExecutionMutation
    {
        public PortableExecutionMutation(string attributeId, PortableModifierOperation operation, PortableMagnitude magnitude, PortableClampBound clampBound)
        {
            AttributeId = Float32GameplayEffectRuntimeCatalog.NormalizeAttribute(attributeId);
            Operation = operation;
            Magnitude = magnitude;
            ClampBound = clampBound;
        }

        public string AttributeId { get; }
        public PortableModifierOperation Operation { get; }
        public PortableMagnitude Magnitude { get; }
        public PortableClampBound ClampBound { get; }
    }

    internal sealed class PortableExecutionComponent : PortableEffectComponent
    {
        public PortableExecutionComponent(PortableExecutionMutation[] mutations)
        {
            Mutations = mutations ?? Array.Empty<PortableExecutionMutation>();
        }

        public PortableExecutionMutation[] Mutations { get; }
    }

    internal readonly struct PortableAdditionalParameterBinding
    {
        public PortableAdditionalParameterBinding(string childParameterId, PortableAdditionalParameterSource source, string parentParameterId, Float32Scalar constant)
        {
            ChildParameterId = SimulationIdentity.Require(childParameterId, nameof(childParameterId));
            Source = source;
            ParentParameterId = parentParameterId ?? string.Empty;
            Constant = constant;
        }

        public string ChildParameterId { get; }
        public PortableAdditionalParameterSource Source { get; }
        public string ParentParameterId { get; }
        public Float32Scalar Constant { get; }
    }

    internal readonly struct PortableAdditionalEffect
    {
        public PortableAdditionalEffect(PortableAdditionalEffectTrigger trigger, string effectId, PortableAdditionalParameterBinding[] bindings)
        {
            Trigger = trigger;
            EffectId = Float32GameplayEffectRuntimeCatalog.NormalizeEffect(effectId);
            Bindings = bindings ?? Array.Empty<PortableAdditionalParameterBinding>();
            Array.Sort(Bindings, (left, right) => string.CompareOrdinal(left.ChildParameterId, right.ChildParameterId));
        }

        public PortableAdditionalEffectTrigger Trigger { get; }
        public string EffectId { get; }
        public PortableAdditionalParameterBinding[] Bindings { get; }
    }

    internal sealed class PortableAdditionalEffectsComponent : PortableEffectComponent
    {
        public PortableAdditionalEffectsComponent(PortableAdditionalEffect[] effects)
        {
            Effects = effects ?? Array.Empty<PortableAdditionalEffect>();
        }

        public PortableAdditionalEffect[] Effects { get; }
    }

    internal sealed class PortableCueComponent : PortableEffectComponent
    {
        public PortableCueComponent(string cueId, PortableCueTrigger trigger)
        {
            CueId = SimulationIdentity.Require(cueId, nameof(cueId));
            Trigger = trigger;
        }

        public string CueId { get; }
        public PortableCueTrigger Trigger { get; }
    }

    internal sealed class PortableAttackCameraShakeComponent : PortableEffectComponent
    {
        public PortableAttackCameraShakeComponent(string resourceId, bool shakeOnNotHit)
        {
            ResourceId = resourceId;
            ShakeOnNotHit = shakeOnNotHit;
        }

        public string ResourceId { get; }
        public bool ShakeOnNotHit { get; }
    }

    internal sealed class PortableAttackCollisionComponent : PortableEffectComponent
    {
        public PortableAttackCollisionComponent(
            PortableAttackCollisionKind kind,
            Float32Vector3 centerOffset,
            Float32Scalar width,
            Float32Scalar height,
            Float32Scalar depth,
            Float32Scalar fanAngle,
            Float32Scalar radius,
            Float32Scalar invalidRadius,
            Float32Scalar invalidAngle,
            int followDirectionType,
            int coreDistance,
            bool isSubtractive,
            Float32Scalar hitInterval,
            int aliveMaxHitCount,
            int unitMaxHitCount,
            bool followAttacker)
        {
            Kind = kind;
            CenterOffset = centerOffset;
            Width = width;
            Height = height;
            Depth = depth;
            FanAngle = fanAngle;
            Radius = radius;
            InvalidRadius = invalidRadius;
            InvalidAngle = invalidAngle;
            FollowDirectionType = followDirectionType;
            CoreDistance = coreDistance;
            IsSubtractive = isSubtractive;
            HitInterval = hitInterval;
            AliveMaxHitCount = aliveMaxHitCount;
            UnitMaxHitCount = unitMaxHitCount;
            FollowAttacker = followAttacker;
        }

        public PortableAttackCollisionKind Kind { get; }
        public Float32Vector3 CenterOffset { get; }
        public Float32Scalar Width { get; }
        public Float32Scalar Height { get; }
        public Float32Scalar Depth { get; }
        public Float32Scalar FanAngle { get; }
        public Float32Scalar Radius { get; }
        public Float32Scalar InvalidRadius { get; }
        public Float32Scalar InvalidAngle { get; }
        public int FollowDirectionType { get; }
        public int CoreDistance { get; }
        public bool IsSubtractive { get; }
        public Float32Scalar HitInterval { get; }
        public int AliveMaxHitCount { get; }
        public int UnitMaxHitCount { get; }
        public bool FollowAttacker { get; }
        public AttackSweepGeometryData<Float32Scalar> Geometry { get; internal set; }
    }

    internal sealed class PortableAttackPropertyComponent : PortableEffectComponent
    {
        public PortableAttackPropertyComponent(
            string sourceKey,
            int hitType,
            int hitStrengthType,
            string[] combatTags,
            PortableMagnitude damagePercentage,
            PortableMagnitude addedDamage,
            PortableMagnitude breakStunRatio,
            PortableMagnitude elementAccumulation,
            PortableMagnitude exhaustedAccumulation,
            PortableMagnitude exhaustedChase,
            int damageElement,
            int damageHitType,
            int damageBreakLevel,
            PortableMagnitude damageBreakLevelProbability,
            int triggerBuffLevel,
            int destructionClass,
            int destructionDurability,
            int overrideDamageStaggerLevel,
            int damageTextId,
            int damageTextWaitMilliseconds,
            int frameHalt,
            int attackerFrameHalt,
            uint groundHitEffectId,
            uint skyHitEffectId,
            uint downHitEffectId,
            string standardConfigKey,
            string abilityTargetKey,
            bool isCauseStun,
            bool isHeavyAttack,
            bool isCauseExhausted,
            bool isHeal,
            bool isIndirect,
            bool hitsEnemy,
            bool hitsAllied,
            bool hitsNeutral,
            bool useAbilityTargetKey,
            bool banDamage)
        {
            SourceKey = SimulationIdentity.Require(sourceKey, nameof(sourceKey));
            HitType = hitType;
            HitStrengthType = hitStrengthType;
            CombatTags = combatTags ?? Array.Empty<string>();
            DamagePercentage = damagePercentage;
            AddedDamage = addedDamage;
            BreakStunRatio = breakStunRatio;
            ElementAccumulation = elementAccumulation;
            ExhaustedAccumulation = exhaustedAccumulation;
            ExhaustedChase = exhaustedChase;
            DamageElement = damageElement;
            DamageHitType = damageHitType;
            DamageBreakLevel = damageBreakLevel;
            DamageBreakLevelProbability = damageBreakLevelProbability;
            TriggerBuffLevel = triggerBuffLevel;
            DestructionClass = destructionClass;
            DestructionDurability = destructionDurability;
            OverrideDamageStaggerLevel = overrideDamageStaggerLevel;
            DamageTextId = damageTextId;
            DamageTextWaitMilliseconds = damageTextWaitMilliseconds;
            FrameHalt = frameHalt;
            AttackerFrameHalt = attackerFrameHalt;
            GroundHitEffectId = groundHitEffectId;
            SkyHitEffectId = skyHitEffectId;
            DownHitEffectId = downHitEffectId;
            StandardConfigKey = standardConfigKey ?? string.Empty;
            AbilityTargetKey = abilityTargetKey ?? string.Empty;
            IsCauseStun = isCauseStun;
            IsHeavyAttack = isHeavyAttack;
            IsCauseExhausted = isCauseExhausted;
            IsHeal = isHeal;
            IsIndirect = isIndirect;
            HitsEnemy = hitsEnemy;
            HitsAllied = hitsAllied;
            HitsNeutral = hitsNeutral;
            UseAbilityTargetKey = useAbilityTargetKey;
            BanDamage = banDamage;
        }

        public string SourceKey { get; }
        public int HitType { get; }
        public int HitStrengthType { get; }
        public string[] CombatTags { get; }
        public PortableMagnitude DamagePercentage { get; }
        public PortableMagnitude AddedDamage { get; }
        public PortableMagnitude BreakStunRatio { get; }
        public PortableMagnitude ElementAccumulation { get; }
        public PortableMagnitude ExhaustedAccumulation { get; }
        public PortableMagnitude ExhaustedChase { get; }
        public int DamageElement { get; }
        public int DamageHitType { get; }
        public int DamageBreakLevel { get; }
        public PortableMagnitude DamageBreakLevelProbability { get; }
        public int TriggerBuffLevel { get; }
        public int DestructionClass { get; }
        public int DestructionDurability { get; }
        public int OverrideDamageStaggerLevel { get; }
        public int DamageTextId { get; }
        public int DamageTextWaitMilliseconds { get; }
        public int FrameHalt { get; }
        public int AttackerFrameHalt { get; }
        public uint GroundHitEffectId { get; }
        public uint SkyHitEffectId { get; }
        public uint DownHitEffectId { get; }
        public string StandardConfigKey { get; }
        public string AbilityTargetKey { get; }
        public bool IsCauseStun { get; }
        public bool IsHeavyAttack { get; }
        public bool IsCauseExhausted { get; }
        public bool IsHeal { get; }
        public bool IsIndirect { get; }
        public bool HitsEnemy { get; }
        public bool HitsAllied { get; }
        public bool HitsNeutral { get; }
        public bool UseAbilityTargetKey { get; }
        public bool BanDamage { get; }
    }

    internal sealed class PortableEffectDefinition
    {
        public PortableEffectDefinition(
            string id,
            uint revision,
            string[] effectTags,
            PortableEffectDurationPolicy durationPolicy,
            PortableMagnitude durationMagnitude,
            bool hasPeriod,
            PortableMagnitude periodMagnitude,
            bool executeOnApplication,
            PortableEffectStackingPolicy stackingPolicy,
            int maxStacks,
            PortableEffectDurationUpdatePolicy durationUpdate,
            PortableEffectPeriodUpdatePolicy periodUpdate,
            PortableEffectOverflowPolicy overflowPolicy,
            string[] setByCallerParameters,
            GameplayEffectScopeKind scope,
            GameplayEffectLifetimeKind lifetime,
            PortableEffectComponent[] components)
        {
            Id = id;
            Revision = revision;
            EffectTags = effectTags ?? Array.Empty<string>();
            DurationPolicy = durationPolicy;
            DurationMagnitude = durationMagnitude;
            HasPeriod = hasPeriod;
            PeriodMagnitude = periodMagnitude;
            ExecuteOnApplication = executeOnApplication;
            StackingPolicy = stackingPolicy;
            MaxStacks = maxStacks;
            DurationUpdate = durationUpdate;
            PeriodUpdate = periodUpdate;
            OverflowPolicy = overflowPolicy;
            SetByCallerParameters = setByCallerParameters ?? Array.Empty<string>();
            Scope = scope;
            Lifetime = lifetime;
            Components = components ?? Array.Empty<PortableEffectComponent>();
            GrantedTags = CollectGrantedTags();
            CueIds = Components.OfType<PortableCueComponent>().Select(cue => cue.CueId)
                .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }

        public string Id { get; }
        public uint Revision { get; }
        public string[] EffectTags { get; }
        public PortableEffectDurationPolicy DurationPolicy { get; }
        public PortableMagnitude DurationMagnitude { get; }
        public bool HasPeriod { get; }
        public PortableMagnitude PeriodMagnitude { get; }
        public bool ExecuteOnApplication { get; }
        public PortableEffectStackingPolicy StackingPolicy { get; }
        public int MaxStacks { get; }
        public PortableEffectDurationUpdatePolicy DurationUpdate { get; }
        public PortableEffectPeriodUpdatePolicy PeriodUpdate { get; }
        public PortableEffectOverflowPolicy OverflowPolicy { get; }
        public string[] SetByCallerParameters { get; }
        public GameplayEffectScopeKind Scope { get; }
        public GameplayEffectLifetimeKind Lifetime { get; }
        public PortableEffectComponent[] Components { get; }
        public IReadOnlyList<string> GrantedTags { get; }
        public string[] CueIds { get; }
        public int Index { get; internal set; }
        public GameplayEffectDefinitionLayout Layout { get; internal set; }
        public IReadOnlyList<string> SourceSnapshotAttributes => Layout.SourceAttributes;
        public IReadOnlyList<string> TargetSnapshotAttributes => Layout.TargetAttributes;

        string[] CollectGrantedTags()
        {
            var tags = new SortedSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Components.Length; i++)
            {
                if (Components[i] is not PortableGrantedTagsComponent granted)
                    continue;
                for (int tagIndex = 0; tagIndex < granted.Tags.Length; tagIndex++)
                    tags.Add(granted.Tags[tagIndex]);
            }
            return tags.ToArray();
        }

    }

    internal sealed class Float32GameplayEffectRuntimeCatalog
    {
        readonly CharacterGameplayEffectRuntimeBinding m_Binding;
        readonly Dictionary<string, string> m_TagParents = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<string> m_InitialTags = new List<string>();
        readonly Dictionary<string, PortableAttributeDefinition> m_Attributes = new Dictionary<string, PortableAttributeDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, PortableEffectDefinition> m_Effects = new Dictionary<string, PortableEffectDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, int> m_TagIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly string[] m_TagIds;
        readonly PortableEffectDefinition[] m_EffectsByIndex;

        public Float32GameplayEffectRuntimeCatalog(CharacterGameplayEffectRuntimeBinding binding)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            m_Binding = binding;
            ContentLayout = binding.ContentLayout;
            ReadBinding(binding.CatalogBytes);
            ValidateClosure();
            AttributeDependencies = binding.PrepareAttributeDependencies(BuildAttributeDependencies);
            InitializeAttributeIndices();
            m_TagIds = m_TagParents.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            for (int index = 0; index < m_TagIds.Length; index++)
                m_TagIndices.Add(m_TagIds[index], index);
            m_EffectsByIndex = new PortableEffectDefinition[ContentLayout.Definitions.Count];
            foreach (PortableEffectDefinition effect in m_Effects.Values)
            {
                effect.Index = ContentLayout.DefinitionIndex(effect.Id);
                effect.Layout = ContentLayout.Definitions[effect.Index];
                m_EffectsByIndex[effect.Index] = effect;
                if (effect.Layout.DefinitionRevision != effect.Revision)
                    throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                        GameplayEffectPreparationFailureKind.UnsupportedVersion,
                        GameplayEffectCapacityDimension.ContentVersion, effect.Id,
                        effect.Layout.DefinitionRevision, effect.Revision));
            }
            LifecycleIdentities = new GameplayEffectPublishedIdentityReader(
                m_EffectsByIndex.Select(effect => effect.Id)
                    .Concat(m_Attributes.Keys)
                    .Concat(m_TagIds)
                    .Concat(m_EffectsByIndex.SelectMany(effect => effect.SetByCallerParameters))
                    .Concat(m_EffectsByIndex.SelectMany(effect => effect.CueIds)));
        }

        public IReadOnlyDictionary<string, string> TagParents => m_TagParents;
        public IReadOnlyList<string> InitialTags => m_InitialTags;
        public IReadOnlyDictionary<string, PortableAttributeDefinition> Attributes => m_Attributes;
        public GameplayAttributeDependencyLayout AttributeDependencies { get; }
        public GameplayEffectActorCapacity ActorCapacity => m_Binding.ActorCapacity;
        public GameplayEffectContentLayout ContentLayout { get; }
        public GameplayEffectRuntimeCapacityLayout CapacityLayout { get; private set; }
        public IReadOnlyDictionary<string, PortableEffectDefinition> Effects => m_Effects;
        internal GameplayEffectPublishedIdentityReader LifecycleIdentities { get; }
        public int TagIndex(string tagId) => m_TagIndices[tagId];
        public bool ContainsTag(string tagId) => m_TagIndices.ContainsKey(tagId);
        public string TagId(int index) => m_TagIds[index];
        public PortableEffectDefinition EffectAt(int index) => m_EffectsByIndex[index];

        public bool TryResolveTagIdentity(CanonicalTextValue value, out string identity)
        {
            const string prefix = "tag:";
            ReadOnlySpan<char> query = ReadQueryIdentity(value, prefix, "GameplayTag");
            int low = 0;
            int high = m_TagIds.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                string candidate = m_TagIds[middle];
                int comparison = candidate.AsSpan(prefix.Length).SequenceCompareTo(query);
                if (comparison < 0)
                    low = middle + 1;
                else if (comparison > 0)
                    high = middle - 1;
                else
                {
                    identity = candidate;
                    return true;
                }
            }
            identity = null;
            return false;
        }

        public bool TryResolveAttributeIndex(CanonicalTextValue value, out int index)
        {
            const string prefix = "attribute:";
            ReadOnlySpan<char> query = ReadQueryIdentity(value, prefix, "GameplayAttribute");
            int low = 0;
            int high = AttributeDependencies.AttributeCount - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = AttributeDependencies.AttributeId(middle).AsSpan(prefix.Length).SequenceCompareTo(query);
                if (comparison < 0)
                    low = middle + 1;
                else if (comparison > 0)
                    high = middle - 1;
                else
                {
                    index = middle;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        static ReadOnlySpan<char> ReadQueryIdentity(CanonicalTextValue value, string prefix, string parameterName)
        {
            ReadOnlySpan<char> identity = value.AsSpan();
            int start = 0;
            int end = identity.Length;
            while (start < end && char.IsWhiteSpace(identity[start]))
                start++;
            while (end > start && char.IsWhiteSpace(identity[end - 1]))
                end--;
            if (start == end)
                throw new ArgumentException("Identity is required.", parameterName);
            identity = identity.Slice(start, end - start);
            return identity.Length >= prefix.Length && identity.Slice(0, prefix.Length).SequenceEqual(prefix.AsSpan())
                ? identity.Slice(prefix.Length)
                : identity;
        }

        public GameplayEffectRuntimeCapacityLayout PrepareCapacity(in GameplayEffectSessionCapacity session)
        {
            if (!GameplayEffectRuntimeCapacityLayout.TryPrepare(m_Binding, in session,
                out GameplayEffectRuntimeCapacityLayout layout, out GameplayEffectPreparationFailure failure))
                throw new GameplayEffectPreparationException(failure);
            CapacityLayout = layout;
            return layout;
        }

        public bool TryGetEffect(CanonicalTextValue identity, out PortableEffectDefinition definition)
        {
            for (int i = 0; i < m_EffectsByIndex.Length; i++)
                if (identity.Equals(m_EffectsByIndex[i].Id))
                {
                    definition = m_EffectsByIndex[i];
                    return true;
                }
            definition = null;
            return false;
        }

        public PortableEffectDefinition RequireEffect(CanonicalTextValue effectId)
        {
            ReadOnlySpan<char> identity = ReadQueryIdentity(effectId, "effect:", "GameplayEffect");
            for (int i = 0; i < m_EffectsByIndex.Length; i++)
                if (m_EffectsByIndex[i].Id.AsSpan("effect:".Length).SequenceEqual(identity))
                    return m_EffectsByIndex[i];
            throw new KeyNotFoundException("Gameplay Effect identity is absent from the published catalog.");
        }

        public bool IsTagOrParent(string ownedTag, string queryTag)
        {
            string current = ownedTag;
            while (!string.IsNullOrEmpty(current))
            {
                if (string.Equals(current, queryTag, StringComparison.Ordinal))
                    return true;
                current = m_TagParents.TryGetValue(current, out string parent) ? parent : string.Empty;
            }
            return false;
        }

        public bool Matches(PortableTagQuery query, IReadOnlyList<string> tags)
        {
            IReadOnlyList<string> owned = tags ?? Array.Empty<string>();
            for (int i = 0; i < query.All.Length; i++)
            {
                if (!ContainsTag(owned, query.All[i]))
                    return false;
            }
            if (query.Any.Length > 0)
            {
                bool matched = false;
                for (int i = 0; i < query.Any.Length; i++)
                {
                    if (!ContainsTag(owned, query.Any[i]))
                        continue;
                    matched = true;
                    break;
                }
                if (!matched)
                    return false;
            }
            for (int i = 0; i < query.None.Length; i++)
            {
                if (ContainsTag(owned, query.None[i]))
                    return false;
            }
            return true;
        }

        bool ContainsTag(IReadOnlyList<string> owned, string required)
        {
            for (int i = 0; i < owned.Count; i++)
                if (IsTagOrParent(owned[i], required))
                    return true;
            return false;
        }

        void ReadBinding(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            int catalogVersion = reader.ReadInt32();
            if (catalogVersion != CharacterGameplayEffectRuntimeBinding.CatalogFormatVersion)
                throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                    GameplayEffectPreparationFailureKind.UnsupportedVersion,
                    GameplayEffectCapacityDimension.ContentVersion, m_Binding.SourceIdentity,
                    CharacterGameplayEffectRuntimeBinding.CatalogFormatVersion, catalogVersion));
            int tagCount = ReadCount(reader, "Gameplay Tag");
            for (int i = 0; i < tagCount; i++)
            {
                string id = NormalizeTag(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                string parent = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
                m_TagParents.Add(id, string.IsNullOrEmpty(parent) ? string.Empty : NormalizeTag(parent));
                if (reader.ReadBoolean())
                    m_InitialTags.Add(id);
            }
            int attributeCount = ReadCount(reader, "Gameplay Attribute");
            for (int i = 0; i < attributeCount; i++)
            {
                string id = NormalizeAttribute(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                m_Attributes.Add(
                    id,
                    new PortableAttributeDefinition(
                        id,
                        Float32Scalar.FromDouble(reader.ReadDouble()),
                        ReadBindingBound(reader),
                        ReadBindingBound(reader)));
            }
            int effectCount = ReadCount(reader, "Gameplay Effect");
            for (int i = 0; i < effectCount; i++)
            {
                string id = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
                uint revision = reader.ReadUInt32();
                string[] tags = ReadStrings(reader, true);
                PortableEffectDefinition definition = DecodeBindingEffect(id, revision, tags, reader.ReadBytes());
                if (!m_Effects.TryAdd(definition.Id, definition))
                    throw new InvalidDataException($"Gameplay Effect '{definition.Id}' is duplicated in the runtime catalog.");
            }
            reader.RequireComplete();
        }

        PortableAttributeBound ReadBindingBound(CanonicalReader reader)
        {
            if (!reader.ReadBoolean())
                return default;
            int source = reader.ReadInt32();
            if (source == 0)
                return new PortableAttributeBound(true, false, Float32Scalar.FromDouble(reader.ReadDouble()), string.Empty);
            if (source == 1)
                return new PortableAttributeBound(true, true, Float32Scalar.Zero, NormalizeAttribute(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())));
            throw new InvalidDataException($"Gameplay Attribute bound source '{source}' is invalid.");
        }

        PortableEffectDefinition DecodeBindingEffect(
            string entryIdentity,
            uint entryRevision,
            string[] tags,
            byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            int version = reader.ReadInt32();
            if (version != 2)
                throw new InvalidDataException($"Gameplay Effect '{entryIdentity}' format '{version}' is unsupported.");
            string id = NormalizeEffect(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            uint revision = reader.ReadUInt32();
            if (!string.Equals(id, NormalizeEffect(entryIdentity), StringComparison.Ordinal) || revision != entryRevision || revision == 0)
                throw new InvalidDataException($"Gameplay Effect '{entryIdentity}' runtime identity or revision does not match its definition bytes.");
            GameplayEffectScopeKind scope = EnumValue<GameplayEffectScopeKind>(reader.ReadInt32(), "effect scope");
            GameplayEffectLifetimeKind lifetime = EnumValue<GameplayEffectLifetimeKind>(reader.ReadInt32(), "effect lifetime");
            PortableEffectDurationPolicy durationPolicy = EnumValue<PortableEffectDurationPolicy>(reader.ReadInt32(), "duration policy");
            PortableMagnitude duration = ReadMagnitude(reader, true);
            bool hasPeriod = reader.ReadBoolean();
            PortableMagnitude period = ReadMagnitude(reader, true);
            bool executeOnApplication = reader.ReadBoolean();
            PortableEffectStackingPolicy stacking = EnumValue<PortableEffectStackingPolicy>(reader.ReadInt32(), "stacking policy");
            int maxStacks = reader.ReadInt32();
            if (maxStacks <= 0)
                throw new InvalidDataException($"Gameplay Effect '{id}' MaxStacks must be positive.");
            PortableEffectDurationUpdatePolicy durationUpdate = EnumValue<PortableEffectDurationUpdatePolicy>(reader.ReadInt32(), "duration update policy");
            PortableEffectPeriodUpdatePolicy periodUpdate = EnumValue<PortableEffectPeriodUpdatePolicy>(reader.ReadInt32(), "period update policy");
            PortableEffectOverflowPolicy overflow = EnumValue<PortableEffectOverflowPolicy>(reader.ReadInt32(), "overflow policy");
            string[] setByCaller = ReadStrings(reader, false);
            PortableEffectComponent[] components = ReadComponents(reader, true);
            reader.RequireComplete();
            return new PortableEffectDefinition(
                id,
                revision,
                tags,
                durationPolicy,
                duration,
                hasPeriod,
                period,
                executeOnApplication,
                stacking,
                maxStacks,
                durationUpdate,
                periodUpdate,
                overflow,
                setByCaller,
                scope,
                lifetime,
                components);
        }

        static AttackSweepGeometryData<Float32Scalar> ReadAttackGeometry(CanonicalReader reader, bool sourceDouble)
        {
            Float32Scalar Scalar() => sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar();
            ThirdPersonSimulation.Collision.SweepVector3<Float32Scalar> Vector() =>
                new ThirdPersonSimulation.Collision.SweepVector3<Float32Scalar>(Scalar(), Scalar(), Scalar());
            string identity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            int capacity = reader.ReadInt32();
            Float32Scalar error = Scalar();
            var parts = new AttackSweepPartData<Float32Scalar>[reader.ReadInt32()];
            for (int part = 0; part < parts.Length; part++)
            {
                string id = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
                var kind = (ThirdPersonSimulation.Collision.SweepShapeKind)reader.ReadInt32();
                var halfExtents = Vector();
                Float32Scalar radius = Scalar();
                Float32Scalar halfLength = Scalar();
                var vertices = new ThirdPersonSimulation.Collision.SweepVector3<Float32Scalar>[reader.ReadInt32()];
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                    vertices[vertex] = Vector();
                var samples = new ThirdPersonSimulation.Collision.SweepSample<Float32Scalar>[reader.ReadInt32()];
                for (int sample = 0; sample < samples.Length; sample++)
                {
                    Float32Scalar time = Scalar();
                    var position = Vector();
                    var rotation = new ThirdPersonSimulation.Collision.SweepQuaternion<Float32Scalar>(Scalar(), Scalar(), Scalar(), Scalar());
                    samples[sample] = new ThirdPersonSimulation.Collision.SweepSample<Float32Scalar>(time,
                        new ThirdPersonSimulation.Collision.SweepPose<Float32Scalar>(position, rotation));
                }
                parts[part] = new AttackSweepPartData<Float32Scalar>(id, kind, halfExtents, radius, halfLength, vertices, samples);
            }
            return new AttackSweepGeometryData<Float32Scalar>(identity, capacity, error, parts);
        }

        PortableEffectComponent[] ReadComponents(CanonicalReader reader, bool sourceDouble = false)
        {
            int count = ReadCount(reader, "Gameplay Effect component");
            var result = new PortableEffectComponent[count];
            for (int i = 0; i < count; i++)
            {
                string type = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
                if (type.EndsWith(".GameplayModifierComponentDefinition", StringComparison.Ordinal))
                {
                    result[i] = new PortableModifierComponent(
                        CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                        EnumValue<PortableModifierApplication>(reader.ReadInt32(), "modifier application"),
                        EnumValue<PortableModifierOperation>(reader.ReadInt32(), "modifier operation"),
                        ReadMagnitude(reader, sourceDouble),
                        reader.ReadInt32(),
                        EnumValue<PortableClampBound>(reader.ReadInt32(), "modifier clamp bound"),
                        reader.ReadBoolean());
                }
                else if (type.EndsWith(".GrantedTagsComponentDefinition", StringComparison.Ordinal))
                {
                    result[i] = new PortableGrantedTagsComponent(ReadStrings(reader, true));
                }
                else if (type.EndsWith(".BlockAbilitiesComponentDefinition", StringComparison.Ordinal))
                {
                    result[i] = new PortableBlockAbilitiesComponent(ReadQuery(reader));
                }
                else if (type.EndsWith(".ReplacementSourceRequirementComponentDefinition", StringComparison.Ordinal))
                {
                    result[i] = new PortableReplacementSourceRequirementComponent(ReadQuery(reader), ReadQuery(reader));
                }
                else if (type.EndsWith(".GameplayTagRequirementsComponentDefinition", StringComparison.Ordinal))
                {
                    result[i] = new PortableTagRequirementsComponent(
                        EnumValue<PortableRequirementPhase>(reader.ReadInt32(), "tag requirement phase"),
                        ReadQuery(reader),
                        ReadQuery(reader));
                }
                else if (type.EndsWith(".GameplayAttributeRequirementsComponentDefinition", StringComparison.Ordinal))
                {
                    result[i] = new PortableAttributeRequirementsComponent(
                        EnumValue<PortableRequirementPhase>(reader.ReadInt32(), "attribute requirement phase"),
                        EnumValue<PortableAttributeSource>(reader.ReadInt32(), "attribute requirement source"),
                        CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                        EnumValue<PortableAttributeComparison>(reader.ReadInt32(), "attribute comparison"),
                        ReadMagnitude(reader, sourceDouble));
                }
                else if (type.EndsWith(".GameplayEffectExecutionComponentDefinition", StringComparison.Ordinal))
                {
                    int mutationCount = ReadCount(reader, "Gameplay Effect execution mutation");
                    var mutations = new PortableExecutionMutation[mutationCount];
                    for (int mutationIndex = 0; mutationIndex < mutationCount; mutationIndex++)
                    {
                        mutations[mutationIndex] = new PortableExecutionMutation(
                            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                            EnumValue<PortableModifierOperation>(reader.ReadInt32(), "execution modifier operation"),
                            ReadMagnitude(reader, sourceDouble),
                            EnumValue<PortableClampBound>(reader.ReadInt32(), "execution clamp bound"));
                    }
                    result[i] = new PortableExecutionComponent(mutations);
                }
                else if (type.EndsWith(".AdditionalEffectsComponentDefinition", StringComparison.Ordinal))
                {
                    int effectCount = ReadCount(reader, "Additional Gameplay Effect");
                    var effects = new PortableAdditionalEffect[effectCount];
                    for (int effectIndex = 0; effectIndex < effectCount; effectIndex++)
                    {
                        PortableAdditionalEffectTrigger trigger = EnumValue<PortableAdditionalEffectTrigger>(reader.ReadInt32(), "additional effect trigger");
                        string effectId = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
                        int bindingCount = ReadCount(reader, "Additional Gameplay Effect parameter binding");
                        var bindings = new PortableAdditionalParameterBinding[bindingCount];
                        for (int bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
                        {
                            bindings[bindingIndex] = new PortableAdditionalParameterBinding(
                                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                                EnumValue<PortableAdditionalParameterSource>(reader.ReadInt32(), "additional effect parameter source"),
                                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                                sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar());
                        }
                        effects[effectIndex] = new PortableAdditionalEffect(trigger, effectId, bindings);
                    }
                    result[i] = new PortableAdditionalEffectsComponent(effects);
                }
                else if (type.EndsWith(".GameplayAttackCameraShakeComponentDefinition", StringComparison.Ordinal))
                {
                    result[i] = new PortableAttackCameraShakeComponent(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), reader.ReadBoolean());
                }
                else if (type.EndsWith(".GameplayAttackCollisionComponentDefinition", StringComparison.Ordinal))
                {
                    result[i] = new PortableAttackCollisionComponent(
                        EnumValue<PortableAttackCollisionKind>(reader.ReadInt32(), "attack collision kind"),
                        sourceDouble
                            ? new Float32Vector3(Float32Scalar.FromDouble(reader.ReadDouble()),
                                Float32Scalar.FromDouble(reader.ReadDouble()), Float32Scalar.FromDouble(reader.ReadDouble()))
                            : reader.ReadVector3(),
                        sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar(),
                        sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar(),
                        sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar(),
                        sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar(),
                        sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar(),
                        sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar(),
                        sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadBoolean(),
                        sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadBoolean());
                    var collision = (PortableAttackCollisionComponent)result[i];
                    if (collision.Kind == PortableAttackCollisionKind.GeometrySweep)
                        collision.Geometry = ReadAttackGeometry(reader, sourceDouble);
                }
                else if (type.EndsWith(".GameplayAttackPropertyComponentDefinition", StringComparison.Ordinal))
                {
                    result[i] = new PortableAttackPropertyComponent(
                        CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        ReadStrings(reader, false),
                        ReadMagnitude(reader, sourceDouble),
                        ReadMagnitude(reader, sourceDouble),
                        ReadMagnitude(reader, sourceDouble),
                        ReadMagnitude(reader, sourceDouble),
                        ReadMagnitude(reader, sourceDouble),
                        ReadMagnitude(reader, sourceDouble),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        ReadMagnitude(reader, sourceDouble),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadUInt32(),
                        reader.ReadUInt32(),
                        reader.ReadUInt32(),
                        CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                        CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                        reader.ReadBoolean(),
                        reader.ReadBoolean(),
                        reader.ReadBoolean(),
                        reader.ReadBoolean(),
                        reader.ReadBoolean(),
                        reader.ReadBoolean(),
                        reader.ReadBoolean(),
                        reader.ReadBoolean(),
                        reader.ReadBoolean(),
                        reader.ReadBoolean());
                }
                else if (type.EndsWith(".GameplayCueBindingComponentDefinition", StringComparison.Ordinal))
                {
                    result[i] = new PortableCueComponent(
                        CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                        EnumValue<PortableCueTrigger>(reader.ReadInt32(), "Gameplay Cue trigger"));
                }
                else
                {
                    throw new InvalidDataException($"Gameplay Effect component '{type}' has no portable decoder.");
                }
            }
            return result;
        }

        PortableMagnitude ReadMagnitude(CanonicalReader reader, bool sourceDouble = false)
        {
            return new PortableMagnitude(
                EnumValue<PortableMagnitudeSource>(reader.ReadInt32(), "magnitude source"),
                sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                NormalizeOptionalAttribute(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar(),
                sourceDouble ? Float32Scalar.FromDouble(reader.ReadDouble()) : reader.ReadScalar());
        }

        PortableTagQuery ReadQuery(CanonicalReader reader)
        {
            return new PortableTagQuery(ReadQueryTags(reader), ReadQueryTags(reader), ReadQueryTags(reader));
        }

        static string[] ReadQueryTags(CanonicalReader reader)
        {
            int count = ReadCount(reader, "Tag query");
            var values = new string[count];
            for (int i = 0; i < count; i++)
                values[i] = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            return values;
        }

        GameplayAttributeDependencyLayout BuildAttributeDependencies()
        {
            var builder = new GameplayAttributeDependencyBuilder();
            foreach (string id in m_Attributes.Keys)
                builder.AddAttribute(id);
            foreach (PortableAttributeDefinition attribute in m_Attributes.Values)
            {
                AddBoundDependency(builder, attribute.Id, attribute.Minimum, "Minimum");
                AddBoundDependency(builder, attribute.Id, attribute.Maximum, "Maximum");
            }
            foreach (PortableEffectDefinition effect in m_Effects.Values)
            {
                if (effect.DurationPolicy == PortableEffectDurationPolicy.Instant)
                    continue;
                for (int i = 0; i < effect.Components.Length; i++)
                {
                    if (effect.Components[i] is PortableModifierComponent modifier &&
                        modifier.Application == PortableModifierApplication.CurrentValue &&
                        modifier.Magnitude.Source == PortableMagnitudeSource.TargetAttributeLive)
                        builder.AddDependency(modifier.Magnitude.AttributeId, modifier.AttributeId,
                            $"Effect '{effect.Id}' component #{i}", false);
                }
            }
            return builder.Build();
        }

        void InitializeAttributeIndices()
        {
            foreach (PortableAttributeDefinition attribute in m_Attributes.Values)
            {
                attribute.Index = AttributeDependencies.AttributeIndex(attribute.Id);
                attribute.MinimumAttributeIndex = attribute.Minimum.Enabled && attribute.Minimum.FromAttribute
                    ? AttributeDependencies.AttributeIndex(attribute.Minimum.AttributeId) : -1;
                attribute.MaximumAttributeIndex = attribute.Maximum.Enabled && attribute.Maximum.FromAttribute
                    ? AttributeDependencies.AttributeIndex(attribute.Maximum.AttributeId) : -1;
            }
        }

        static void AddBoundDependency(GameplayAttributeDependencyBuilder builder, string output,
            PortableAttributeBound bound, string kind)
        {
            if (bound.Enabled && bound.FromAttribute)
                builder.AddDependency(bound.AttributeId, output, $"{output}.{kind}", true);
        }

        void ValidateClosure()
        {
            foreach (KeyValuePair<string, string> pair in m_TagParents)
            {
                if (!string.IsNullOrEmpty(pair.Value) && !m_TagParents.ContainsKey(pair.Value))
                    throw new InvalidDataException($"Gameplay Tag '{pair.Key}' references missing parent '{pair.Value}'.");
                string current = pair.Key;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (!string.IsNullOrEmpty(current))
                {
                    if (!visited.Add(current))
                        throw new InvalidDataException($"Gameplay Tag parent cycle reaches '{current}'.");
                    current = m_TagParents.TryGetValue(current, out string parent) ? parent : string.Empty;
                }
            }
            foreach (PortableEffectDefinition effect in m_Effects.Values)
            {
                for (int i = 0; i < effect.EffectTags.Length; i++)
                    RequireTag(effect.Id, effect.EffectTags[i]);
                ValidateMagnitude(effect.Id, effect.DurationMagnitude);
                if (effect.HasPeriod)
                    ValidateMagnitude(effect.Id, effect.PeriodMagnitude);
                if (effect.Lifetime != GameplayEffectLifetimeKind.Independent &&
                    (effect.DurationPolicy == PortableEffectDurationPolicy.Instant ||
                     effect.StackingPolicy != PortableEffectStackingPolicy.Independent))
                    throw new InvalidDataException($"Gameplay Effect '{effect.Id}' held lifetime requires an Independent active effect.");
                for (int i = 0; i < effect.Components.Length; i++)
                    ValidateComponent(effect, effect.Components[i]);
            }
        }

        void ValidateComponent(PortableEffectDefinition owner, PortableEffectComponent component)
        {
            switch (component)
            {
                case PortableModifierComponent modifier:
                    if (owner.Scope == GameplayEffectScopeKind.ActionInstance)
                        throw new InvalidDataException($"Gameplay Effect '{owner.Id}' cannot modify Actor attributes in ActionInstance scope.");
                    RequireAttribute(owner.Id, modifier.AttributeId);
                    ValidateMagnitude(owner.Id, modifier.Magnitude);
                    break;
                case PortableGrantedTagsComponent granted:
                    for (int i = 0; i < granted.Tags.Length; i++) RequireTag(owner.Id, granted.Tags[i]);
                    break;
                case PortableTagRequirementsComponent tags:
                    ValidateQuery(owner.Id, tags.Source);
                    ValidateQuery(owner.Id, tags.Target);
                    break;
                case PortableBlockAbilitiesComponent blocked:
                    ValidateQuery(owner.Id, blocked.Query);
                    break;
                case PortableReplacementSourceRequirementComponent replacement:
                    if (owner.Scope != GameplayEffectScopeKind.Actor)
                        throw new InvalidDataException($"Gameplay Effect '{owner.Id}' replacement source requirements must use Actor scope.");
                    ValidateQuery(owner.Id, replacement.CandidateQuery);
                    ValidateQuery(owner.Id, replacement.SelectedSourceQuery);
                    break;
                case PortableAttributeRequirementsComponent attributes:
                    RequireAttribute(owner.Id, attributes.AttributeId);
                    ValidateMagnitude(owner.Id, attributes.Threshold);
                    break;
                case PortableExecutionComponent execution:
                    if (owner.Scope == GameplayEffectScopeKind.ActionInstance)
                        throw new InvalidDataException($"Gameplay Effect '{owner.Id}' cannot execute Actor attribute mutations in ActionInstance scope.");
                    for (int i = 0; i < execution.Mutations.Length; i++)
                    {
                        RequireAttribute(owner.Id, execution.Mutations[i].AttributeId);
                        ValidateMagnitude(owner.Id, execution.Mutations[i].Magnitude);
                    }
                    break;
                case PortableAdditionalEffectsComponent additional:
                    for (int i = 0; i < additional.Effects.Length; i++)
                    {
                        if (!m_Effects.ContainsKey(additional.Effects[i].EffectId))
                            throw new InvalidDataException($"Gameplay Effect '{owner.Id}' references missing Additional Effect '{additional.Effects[i].EffectId}'.");
                    }
                    break;
                case PortableAttackCollisionComponent:
                case PortableAttackCameraShakeComponent:
                    break;
                case PortableAttackPropertyComponent attackComponent:
                    for (int i = 0; i < attackComponent.CombatTags.Length; i++)
                        SimulationIdentity.Require(attackComponent.CombatTags[i], "CombatTag");
                    ValidateMagnitude(owner.Id, attackComponent.DamagePercentage);
                    ValidateMagnitude(owner.Id, attackComponent.AddedDamage);
                    ValidateMagnitude(owner.Id, attackComponent.BreakStunRatio);
                    ValidateMagnitude(owner.Id, attackComponent.ElementAccumulation);
                    ValidateMagnitude(owner.Id, attackComponent.ExhaustedAccumulation);
                    ValidateMagnitude(owner.Id, attackComponent.ExhaustedChase);
                    ValidateMagnitude(owner.Id, attackComponent.DamageBreakLevelProbability);
                    break;
                case PortableCueComponent:
                    break;
                default:
                    throw new InvalidDataException($"Gameplay Effect '{owner.Id}' contains unknown portable component '{component?.GetType().FullName}'.");
            }
        }

        void ValidateMagnitude(string owner, PortableMagnitude magnitude)
        {
            if (magnitude.Source == PortableMagnitudeSource.SetByCaller && string.IsNullOrEmpty(magnitude.SetByCallerParameterId))
                throw new InvalidDataException($"Gameplay Effect '{owner}' has a SetByCaller magnitude without a parameter id.");
            if (magnitude.Source == PortableMagnitudeSource.SourceAttributeSnapshot ||
                magnitude.Source == PortableMagnitudeSource.TargetAttributeSnapshot ||
                magnitude.Source == PortableMagnitudeSource.TargetAttributeLive)
                RequireAttribute(owner, magnitude.AttributeId);
        }

        void ValidateQuery(string owner, PortableTagQuery query)
        {
            foreach (string tag in query.All.Concat(query.Any).Concat(query.None))
                RequireTag(owner, tag);
        }

        void RequireTag(string owner, string id)
        {
            if (!m_TagParents.ContainsKey(id))
                throw new InvalidDataException($"Gameplay definition '{owner}' references missing Tag '{id}'.");
        }

        void RequireAttribute(string owner, string id)
        {
            if (!m_Attributes.ContainsKey(id))
                throw new InvalidDataException($"Gameplay definition '{owner}' references missing Attribute '{id}'.");
        }

        static string[] ReadStrings(CanonicalReader reader, bool normalizeTags)
        {
            int count = ReadCount(reader, "string array");
            var values = new string[count];
            for (int i = 0; i < count; i++)
                values[i] = normalizeTags ? NormalizeTag(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())) : SimulationIdentity.Require(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), "CatalogString");
            Array.Sort(values, StringComparer.Ordinal);
            for (int i = 1; i < values.Length; i++)
            {
                if (string.Equals(values[i - 1], values[i], StringComparison.Ordinal))
                    throw new InvalidDataException($"Canonical string array contains duplicate '{values[i]}'.");
            }
            return values;
        }

        static int ReadCount(CanonicalReader reader, string label)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 100000)
                throw new InvalidDataException($"{label} count '{count}' is invalid.");
            return count;
        }

        static T EnumValue<T>(int value, string label) where T : struct, Enum
        {
            object candidate = Enum.ToObject(typeof(T), value);
            if (!Enum.IsDefined(typeof(T), candidate))
                throw new InvalidDataException($"Gameplay Effect {label} '{value}' is invalid.");
            return (T)candidate;
        }

        internal static string NormalizeTag(string value) => GameplayEffectRuntimeIdentity.Tag(value);
        internal static string NormalizeAttribute(string value) => GameplayEffectRuntimeIdentity.Attribute(value);
        internal static string NormalizeEffect(string value) => GameplayEffectRuntimeIdentity.Effect(value);
        static string NormalizeOptionalAttribute(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeAttribute(value);

    }
}


