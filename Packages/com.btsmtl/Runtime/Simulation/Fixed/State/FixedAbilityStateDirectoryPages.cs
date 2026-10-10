using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedAbilityStateDirectoryBank
    {
        readonly List<FixedAbilityStateDirectoryPage> m_Pages = new List<FixedAbilityStateDirectoryPage>();
        readonly int m_AbilityCount;

        internal FixedAbilityStateDirectoryBank(int abilityCount)
        {
            m_AbilityCount = abilityCount;
            PrepareCapacity(0);
        }

        internal void PrepareCapacity(int capacity)
        {
            if (capacity <= m_Pages.Count)
                return;
            m_Pages.Capacity = capacity;
            while (m_Pages.Count < capacity)
                m_Pages.Add(new FixedAbilityStateDirectoryPage(this, m_AbilityCount));
        }

        internal FixedAbilityStateDirectoryReference Begin()
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                FixedAbilityStateDirectoryPage page = m_Pages[index];
                if (page.Retainers != 0)
                    continue;
                Array.Clear(page.Values, 0, page.Values.Length);
                page.Generation++;
                return new FixedAbilityStateDirectoryReference(page, page.Generation);
            }
            throw new InvalidOperationException("Fixed Ability directory capacity does not cover the active transaction.");
        }
    }

    internal sealed class FixedAbilityStateDirectoryPage
    {
        internal FixedAbilityStateDirectoryPage(FixedAbilityStateDirectoryBank bank, int abilityCount)
        {
            Bank = bank;
            Values = new FixedAbilityRuntimeState[abilityCount];
        }

        internal readonly FixedAbilityStateDirectoryBank Bank;
        internal readonly FixedAbilityRuntimeState[] Values;
        internal ulong Generation;
        internal int Retainers;
    }

    internal readonly struct FixedAbilityStateDirectoryReference
    {
        internal FixedAbilityStateDirectoryReference(FixedAbilityStateDirectoryPage page, ulong generation)
        {
            Page = page;
            Generation = generation;
        }

        internal FixedAbilityStateDirectoryPage Page { get; }
        internal ulong Generation { get; }
        internal FixedAbilityRuntimeState[] Values
        {
            get
            {
                RequireReadable();
                return Page.Values;
            }
        }

        internal void Retain()
        {
            RequireReadable();
            Page.Retainers++;
        }

        internal void Release() => Page.Retainers--;

        void RequireReadable()
        {
            if (Generation != Page.Generation)
                throw new InvalidOperationException("Fixed Ability directory is outside its retained lifetime.");
        }
    }
}
