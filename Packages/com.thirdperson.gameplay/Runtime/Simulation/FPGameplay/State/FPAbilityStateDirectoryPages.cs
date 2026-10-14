using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPAbilityStateDirectoryBank
    {
        readonly List<FPAbilityStateDirectoryPage> m_Pages = new List<FPAbilityStateDirectoryPage>();
        readonly int m_AbilityCount;

        internal FPAbilityStateDirectoryBank(int abilityCount)
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
                m_Pages.Add(new FPAbilityStateDirectoryPage(this, m_AbilityCount));
        }

        internal FPAbilityStateDirectoryReference Begin()
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                FPAbilityStateDirectoryPage page = m_Pages[index];
                if (page.Retainers != 0)
                    continue;
                Array.Clear(page.Values, 0, page.Values.Length);
                page.Generation++;
                return new FPAbilityStateDirectoryReference(page, page.Generation);
            }
            throw new InvalidOperationException("FP Ability directory capacity does not cover the active transaction.");
        }
    }

    internal sealed class FPAbilityStateDirectoryPage
    {
        internal FPAbilityStateDirectoryPage(FPAbilityStateDirectoryBank bank, int abilityCount)
        {
            Bank = bank;
            Values = new FPAbilityRuntimeState[abilityCount];
        }

        internal readonly FPAbilityStateDirectoryBank Bank;
        internal readonly FPAbilityRuntimeState[] Values;
        internal ulong Generation;
        internal int Retainers;
    }

    internal readonly struct FPAbilityStateDirectoryReference
    {
        internal FPAbilityStateDirectoryReference(FPAbilityStateDirectoryPage page, ulong generation)
        {
            Page = page;
            Generation = generation;
        }

        internal FPAbilityStateDirectoryPage Page { get; }
        internal ulong Generation { get; }
        internal FPAbilityRuntimeState[] Values
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
                throw new InvalidOperationException("FP Ability directory is outside its retained lifetime.");
        }
    }
}
