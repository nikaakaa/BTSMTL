using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public static class OrchestrationIdentityPublication
    {
        public static OrchestrationIdentityTable Create(IEnumerable<string> identities,
            OrchestrationIdentityTable previous = null)
        {
            var entries = new HashSet<string>(StringComparer.Ordinal);
            foreach (string identity in identities)
                if (!string.IsNullOrEmpty(identity))
                    entries.Add(identity);
            var values = new string[entries.Count];
            entries.CopyTo(values);
            Array.Sort(values, StringComparer.Ordinal);
            if (previous != null && values.AsSpan().SequenceEqual(previous.Entries.Span))
                return previous;
            return new OrchestrationIdentityTable(values, OrchestrationIdentityContentHash.Table(values));
        }

        public static OrchestrationIdentityTable Extend(OrchestrationIdentityTable baseline,
            IEnumerable<string> identities)
        {
            var additions = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string identity in identities)
                if (!string.IsNullOrEmpty(identity) && !baseline.TryBind(identity.AsSpan(), out _))
                    additions.Add(identity);
            var values = new string[baseline.Count + additions.Count];
            baseline.Entries.Span.CopyTo(values);
            additions.CopyTo(values, baseline.Count);
            return new OrchestrationIdentityTable(values, OrchestrationIdentityContentHash.Table(values));
        }
    }
}
