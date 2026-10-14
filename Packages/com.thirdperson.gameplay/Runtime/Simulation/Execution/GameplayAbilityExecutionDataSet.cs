using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public sealed class GameplayAbilityExecutionDataSet<TData>
        where TData : class
    {
        readonly ReadOnlyCollection<TData> m_Data;
        readonly Dictionary<CharacterSkillId, TData> m_ByAbility;

        public GameplayAbilityExecutionDataSet(
            IEnumerable<TData> data,
            Func<TData, CharacterSkillId> abilityIdSelector)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (abilityIdSelector == null)
                throw new ArgumentNullException(nameof(abilityIdSelector));
            var values = new List<TData>(data);
            m_ByAbility = new Dictionary<CharacterSkillId, TData>();
            for (int i = 0; i < values.Count; i++)
            {
                TData value = values[i] ?? throw new ArgumentException("Ability execution data contains a missing entry.", nameof(data));
                CharacterSkillId abilityId = abilityIdSelector(value);
                if (!abilityId.IsValid || !m_ByAbility.TryAdd(abilityId, value))
                    throw new ArgumentException($"Ability execution data '{abilityId}' is invalid or duplicated.", nameof(data));
            }
            values.Sort((left, right) => abilityIdSelector(left).CompareTo(abilityIdSelector(right)));
            m_Data = values.AsReadOnly();
        }

        public IReadOnlyList<TData> Data => m_Data;

        public TData Require(CharacterSkillId abilityId)
        {
            if (!abilityId.IsValid || !m_ByAbility.TryGetValue(abilityId, out TData value))
                throw new InvalidOperationException($"Ability execution data '{abilityId}' is not installed.");
            return value;
        }

    }
}
