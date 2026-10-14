using System;
using System.Collections.Generic;
using System.Globalization;

namespace ThirdPersonSimulation
{
    public readonly struct CharacterInputRequestBinding
    {
        public CharacterInputRequestBinding(string requestId, string triggerTag)
        {
            RequestId = requestId;
            TriggerTag = triggerTag;
        }

        public string RequestId { get; }
        public string TriggerTag { get; }
    }

    public readonly struct GameplayAbilityGrantInputBinding
    {
        public GameplayAbilityGrantInputBinding(CharacterSkillId abilityId,
            IReadOnlyList<string> triggerAll, IReadOnlyList<string> triggerAny, IReadOnlyList<string> triggerNone,
            bool consumeSourceInputRequest, string targetInputValueId, string targetKey)
        {
            AbilityId = abilityId;
            TriggerAll = triggerAll == null ? null : Copy(triggerAll);
            TriggerAny = triggerAll == null ? null : Copy(triggerAny);
            TriggerNone = triggerAll == null ? null : Copy(triggerNone);
            ConsumeSourceInputRequest = consumeSourceInputRequest;
            TargetInputValueId = targetInputValueId;
            TargetKey = targetKey;
        }

        public CharacterSkillId AbilityId { get; }
        public bool HasTriggerQuery => TriggerAll != null;
        public IReadOnlyList<string> TriggerAll { get; }
        public IReadOnlyList<string> TriggerAny { get; }
        public IReadOnlyList<string> TriggerNone { get; }
        public bool ConsumeSourceInputRequest { get; }
        public string TargetInputValueId { get; }
        public string TargetKey { get; }

        static IReadOnlyList<string> Copy(IReadOnlyList<string> source)
        {
            var values = new string[source.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = source[i];
            Array.Sort(values, StringComparer.Ordinal);
            return Array.AsReadOnly(values);
        }
    }

    public sealed class CharacterInputRuntimeBinding
    {
        readonly Dictionary<CharacterSkillId, GameplayAbilityGrantInputBinding> m_ByAbility;

        public CharacterInputRuntimeBinding(IEnumerable<CharacterInputRequestBinding> inputRequests,
            IEnumerable<GameplayAbilityGrantInputBinding> abilityGrants)
        {
            var requests = new List<CharacterInputRequestBinding>(inputRequests);
            requests.Sort((left, right) => string.CompareOrdinal(left.RequestId, right.RequestId));
            var requestIds = new string[requests.Count];
            for (int i = 0; i < requests.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(requests[i].RequestId) ||
                    i > 0 && string.Equals(requests[i - 1].RequestId, requests[i].RequestId, StringComparison.Ordinal))
                    throw new ArgumentException("角色输入请求身份缺失或重复。", nameof(inputRequests));
                requestIds[i] = requests[i].RequestId;
            }
            InputRequests = requests.AsReadOnly();
            InputRequestIds = Array.AsReadOnly(requestIds);
            var grants = new List<GameplayAbilityGrantInputBinding>(abilityGrants);
            grants.Sort((left, right) => left.AbilityId.CompareTo(right.AbilityId));
            m_ByAbility = new Dictionary<CharacterSkillId, GameplayAbilityGrantInputBinding>(grants.Count);
            for (int i = 0; i < grants.Count; i++)
                m_ByAbility.Add(grants[i].AbilityId, grants[i]);
            AbilityGrants = grants.AsReadOnly();
            var parts = new List<string> { "character-input-runtime-binding/3", requests.Count.ToString(CultureInfo.InvariantCulture) };
            for (int i = 0; i < requests.Count; i++)
            {
                parts.Add(requests[i].RequestId);
                parts.Add(requests[i].TriggerTag);
            }
            parts.Add(grants.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < grants.Count; i++)
            {
                GameplayAbilityGrantInputBinding grant = grants[i];
                parts.Add(grant.AbilityId.Value);
                parts.Add(grant.HasTriggerQuery ? "query" : "none");
                if (grant.HasTriggerQuery)
                {
                    Add(grant.TriggerAll);
                    Add(grant.TriggerAny);
                    Add(grant.TriggerNone);
                }
                parts.Add(grant.ConsumeSourceInputRequest ? "true" : "false");
                parts.Add(grant.TargetInputValueId);
                parts.Add(grant.TargetKey);
            }
            BindingHash = StableHash.Compute(parts.ToArray());

            void Add(IReadOnlyList<string> values)
            {
                parts.Add(values.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < values.Count; i++)
                    parts.Add(values[i]);
            }
        }

        public IReadOnlyList<CharacterInputRequestBinding> InputRequests { get; }
        public IReadOnlyList<string> InputRequestIds { get; }
        public IReadOnlyList<GameplayAbilityGrantInputBinding> AbilityGrants { get; }
        public StableHash BindingHash { get; }

        public GameplayAbilityGrantInputBinding RequireAbilityGrant(CharacterSkillId abilityId) =>
            m_ByAbility[abilityId];

        public string[] PrepareAbilityTriggerRequests(CharacterSkillId abilityId,
            Func<string, string, bool> isTagOrParent)
        {
            GameplayAbilityGrantInputBinding grant = RequireAbilityGrant(abilityId);
            if (!grant.HasTriggerQuery)
                return Array.Empty<string>();
            var matches = new List<string>();
            for (int i = 0; i < InputRequests.Count; i++)
            {
                CharacterInputRequestBinding request = InputRequests[i];
                bool matchesAll = true;
                for (int tag = 0; tag < grant.TriggerAll.Count; tag++)
                    matchesAll &= isTagOrParent(request.TriggerTag, grant.TriggerAll[tag]);
                if (!matchesAll)
                    continue;
                bool matchesAny = grant.TriggerAny.Count == 0;
                for (int tag = 0; tag < grant.TriggerAny.Count; tag++)
                    matchesAny |= isTagOrParent(request.TriggerTag, grant.TriggerAny[tag]);
                if (!matchesAny)
                    continue;
                bool matchesNone = true;
                for (int tag = 0; tag < grant.TriggerNone.Count; tag++)
                    matchesNone &= !isTagOrParent(request.TriggerTag, grant.TriggerNone[tag]);
                if (matchesNone)
                    matches.Add(request.RequestId);
            }
            return matches.ToArray();
        }

        public void RequireRequests(IEnumerable<string> requiredRequests)
        {
            var known = new HashSet<string>(InputRequestIds, StringComparer.Ordinal);
            foreach (string request in requiredRequests)
                if (!known.Contains(request))
                    throw new InvalidOperationException($"角色输入绑定缺少技能使用的请求'{request}'。");
        }
    }
}
