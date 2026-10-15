using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationIdentityTable
    {
        public OrchestrationIdentityTable(ReadOnlyMemory<string> entries, StableHash identity)
        {
            Entries = entries;
            Identity = identity;
        }

        public ReadOnlyMemory<string> Entries { get; }
        public StableHash Identity { get; }
        public int Count => Entries.Length;

        public string Resolve(int identity) => identity == 0 ? string.Empty : Entries.Span[identity - 1];

        public int Bind(string text) => Bind(text.AsSpan());

        public int Bind(ReadOnlySpan<char> text)
        {
            if (TryBind(text, out int identity))
                return identity;
            throw new KeyNotFoundException($"身份文本 '{text.ToString()}' 未包含在已发布内容表 '{Identity}' 中。");
        }

        public bool TryBind(ReadOnlySpan<char> text, out int identity)
        {
            if (text.IsEmpty)
            {
                identity = 0;
                return true;
            }
            ReadOnlySpan<string> entries = Entries.Span;
            int low = 0;
            int high = entries.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int order = entries[middle].AsSpan().CompareTo(text, StringComparison.Ordinal);
                if (order < 0)
                    low = middle + 1;
                else if (order > 0)
                    high = middle - 1;
                else
                {
                    identity = middle + 1;
                    return true;
                }
            }
            identity = 0;
            return false;
        }
    }
}
