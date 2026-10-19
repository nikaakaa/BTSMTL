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
            if (additions.Count == 0)
                return baseline;
            var values = new string[baseline.Count + additions.Count];
            baseline.Entries.Span.CopyTo(values);
            additions.CopyTo(values, baseline.Count);
            var sortedIdentityIds = new int[values.Length];
            ReadOnlySpan<int> baselineOrder = baseline.SortedIdentityIds.Span;
            int original = 0;
            int added = baseline.Count;
            int written = 0;
            while (original < baselineOrder.Length && added < values.Length)
            {
                if (string.CompareOrdinal(values[baselineOrder[original] - 1], values[added]) < 0)
                    sortedIdentityIds[written++] = baselineOrder[original++];
                else
                {
                    sortedIdentityIds[written++] = added + 1;
                    added++;
                }
            }
            baselineOrder.Slice(original).CopyTo(sortedIdentityIds.AsSpan(written));
            written += baselineOrder.Length - original;
            while (added < values.Length)
            {
                sortedIdentityIds[written++] = added + 1;
                added++;
            }
            return new OrchestrationIdentityTable(values, sortedIdentityIds,
                () => OrchestrationIdentityContentHash.Table(values));
        }
    }
}
