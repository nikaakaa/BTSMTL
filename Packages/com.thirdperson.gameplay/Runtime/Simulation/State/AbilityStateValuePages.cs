using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal sealed class AbilityStateValuePageBank<TValue> where TValue : struct
    {
        internal const int PageSize = 32;
        readonly List<AbilityStateValuePageSet<TValue>> m_Sets = new List<AbilityStateValuePageSet<TValue>>();
        readonly AbilityStateValuePage<TValue>[][] m_Pages;
        readonly int[] m_PageIndices;
        internal readonly int[] ValueIndices;
        internal readonly int[] LogicalSlots;
        internal readonly int[] BlackboardBlocks;
        internal readonly int[] BlackboardFields;
        internal readonly GeneratedBlackboardBlock<TValue>[] BlackboardBindings;
        readonly GeneratedBlackboardPage<TValue>[][] m_BlackboardPages;

        internal AbilityStateValuePageBank(GameplayAbilityExecutionIdentity identity, int slotCount,
            Func<int, bool> ownsSlot, Action<TValue> retainValue, Action<TValue> releaseValue,
            GeneratedBlackboardStateBinding<TValue> blackboards)
        {
            Identity = identity;
            RetainValue = retainValue;
            ReleaseValue = releaseValue;
            Blackboards = blackboards;
            ValueIndices = new int[slotCount];
            BlackboardBlocks = new int[slotCount];
            BlackboardFields = new int[slotCount];
            Array.Fill(ValueIndices, -1);
            Array.Fill(BlackboardBlocks, -1);
            var logicalSlots = new List<int>();
            var bindings = new List<GeneratedBlackboardBlock<TValue>>();
            var blocks = new Dictionary<int, int>();
            for (int slot = 0; slot < slotCount; slot++)
            {
                if (!ownsSlot(slot))
                    continue;
                if (blackboards.Contains(slot))
                {
                    int source = blackboards.SlotBlocks[slot];
                    if (!blocks.TryGetValue(source, out int block))
                    {
                        block = bindings.Count;
                        blocks.Add(source, block);
                        bindings.Add(blackboards.Blocks[source]);
                    }
                    BlackboardBlocks[slot] = block;
                    BlackboardFields[slot] = blackboards.SlotFields[slot];
                }
                else
                {
                    ValueIndices[slot] = logicalSlots.Count;
                    logicalSlots.Add(slot);
                }
            }
            LogicalSlots = logicalSlots.ToArray();
            BlackboardBindings = bindings.ToArray();
            m_Pages = new AbilityStateValuePage<TValue>[(LogicalSlots.Length + PageSize - 1) / PageSize][];
            m_PageIndices = new int[m_Pages.Length];
            for (int index = 0; index < m_PageIndices.Length; index++)
                m_PageIndices[index] = index;
            m_BlackboardPages = new GeneratedBlackboardPage<TValue>[bindings.Count][];
            PrepareCapacity(0);
        }

        internal Action<TValue> RetainValue { get; }
        internal Action<TValue> ReleaseValue { get; }
        internal GameplayAbilityExecutionIdentity Identity { get; }
        internal GeneratedBlackboardStateBinding<TValue> Blackboards { get; }
        internal ReadOnlySpan<int> PageIndices => m_PageIndices;

        internal void PrepareCapacity(int capacity)
        {
            if (capacity <= m_Sets.Count)
                return;
            for (int page = 0; page < m_PageIndices.Length; page++)
            {
                int pageIndex = m_PageIndices[page];
                var pages = new AbilityStateValuePage<TValue>[capacity];
                int previousCount = m_Sets.Count;
                if (previousCount != 0)
                    Array.Copy(m_Pages[pageIndex], pages, previousCount);
                for (int index = previousCount; index < capacity; index++)
                    pages[index] = new AbilityStateValuePage<TValue>();
                m_Pages[pageIndex] = pages;
            }
            for (int block = 0; block < m_BlackboardPages.Length; block++)
            {
                var pages = new GeneratedBlackboardPage<TValue>[capacity];
                if (m_Sets.Count != 0)
                    Array.Copy(m_BlackboardPages[block], pages, m_Sets.Count);
                for (int index = m_Sets.Count; index < capacity; index++)
                    pages[index] = new GeneratedBlackboardPage<TValue>(BlackboardBindings[block].Contract.Create());
                m_BlackboardPages[block] = pages;
            }
            m_Sets.Capacity = capacity;
            while (m_Sets.Count < capacity)
                m_Sets.Add(new AbilityStateValuePageSet<TValue>(this, m_Pages.Length));
        }

        internal AbilityStateValuePageReference<TValue> BeginEmpty()
        {
            AbilityStateValuePageSet<TValue> set = RentSet(null);
            set.BeginEmpty();
            return new AbilityStateValuePageReference<TValue>(set, set.Generation);
        }

        internal AbilityStateValuePageReference<TValue> BeginWrite(in AbilityStateValuePageReference<TValue> source)
        {
            source.RequireReadable();
            AbilityStateValuePageSet<TValue> set = RentSet(source.Set);
            set.BeginWrite(source.Set);
            var reference = new AbilityStateValuePageReference<TValue>(set, set.Generation);
            reference.Retain();
            return reference;
        }

        AbilityStateValuePageSet<TValue> RentSet(AbilityStateValuePageSet<TValue> source)
        {
            for (int index = 0; index < m_Sets.Count; index++)
                if (!m_Sets[index].IsRetained && !ReferenceEquals(m_Sets[index], source))
                    return m_Sets[index];
            throw new InvalidOperationException("Ability state page capacity does not cover the active transaction.");
        }

        internal AbilityStateValuePage<TValue> RentPage(int pageIndex)
        {
            AbilityStateValuePage<TValue>[] pages = m_Pages[pageIndex];
            for (int index = 0; index < pages.Length; index++)
                if (pages[index].Retainers == 0)
                    return pages[index];
            throw new InvalidOperationException("Ability state value page capacity does not cover the active transaction.");
        }

        internal GeneratedBlackboardPage<TValue> RentBlackboardPage(int block)
        {
            GeneratedBlackboardPage<TValue>[] pages = m_BlackboardPages[block];
            for (int index = 0; index < pages.Length; index++)
                if (pages[index].Retainers == 0)
                    return pages[index];
            throw new InvalidOperationException("Generated blackboard page capacity does not cover the active transaction.");
        }
    }

    internal sealed class GeneratedBlackboardPage<TValue> where TValue : struct
    {
        internal GeneratedBlackboardPage(IGeneratedBlackboardValues<TValue> values) => Values = values;
        internal readonly IGeneratedBlackboardValues<TValue> Values;
        internal uint Stored;
        internal int Retainers;
    }

    internal sealed class AbilityStateValuePage<TValue> where TValue : struct
    {
        internal readonly TValue[] Values = new TValue[AbilityStateValuePageBank<TValue>.PageSize];
        internal uint Stored;
        internal int Retainers;
    }

    internal sealed class AbilityStateValuePageSet<TValue> where TValue : struct
    {
        readonly AbilityStateValuePage<TValue>[] m_Pages;
        readonly GeneratedBlackboardPage<TValue>[] m_BlackboardPages;
        int m_Retainers;

        internal AbilityStateValuePageSet(AbilityStateValuePageBank<TValue> bank, int pageCount)
        {
            Bank = bank;
            m_Pages = new AbilityStateValuePage<TValue>[pageCount];
            m_BlackboardPages = new GeneratedBlackboardPage<TValue>[bank.BlackboardBindings.Length];
        }

        internal AbilityStateValuePageBank<TValue> Bank { get; }
        internal ulong Generation { get; private set; }
        internal bool IsRetained => m_Retainers != 0;
        internal int Count { get; private set; }

        internal void BeginEmpty()
        {
            ReadOnlySpan<int> pageIndices = Bank.PageIndices;
            for (int pageIndex = 0; pageIndex < pageIndices.Length; pageIndex++)
            {
                int index = pageIndices[pageIndex];
                AbilityStateValuePage<TValue> page = Bank.RentPage(index);
                Array.Clear(page.Values, 0, page.Values.Length);
                page.Stored = 0;
                m_Pages[index] = page;
            }
            for (int block = 0; block < m_BlackboardPages.Length; block++)
            {
                GeneratedBlackboardPage<TValue> page = Bank.RentBlackboardPage(block);
                page.Values.Clear();
                page.Stored = 0;
                m_BlackboardPages[block] = page;
            }
            Count = 0;
            Generation++;
        }

        internal void BeginWrite(AbilityStateValuePageSet<TValue> source)
        {
            Array.Copy(source.m_Pages, m_Pages, m_Pages.Length);
            Array.Copy(source.m_BlackboardPages, m_BlackboardPages, m_BlackboardPages.Length);
            Count = source.Count;
            Generation++;
        }

        internal bool SharesValuesWith(AbilityStateValuePageSet<TValue> other)
        {
            for (int index = 0; index < m_Pages.Length; index++)
                if (!ReferenceEquals(m_Pages[index], other.m_Pages[index]))
                    return false;
            for (int index = 0; index < m_BlackboardPages.Length; index++)
                if (!ReferenceEquals(m_BlackboardPages[index], other.m_BlackboardPages[index]))
                    return false;
            return true;
        }

        internal bool TryGetValue(int slot, out TValue value)
        {
            int block = Bank.BlackboardBlocks[slot];
            if (block >= 0)
            {
                GeneratedBlackboardPage<TValue> blackboard = m_BlackboardPages[block];
                int field = Bank.BlackboardFields[slot];
                value = blackboard.Values.Read(field);
                return (blackboard.Stored & (1u << field)) != 0;
            }
            int storedSlot = Bank.ValueIndices[slot];
            if (storedSlot < 0)
            {
                value = default;
                return false;
            }
            AbilityStateValuePage<TValue> page = m_Pages[storedSlot / AbilityStateValuePageBank<TValue>.PageSize];
            int offset = storedSlot % AbilityStateValuePageBank<TValue>.PageSize;
            value = page.Values[offset];
            return (page.Stored & (1u << offset)) != 0;
        }

        internal struct Enumerator
        {
            readonly AbilityStateValuePageSet<TValue> m_Set;
            TValue[] m_PageValues;
            int m_PageCursor;
            int m_PageStart;
            int m_Offset;
            uint m_RemainingStored;

            internal Enumerator(AbilityStateValuePageSet<TValue> set)
            {
                m_Set = set;
                m_PageValues = null;
                m_PageCursor = 0;
                m_PageStart = 0;
                m_Offset = 0;
                m_RemainingStored = 0;
            }

            internal bool MoveNext(out int slot, out TValue value)
            {
                ReadOnlySpan<int> pageIndices = m_Set.Bank.PageIndices;
                while (m_RemainingStored == 0)
                {
                    if (m_PageCursor == pageIndices.Length)
                    {
                        slot = -1;
                        value = default;
                        return false;
                    }
                    int pageIndex = pageIndices[m_PageCursor++];
                    AbilityStateValuePage<TValue> page = m_Set.m_Pages[pageIndex];
                    m_PageValues = page.Values;
                    m_PageStart = pageIndex * AbilityStateValuePageBank<TValue>.PageSize;
                    m_Offset = 0;
                    m_RemainingStored = page.Stored;
                }
                while ((m_RemainingStored & 1) == 0)
                {
                    m_RemainingStored >>= 1;
                    m_Offset++;
                }
                slot = m_Set.Bank.LogicalSlots[m_PageStart + m_Offset];
                value = m_PageValues[m_Offset++];
                m_RemainingStored >>= 1;
                return true;
            }
        }

        internal void Set(int slot, TValue value)
        {
            int block = Bank.BlackboardBlocks[slot];
            if (block >= 0)
            {
                GeneratedBlackboardPage<TValue> blackboard = m_BlackboardPages[block];
                if (blackboard.Retainers > 1)
                {
                    GeneratedBlackboardPage<TValue> writable = Bank.RentBlackboardPage(block);
                    blackboard.Values.CopyTo(writable.Values);
                    writable.Stored = blackboard.Stored;
                    blackboard.Retainers--;
                    writable.Retainers++;
                    writable.Values.Retain();
                    m_BlackboardPages[block] = blackboard = writable;
                }
                StoreBlackboard(blackboard, Bank.BlackboardFields[slot], value);
                return;
            }
            int storedSlot = Bank.ValueIndices[slot];
            int pageIndex = storedSlot / AbilityStateValuePageBank<TValue>.PageSize;
            AbilityStateValuePage<TValue> page = m_Pages[pageIndex];
            if (page.Retainers > 1)
            {
                AbilityStateValuePage<TValue> writable = Bank.RentPage(pageIndex);
                Array.Copy(page.Values, writable.Values, page.Values.Length);
                writable.Stored = page.Stored;
                page.Retainers--;
                writable.Retainers++;
                RetainValues(writable);
                m_Pages[pageIndex] = page = writable;
            }
            Store(page, storedSlot % AbilityStateValuePageBank<TValue>.PageSize, value);
        }

        internal void RestoreValue(int slot, TValue value)
        {
            int block = Bank.BlackboardBlocks[slot];
            if (block >= 0)
                StoreBlackboard(m_BlackboardPages[block], Bank.BlackboardFields[slot], value);
            else
            {
                int storedSlot = Bank.ValueIndices[slot];
                Store(m_Pages[storedSlot / AbilityStateValuePageBank<TValue>.PageSize],
                    storedSlot % AbilityStateValuePageBank<TValue>.PageSize, value);
            }
        }

        void StoreBlackboard(GeneratedBlackboardPage<TValue> page, int field, TValue value)
        {
            uint mask = 1u << field;
            if (page.Retainers != 0)
            {
                Bank.RetainValue(value);
                if ((page.Stored & mask) != 0)
                    Bank.ReleaseValue(page.Values.Read(field));
            }
            page.Values.Write(field, value);
            page.Stored |= mask;
        }

        void Store(AbilityStateValuePage<TValue> page, int offset, TValue value)
        {
            uint mask = 1u << offset;
            if ((page.Stored & mask) == 0)
                Count++;
            if (page.Retainers != 0)
            {
                Bank.RetainValue(value);
                if ((page.Stored & mask) != 0)
                    Bank.ReleaseValue(page.Values[offset]);
            }
            page.Stored |= mask;
            page.Values[offset] = value;
        }

        internal void Retain()
        {
            if (m_Retainers++ != 0)
                return;
            ReadOnlySpan<int> pageIndices = Bank.PageIndices;
            for (int index = 0; index < pageIndices.Length; index++)
            {
                AbilityStateValuePage<TValue> page = m_Pages[pageIndices[index]];
                if (page.Retainers++ == 0)
                    RetainValues(page);
            }
            for (int block = 0; block < m_BlackboardPages.Length; block++)
                if (m_BlackboardPages[block].Retainers++ == 0)
                    m_BlackboardPages[block].Values.Retain();
        }

        internal void Release()
        {
            if (--m_Retainers != 0)
                return;
            ReadOnlySpan<int> pageIndices = Bank.PageIndices;
            for (int index = 0; index < pageIndices.Length; index++)
            {
                AbilityStateValuePage<TValue> page = m_Pages[pageIndices[index]];
                if (--page.Retainers == 0)
                    ReleaseValues(page);
            }
            for (int block = 0; block < m_BlackboardPages.Length; block++)
                if (--m_BlackboardPages[block].Retainers == 0)
                    m_BlackboardPages[block].Values.Release();
        }

        void RetainValues(AbilityStateValuePage<TValue> page)
        {
            uint stored = page.Stored;
            for (int offset = 0; stored != 0; offset++, stored >>= 1)
                if ((stored & 1) != 0)
                    Bank.RetainValue(page.Values[offset]);
        }

        void ReleaseValues(AbilityStateValuePage<TValue> page)
        {
            uint stored = page.Stored;
            for (int offset = 0; stored != 0; offset++, stored >>= 1)
                if ((stored & 1) != 0)
                    Bank.ReleaseValue(page.Values[offset]);
        }

        internal void RequireReadable(ulong generation)
        {
            if (Generation != generation)
                throw new InvalidOperationException("Ability state page lease has expired. Retained history must use Snapshot/Capture.");
        }
    }

    internal readonly struct AbilityStateValuePageReference<TValue> where TValue : struct
    {
        internal AbilityStateValuePageReference(AbilityStateValuePageSet<TValue> set, ulong generation)
        {
            Set = set;
            Generation = generation;
        }

        internal AbilityStateValuePageSet<TValue> Set { get; }
        internal ulong Generation { get; }
        internal void RequireReadable() => Set.RequireReadable(Generation);

        internal bool TryGetValue(int slot, out TValue value)
        {
            RequireReadable();
            return Set.TryGetValue(slot, out value);
        }

        internal AbilityStateValuePageSet<TValue>.Enumerator GetEnumerator()
        {
            RequireReadable();
            return new AbilityStateValuePageSet<TValue>.Enumerator(Set);
        }

        internal void Retain()
        {
            RequireReadable();
            Set.Retain();
        }

        internal void Release() => Set.Release();
    }
}
