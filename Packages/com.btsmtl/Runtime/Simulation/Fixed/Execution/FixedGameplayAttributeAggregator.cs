using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal struct PortableModifierRemovalCursor
    {
        internal PortableModifierRemovalCursor(ulong sourceHandle, int modifierIndex)
        {
            SourceHandle = sourceHandle;
            AttributeIndex = 0;
            ModifierIndex = modifierIndex;
        }

        internal readonly ulong SourceHandle;
        internal int AttributeIndex;
        internal int ModifierIndex;
    }

    internal sealed class FixedGameplayAttributeAggregator
    {
        readonly GameplayAttributeDependencyLayout m_Layout;
        readonly GameplayAttributeDependencyWorkspace m_Dependencies;
        readonly SortedList<string, PortableAttributeState> m_Attributes;
        readonly PortableAttributeBefore[] m_Before;
        readonly List<PortableAttributeChange> m_Changes;
        GameplayEffectStateAggregate m_Baseline;

        internal FixedGameplayAttributeAggregator(FixedGameplayEffectRuntimeCatalog catalog)
        {
            m_Layout = catalog.AttributeDependencies;
            m_Dependencies = new GameplayAttributeDependencyWorkspace(m_Layout);
            m_Attributes = new SortedList<string, PortableAttributeState>(m_Layout.AttributeCount, StringComparer.Ordinal);
            m_Before = new PortableAttributeBefore[m_Layout.AttributeCount];
            m_Changes = new List<PortableAttributeChange>(m_Layout.AttributeCount);
            for (int index = 0; index < m_Layout.AttributeCount; index++)
            {
                string id = m_Layout.AttributeId(index);
                PortableAttributeDefinition definition = catalog.Attributes[id];
                m_Attributes.Add(id, new PortableAttributeState
                {
                    Definition = definition,
                    BaseValue = definition.InitialBase,
                    CurrentValue = definition.InitialBase,
                    Revision = 1
                });
            }
        }

        internal SortedList<string, PortableAttributeState> Attributes => m_Attributes;
        internal bool HasChanges => m_Dependencies.HasDeviations;

        internal void PrepareModifierCapacity(int activeEffectWidth, FixedGameplayEffectRuntimeCatalog catalog)
        {
            var capacities = new int[m_Attributes.Count];
            foreach (PortableEffectDefinition effect in catalog.Effects.Values)
            {
                var counts = new int[m_Attributes.Count];
                foreach (PortableEffectComponent component in effect.Components)
                    if (component is PortableModifierComponent modifier && modifier.Application == PortableModifierApplication.CurrentValue)
                        counts[m_Layout.AttributeIndex(modifier.AttributeId)]++;
                for (int i = 0; i < capacities.Length; i++)
                    capacities[i] = Math.Max(capacities[i], counts[i] * activeEffectWidth);
            }
            for (int i = 0; i < capacities.Length; i++)
                m_Attributes.Values[i].Modifiers.Capacity = capacities[i];
        }

        internal void Initialize()
        {
            for (int rank = 0; rank < m_Layout.AttributeCount; rank++)
                CalculateCurrent(m_Layout.TopologicalAttribute(rank));
        }

        internal void AcceptBaseline(GameplayEffectStateAggregate baseline)
        {
            m_Baseline = baseline;
            m_Dependencies.AcceptBaseline();
        }

        internal void RebuildDependencies()
        {
            m_Dependencies.ResetSources();
            m_Changes.Clear();
            for (int index = 0; index < m_Attributes.Count; index++)
            {
                List<PortableAttributeModifierState> modifiers = m_Attributes.Values[index].Modifiers;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    int input = modifiers[i].LiveAttributeIndex;
                    if (input >= 0)
                        m_Dependencies.AddSource(input, index);
                }
            }
        }

        void BeginMutation(int root)
        {
            m_Dependencies.CollectAffected(root);
            foreach (int index in m_Dependencies.Affected)
            {
                PortableAttributeState value = m_Attributes.Values[index];
                m_Before[index] = new PortableAttributeBefore(value.BaseValue, value.CurrentValue, value.Revision);
            }
        }

        IReadOnlyList<PortableAttributeChange> CompleteMutation(ulong causeHandle, int directRoot)
        {
            m_Changes.Clear();
            m_Dependencies.SortForEvaluation();
            foreach (int index in m_Dependencies.Affected)
                if (index != directRoot)
                    CalculateCurrent(index);
            if (directRoot >= 0)
                AddChange(directRoot, causeHandle);
            m_Dependencies.SortForOutput();
            foreach (int index in m_Dependencies.Affected)
            {
                PortableAttributeState current = m_Attributes.Values[index];
                if (index != directRoot)
                {
                    PortableAttributeBefore previous = m_Before[index];
                    if (previous.Base != current.BaseValue || previous.Current != current.CurrentValue)
                    {
                        current.Revision = checked(current.Revision + 1);
                        AddChange(index, causeHandle);
                    }
                }
                m_Dependencies.SetDeviation(index, !m_Baseline.IsValid || !m_Baseline.MatchesAttributeAt(index, current));
            }
            m_Dependencies.ClearMutation();
            return m_Changes;
        }

        void AddChange(int index, ulong causeHandle)
        {
            PortableAttributeState current = m_Attributes.Values[index];
            PortableAttributeBefore previous = m_Before[index];
            m_Changes.Add(new PortableAttributeChange(current.Definition.Id, previous.Base, current.BaseValue,
                previous.Current, current.CurrentValue, current.Revision, causeHandle));
        }

        internal IReadOnlyList<PortableAttributeChange> MutateBase(string attributeId,
            PortableModifierOperation operation, FixedScalar magnitude, PortableClampBound clampBound, ulong causeHandle)
        {
            int index = m_Layout.AttributeIndex(attributeId);
            BeginMutation(index);
            PortableAttributeState attribute = m_Attributes.Values[index];
            attribute.BaseValue = GameplayAttributeCalculation<FixedScalar, FixedSimulationMath>.ApplyModifier(attribute.BaseValue, operation, magnitude, clampBound);
            return CompleteMutation(causeHandle, -1);
        }

        internal IReadOnlyList<PortableAttributeChange> AddModifier(string attributeId, PortableAttributeModifierState modifier)
        {
            int index = m_Layout.AttributeIndex(attributeId);
            BeginMutation(index);
            if (modifier.LiveAttributeIndex >= 0)
                m_Dependencies.AddSource(modifier.LiveAttributeIndex, index);
            List<PortableAttributeModifierState> modifiers = m_Attributes.Values[index].Modifiers;
            int insertionIndex = modifiers.Count;
            while (insertionIndex > 0 && PortableAttributeModifierComparer.Compare(modifiers[insertionIndex - 1], modifier) > 0)
                insertionIndex--;
            modifiers.Insert(insertionIndex, modifier);
            return CompleteMutation(modifier.SourceEffectHandle, -1);
        }

        internal PortableModifierRemovalCursor BeginModifierRemoval(ulong sourceHandle) =>
            new PortableModifierRemovalCursor(sourceHandle, m_Attributes.Count == 0 ? -1 : m_Attributes.Values[0].Modifiers.Count - 1);

        internal bool RemoveNextModifier(ref PortableModifierRemovalCursor cursor, out IReadOnlyList<PortableAttributeChange> changes)
        {
            while (cursor.AttributeIndex < m_Attributes.Count)
            {
                List<PortableAttributeModifierState> modifiers = m_Attributes.Values[cursor.AttributeIndex].Modifiers;
                while (cursor.ModifierIndex >= 0)
                {
                    int modifierIndex = cursor.ModifierIndex--;
                    PortableAttributeModifierState modifier = modifiers[modifierIndex];
                    if (modifier.SourceEffectHandle != cursor.SourceHandle)
                        continue;
                    BeginMutation(cursor.AttributeIndex);
                    if (modifier.LiveAttributeIndex >= 0)
                        m_Dependencies.RemoveSource(modifier.LiveAttributeIndex, cursor.AttributeIndex);
                    modifiers.RemoveAt(modifierIndex);
                    changes = CompleteMutation(cursor.SourceHandle, -1);
                    return true;
                }
                cursor.AttributeIndex++;
                if (cursor.AttributeIndex < m_Attributes.Count)
                    cursor.ModifierIndex = m_Attributes.Values[cursor.AttributeIndex].Modifiers.Count - 1;
            }
            changes = Array.Empty<PortableAttributeChange>();
            return false;
        }

        internal bool ApplyAuthoritativeAttribute(string attributeId, FixedScalar baseValue,
            FixedScalar currentValue, ulong revision, ulong causeHandle, out IReadOnlyList<PortableAttributeChange> changes)
        {
            int index = m_Layout.AttributeIndex(attributeId);
            PortableAttributeState attribute = m_Attributes.Values[index];
            if (revision <= attribute.Revision)
            {
                changes = Array.Empty<PortableAttributeChange>();
                return false;
            }
            changes = ApplyOwnedAttribute(index, baseValue, currentValue, revision, causeHandle);
            return true;
        }

        internal IReadOnlyList<PortableAttributeChange> ApplyCorrectionAttribute(string attributeId,
            FixedScalar baseValue, FixedScalar currentValue, ulong revision, ulong causeHandle) =>
            ApplyOwnedAttribute(m_Layout.AttributeIndex(attributeId), baseValue, currentValue, revision, causeHandle);

        internal void RestorePredictedAttributeOwned(string attributeId, PortablePredictionAttributeSnapshot snapshot,
            ulong causeHandle, out IReadOnlyList<PortableAttributeChange> changes)
        {
            changes = ApplyOwnedAttribute(m_Layout.AttributeIndex(attributeId), snapshot.BaseValue,
                snapshot.CurrentValue, snapshot.BeforeRevision, causeHandle);
        }

        IReadOnlyList<PortableAttributeChange> ApplyOwnedAttribute(int index, FixedScalar baseValue,
            FixedScalar currentValue, ulong revision, ulong causeHandle)
        {
            BeginMutation(index);
            PortableAttributeState attribute = m_Attributes.Values[index];
            attribute.BaseValue = baseValue;
            attribute.CurrentValue = currentValue;
            attribute.Revision = revision;
            return CompleteMutation(causeHandle, index);
        }

        void CalculateCurrent(int index)
        {
            PortableAttributeState attribute = m_Attributes.Values[index];
            var aggregation = GameplayAttributeAggregation<FixedScalar, FixedSimulationMath>.Begin();
            for (int i = 0; i < attribute.Modifiers.Count; i++)
            {
                PortableAttributeModifierState modifier = attribute.Modifiers[i];
                FixedScalar magnitude = modifier.LiveAttributeIndex < 0
                    ? modifier.Magnitude
                    : m_Attributes.Values[modifier.LiveAttributeIndex].CurrentValue * modifier.LiveCoefficient + modifier.LivePostAdd;
                aggregation.Add(modifier.Operation, magnitude, modifier.ClampBound, modifier.Priority, modifier.InsertionSequence);
            }
            PortableAttributeBound minimum = attribute.Definition.Minimum;
            if (minimum.Enabled)
                aggregation.IncludeMinimum(minimum.FromAttribute
                    ? m_Attributes.Values[attribute.Definition.MinimumAttributeIndex].CurrentValue
                    : minimum.Constant);
            PortableAttributeBound maximum = attribute.Definition.Maximum;
            if (maximum.Enabled)
                aggregation.IncludeMaximum(maximum.FromAttribute
                    ? m_Attributes.Values[attribute.Definition.MaximumAttributeIndex].CurrentValue
                    : maximum.Constant);
            attribute.CurrentValue = aggregation.Resolve(attribute.BaseValue, attribute.Definition.Id);
        }
    }
}
