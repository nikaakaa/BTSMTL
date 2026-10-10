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

        internal AbilityStateValuePageBank(GameplayAbilityExecutionIdentity identity, int slotCount,
            Func<int, bool> ownsSlot, Action<TValue> retainValue, Action<TValue> releaseValue)
        {
            Identity = identity;
            RetainValue = retainValue;
            ReleaseValue = releaseValue;
            SlotCount = slotCount;
            m_Pages = new AbilityStateValuePage<TValue>[(slotCount + PageSize - 1) / PageSize][];
            var pageIndices = new List<int>(m_Pages.Length);
            for (int slot = 0; slot < slotCount; slot++)
            {
                if (!ownsSlot(slot))
                    continue;
                int page = slot / PageSize;
                if (pageIndices.Count == 0 || pageIndices[pageIndices.Count - 1] != page)
                    pageIndices.Add(page);
            }
            m_PageIndices = pageIndices.ToArray();
            PrepareCapacity(0);
        }

        internal Action<TValue> RetainValue { get; }
        internal Action<TValue> ReleaseValue { get; }
        internal GameplayAbilityExecutionIdentity Identity { get; }
        internal int SlotCount { get; }
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
        int m_Retainers;

        internal AbilityStateValuePageSet(AbilityStateValuePageBank<TValue> bank, int pageCount)
        {
            Bank = bank;
            m_Pages = new AbilityStateValuePage<TValue>[pageCount];
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
            Count = 0;
            Generation++;
        }

        internal void BeginWrite(AbilityStateValuePageSet<TValue> source)
        {
            Array.Copy(source.m_Pages, m_Pages, m_Pages.Length);
            Count = source.Count;
            Generation++;
        }

        internal bool TryGetValue(int slot, out TValue value)
        {
            AbilityStateValuePage<TValue> page = m_Pages[slot / AbilityStateValuePageBank<TValue>.PageSize];
            if (page == null)
            {
                value = default;
                return false;
            }
            int offset = slot % AbilityStateValuePageBank<TValue>.PageSize;
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
                slot = m_PageStart + m_Offset;
                value = m_PageValues[m_Offset++];
                m_RemainingStored >>= 1;
                return true;
            }
        }

        internal void Set(int slot, TValue value)
        {
            int pageIndex = slot / AbilityStateValuePageBank<TValue>.PageSize;
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
            Store(page, slot % AbilityStateValuePageBank<TValue>.PageSize, value);
        }

        internal void RestoreValue(int slot, TValue value) =>
            Store(m_Pages[slot / AbilityStateValuePageBank<TValue>.PageSize],
                slot % AbilityStateValuePageBank<TValue>.PageSize, value);

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
