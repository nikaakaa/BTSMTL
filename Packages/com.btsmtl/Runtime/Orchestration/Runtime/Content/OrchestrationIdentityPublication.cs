using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public static class OrchestrationIdentityPublication
    {
        public static OrchestrationIdentityTable Create(IEnumerable<string> identities)
        {
            var entries = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string identity in identities)
                if (!string.IsNullOrEmpty(identity))
                    entries.Add(identity);
            var values = new string[entries.Count];
            entries.CopyTo(values);
            return new OrchestrationIdentityTable(values, OrchestrationIdentityContentHash.Table(values));
        }
    }
}
