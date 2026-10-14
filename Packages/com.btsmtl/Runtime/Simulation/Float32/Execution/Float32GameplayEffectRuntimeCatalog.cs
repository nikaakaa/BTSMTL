using PortableMagnitude = ThirdPersonSimulation.GameplayEffectMagnitude<ThirdPersonSimulation.Float32Scalar>;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThirdPersonGameplay.Configuration;

namespace ThirdPersonSimulation
{
    internal enum PortableEffectDurationPolicy : byte
    {
        Instant = 0,
        Duration = 1,
        Infinite = 2
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


    internal enum PortableCueTrigger : byte
    {
        OnActive = 0,
        Executed = 1,
        WhileActive = 2,
        Removed = 3,
        Expired = 4
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

    [CanonicalPublishedReference]
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
        [CanonicalReferenceKey]
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

    internal sealed partial class Float32GameplayEffectRuntimeCatalog
    {
        readonly CharacterGameplayEffectRuntimeBinding m_Binding;
        readonly Dictionary<string, string> m_TagParents = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<string> m_InitialTags = new List<string>();
        readonly List<(PortableEffectDefinition Definition, SimulationSetByCallerValue[] Parameters)> m_InitialEffects =
            new List<(PortableEffectDefinition, SimulationSetByCallerValue[])>();
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
            LifecycleIdentities = new CanonicalDeclaredIdentityReader(
                m_EffectsByIndex.Select(effect => effect.Id)
                    .Concat(m_Attributes.Keys)
                    .Concat(m_TagIds)
                    .Concat(m_EffectsByIndex.SelectMany(effect => effect.SetByCallerParameters))
                    .Concat(m_EffectsByIndex.SelectMany(effect => effect.CueIds)));
            PublishedValues.Add(this);
            PublishedValues.AddReferences(m_EffectsByIndex);
        }

        public IReadOnlyDictionary<string, string> TagParents => m_TagParents;
        public IReadOnlyList<string> InitialTags => m_InitialTags;
        public IReadOnlyList<(PortableEffectDefinition Definition, SimulationSetByCallerValue[] Parameters)> InitialEffects =>
            m_InitialEffects;
        public IReadOnlyDictionary<string, PortableAttributeDefinition> Attributes => m_Attributes;
        public GameplayAttributeDependencyLayout AttributeDependencies { get; }
        public GameplayEffectActorCapacity ActorCapacity => m_Binding.ActorCapacity;
        public GameplayEffectContentLayout ContentLayout { get; }
        public GameplayEffectRuntimeCapacityLayout CapacityLayout { get; private set; }
        public IReadOnlyDictionary<string, PortableEffectDefinition> Effects => m_Effects;
        internal CanonicalDeclaredIdentityReader LifecycleIdentities { get; }
        internal CanonicalPublishedValues PublishedValues { get; } = new CanonicalPublishedValues();
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

        internal static string NormalizeTag(string value) => GameplayEffectRuntimeIdentity.Tag(value);
        internal static string NormalizeAttribute(string value) => GameplayEffectRuntimeIdentity.Attribute(value);
        internal static string NormalizeEffect(string value) => GameplayEffectRuntimeIdentity.Effect(value);

    }
}


