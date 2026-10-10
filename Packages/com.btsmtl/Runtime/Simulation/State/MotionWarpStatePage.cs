using System;
using System.Collections.Generic;
using System.Linq;

namespace ThirdPersonSimulation
{
    internal sealed class MotionWarpStatePageLayout
    {
        readonly Dictionary<CharacterSkillId, int> m_PartitionByAbility;
        readonly int[][] m_Operations;
        readonly int[] m_Offsets;

        public MotionWarpStatePageLayout(IEnumerable<KeyValuePair<CharacterSkillId, IReadOnlyList<int>>> abilities)
        {
            KeyValuePair<CharacterSkillId, IReadOnlyList<int>>[] ordered = abilities
                .OrderBy(value => value.Key.Value, StringComparer.Ordinal).ToArray();
            m_PartitionByAbility = new Dictionary<CharacterSkillId, int>(ordered.Length);
            m_Operations = new int[ordered.Length][];
            m_Offsets = new int[ordered.Length];
            for (int index = 0; index < ordered.Length; index++)
            {
                m_PartitionByAbility.Add(ordered[index].Key, index);
                m_Offsets[index] = SlotCount;
                m_Operations[index] = ordered[index].Value.ToArray();
                Array.Sort(m_Operations[index]);
                SlotCount += m_Operations[index].Length;
            }
        }

        public int SlotCount { get; }
        public int Partition(CharacterSkillId ability) => m_PartitionByAbility[ability];
        public int Offset(int partition) => m_Offsets[partition];
        public IReadOnlyList<int> Operations(int partition) => m_Operations[partition];
        public int Slot(int partition, OperationHandle operation) =>
            m_Offsets[partition] + Array.BinarySearch(m_Operations[partition], operation.Value);
    }

    internal sealed class MotionWarpStatePageBank<TState> where TState : struct
    {
        readonly MotionWarpStatePageLayout m_Layout;
        readonly Action<TState> m_RetainValue;
        readonly Action<TState> m_ReleaseValue;
        readonly List<MotionWarpStatePage<TState>> m_Pages = new();

        public MotionWarpStatePageBank(MotionWarpStatePageLayout layout, Action<TState> retainValue, Action<TState> releaseValue)
        {
            m_Layout = layout;
            m_RetainValue = retainValue;
            m_ReleaseValue = releaseValue;
            m_Pages.Add(new MotionWarpStatePage<TState>(layout, retainValue, releaseValue));
            m_Pages.Add(new MotionWarpStatePage<TState>(layout, retainValue, releaseValue));
            m_Pages.Add(new MotionWarpStatePage<TState>(layout, retainValue, releaseValue));
        }

        public MotionWarpStatePageReference<TState> BeginEmpty() => Rent().BeginEmpty();

        public void PrepareCapacity(int count)
        {
            m_Pages.Capacity = Math.Max(m_Pages.Capacity, count);
            while (m_Pages.Count < count)
                m_Pages.Add(new MotionWarpStatePage<TState>(m_Layout, m_RetainValue, m_ReleaseValue));
        }

        public MotionWarpStatePageReference<TState> BeginWrite(in MotionWarpStatePageReference<TState> source) =>
            Rent(source.Page).BeginWrite(source);

        MotionWarpStatePage<TState> Rent(MotionWarpStatePage<TState> source = null)
        {
            for (int index = 0; index < m_Pages.Count; index++)
                if (!m_Pages[index].IsRetained && !ReferenceEquals(m_Pages[index], source))
                    return m_Pages[index];
            throw new InvalidOperationException("MotionWarp 状态页超出 Schedule 声明的已准备容量。");
        }
    }

    internal sealed class MotionWarpStatePage<TState> where TState : struct
    {
        readonly TState[] m_Values;
        readonly Action<TState> m_RetainValue;
        readonly Action<TState> m_ReleaseValue;
        ulong m_Generation;
        int m_Retainers;

        public MotionWarpStatePage(MotionWarpStatePageLayout layout, Action<TState> retainValue, Action<TState> releaseValue)
        {
            Layout = layout;
            m_RetainValue = retainValue;
            m_ReleaseValue = releaseValue;
            m_Values = new TState[layout.SlotCount];
        }

        public MotionWarpStatePageLayout Layout { get; }
        public bool IsRetained => m_Retainers != 0;
        public ref readonly TState At(int slot) => ref m_Values[slot];

        public void Set(int slot, in TState value)
        {
            if (m_Retainers != 0)
            {
                m_RetainValue(value);
                m_ReleaseValue(m_Values[slot]);
            }
            m_Values[slot] = value;
        }

        public MotionWarpStatePageReference<TState> BeginWrite(in MotionWarpStatePageReference<TState> source)
        {
            if (m_Retainers != 0)
                throw new InvalidOperationException("MotionWarp state page is still owned by a committed state or frame output.");
            source.RequireReadable();
            if (!ReferenceEquals(Layout, source.Page.Layout))
                throw new InvalidOperationException("MotionWarp state page belongs to a different prepared layout.");
            Array.Copy(source.Page.m_Values, m_Values, m_Values.Length);
            m_Generation = checked(m_Generation + 1);
            return new MotionWarpStatePageReference<TState>(this, m_Generation);
        }

        public MotionWarpStatePageReference<TState> BeginEmpty()
        {
            if (m_Retainers != 0)
                throw new InvalidOperationException("MotionWarp state page is still retained.");
            Array.Clear(m_Values, 0, m_Values.Length);
            m_Generation = checked(m_Generation + 1);
            return new MotionWarpStatePageReference<TState>(this, m_Generation);
        }

        internal void RequireReadable(ulong generation)
        {
            if (generation != m_Generation)
                throw new InvalidOperationException("MotionWarp state view has expired after its frame page was recycled.");
        }

        internal void Retain(ulong generation)
        {
            RequireReadable(generation);
            if (m_Retainers++ == 0)
                for (int index = 0; index < m_Values.Length; index++)
                    m_RetainValue(m_Values[index]);
        }

        internal void Release(ulong generation)
        {
            RequireReadable(generation);
            if (m_Retainers == 0)
                throw new InvalidOperationException("MotionWarp state page release has no owning lease.");
            if (--m_Retainers == 0)
                for (int index = 0; index < m_Values.Length; index++)
                    m_ReleaseValue(m_Values[index]);
        }
    }

    internal readonly struct MotionWarpStatePageReference<TState> where TState : struct
    {
        internal MotionWarpStatePageReference(MotionWarpStatePage<TState> page, ulong generation)
        {
            Page = page;
            Generation = generation;
        }

        internal MotionWarpStatePage<TState> Page { get; }
        internal ulong Generation { get; }
        public void RequireReadable() => Page.RequireReadable(Generation);
        public void Retain() => Page.Retain(Generation);
        public void Release() => Page.Release(Generation);
    }
}
