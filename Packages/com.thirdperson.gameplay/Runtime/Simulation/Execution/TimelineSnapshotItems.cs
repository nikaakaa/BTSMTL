using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public interface IAbilityTimelineSnapshotContentLifetime
    {
        void Retain();
        void Release();
    }

    [CanonicalBorrowedSequence(typeof(TimelineSnapshotItemBuffer<>))]
    public readonly struct TimelineSnapshotItems<T>
    {
        readonly object m_Items;
        readonly uint m_Generation;
        readonly int m_Start;
        readonly int m_Count;

        TimelineSnapshotItems(T[] items)
        {
            m_Items = items;
            m_Generation = 0;
            m_Start = 0;
            m_Count = items.Length;
        }

        internal TimelineSnapshotItems(TimelineSnapshotItemBuffer<T> buffer, int start, int count)
        {
            m_Items = buffer;
            m_Generation = buffer.Generation;
            m_Start = start;
            m_Count = count;
        }

        public int Count => Span.Length;
        public T this[int index] => Span[index];
        public ReadOnlySpan<T> Span => m_Items is TimelineSnapshotItemBuffer<T> buffer
            ? buffer.Read(m_Generation, m_Start, m_Count) : (T[])m_Items;

        internal void RetainStateStorage()
        {
            if (m_Items is TimelineSnapshotItemBuffer<T> buffer)
                buffer.Retain(m_Generation);
        }

        internal void ReleaseStateStorage()
        {
            if (m_Items is TimelineSnapshotItemBuffer<T> buffer)
                buffer.Release();
        }

        public static TimelineSnapshotItems<T> CopyFrom(IReadOnlyList<T> source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.Count == 0)
                return default;
            var items = new T[source.Count];
            for (int index = 0; index < items.Length; index++)
                items[index] = source[index];
            return new TimelineSnapshotItems<T>(items);
        }
    }

    public sealed class TimelineSnapshotItemBuffer<T>
    {
        readonly AbilityTimelineSnapshotStorage m_Storage;
        readonly T[] m_Items;
        readonly Action<T> m_RetainItem;
        readonly Action<T> m_ReleaseItem;
        int m_Count;

        internal TimelineSnapshotItemBuffer(AbilityTimelineSnapshotStorage storage, int capacity,
            Action<T> retainItem, Action<T> releaseItem)
        {
            m_Storage = storage;
            m_Items = new T[capacity];
            m_RetainItem = retainItem;
            m_ReleaseItem = releaseItem;
        }

        internal int Count => m_Count;
        internal uint Generation => m_Storage.Generation;
        internal void Retain(uint generation) => m_Storage.Retain(generation);
        internal void Release() => m_Storage.Release();
        internal ReadOnlySpan<T> Read(uint generation, int start, int count)
        {
            m_Storage.RequireGeneration(generation);
            return m_Items.AsSpan(start, count);
        }

        internal Span<T> Reserve(int count, out TimelineSnapshotItems<T> items)
        {
            int start = m_Count;
            if (count < 0 || count > m_Items.Length - start)
                throw new InvalidOperationException("Timeline snapshot items exceed the prepared payload capacity.");
            m_Count += count;
            items = new TimelineSnapshotItems<T>(this, start, count);
            return m_Items.AsSpan(start, count);
        }

        public TimelineSnapshotItems<T> Capture(IReadOnlyList<T> source)
        {
            int count = source.Count;
            if (count > m_Items.Length)
                throw new InvalidOperationException("Timeline snapshot items exceed the published content capacity.");
            if (m_Storage.Retainers != 0)
            {
                for (int index = 0; index < count; index++)
                    m_RetainItem(source[index]);
                for (int index = 0; index < m_Count; index++)
                    m_ReleaseItem(m_Items[index]);
            }
            for (int index = 0; index < count; index++)
                m_Items[index] = source[index];
            m_Count = count;
            return new TimelineSnapshotItems<T>(this, 0, count);
        }

        internal void Clear()
        {
            Array.Clear(m_Items, 0, m_Count);
            m_Count = 0;
        }

        internal void RetainItems()
        {
            for (int index = 0; index < m_Count; index++)
                m_RetainItem(m_Items[index]);
        }

        internal void ReleaseItems()
        {
            for (int index = 0; index < m_Count; index++)
                m_ReleaseItem(m_Items[index]);
        }
    }

    public sealed class AbilityTimelineSnapshotStorage
    {
        IAbilityTimelineSnapshotContentLifetime m_ContentLifetime;

        internal AbilityTimelineSnapshotStorage(int clips, int exits, int pendingExits, int trees)
        {
            ActiveClipIds = new TimelineSnapshotItemBuffer<CanonicalTextValue>(this, clips, value => value.Retain(), value => value.Release());
            TreeDecisionExits = new TimelineSnapshotItemBuffer<AbilityTimelineTreeClipExit>(this, exits, value => value.Retain(), value => value.Release());
            PendingTreeDecisionExits = new TimelineSnapshotItemBuffer<AbilityTimelineTreeClipExit>(this, pendingExits, value => value.Retain(), value => value.Release());
            ActiveTreeClips = new TimelineSnapshotItemBuffer<AbilityTimelineTreeClipState>(this, trees, value => value.Retain(), value => value.Release());
        }

        public TimelineSnapshotItemBuffer<CanonicalTextValue> ActiveClipIds { get; }
        public TimelineSnapshotItemBuffer<AbilityTimelineTreeClipExit> TreeDecisionExits { get; }
        public TimelineSnapshotItemBuffer<AbilityTimelineTreeClipExit> PendingTreeDecisionExits { get; }
        public TimelineSnapshotItemBuffer<AbilityTimelineTreeClipState> ActiveTreeClips { get; }
        internal uint Generation { get; private set; }
        internal int Retainers { get; private set; }

        internal void BeginWrite(IAbilityTimelineSnapshotContentLifetime contentLifetime)
        {
            Generation++;
            Retainers = 1;
            m_ContentLifetime = contentLifetime;
            m_ContentLifetime?.Retain();
        }

        internal void BeginDecode()
        {
            ActiveClipIds.Clear();
            TreeDecisionExits.Clear();
            PendingTreeDecisionExits.Clear();
            ActiveTreeClips.Clear();
            Generation++;
            Retainers = 0;
        }

        internal void RequireGeneration(uint generation)
        {
            if (Generation != generation)
                throw new InvalidOperationException("Timeline snapshot storage has already been reused.");
        }

        internal void Retain(uint generation)
        {
            RequireGeneration(generation);
            if (Retainers++ == 0)
                RetainText();
        }

        void RetainText()
        {
            AbilityTimelineRuntimeSnapshot.CanonicalRetainStorageItems(this);
        }

        void ReleaseText()
        {
            AbilityTimelineRuntimeSnapshot.CanonicalReleaseStorageItems(this);
        }

        internal void Release()
        {
            if (--Retainers != 0)
                return;
            ReleaseText();
            ActiveClipIds.Clear();
            TreeDecisionExits.Clear();
            PendingTreeDecisionExits.Clear();
            ActiveTreeClips.Clear();
            IAbilityTimelineSnapshotContentLifetime contentLifetime = m_ContentLifetime;
            m_ContentLifetime = null;
            contentLifetime?.Release();
        }
    }

    internal readonly struct AbilityTimelineSnapshotDecodeLayout
    {
        internal AbilityTimelineSnapshotDecodeLayout(int maximumSnapshots, int maximumPayloadBytes,
            int clips, int exits, int pendingExits, int trees)
        {
            MaximumSnapshots = maximumSnapshots;
            MaximumPayloadBytes = maximumPayloadBytes;
            ActiveClips = (int)Math.Min((long)maximumSnapshots * clips, maximumPayloadBytes / sizeof(int));
            TreeDecisionExits = (int)Math.Min((long)maximumSnapshots * exits, maximumPayloadBytes / (sizeof(int) * 2));
            PendingTreeDecisionExits = (int)Math.Min((long)maximumSnapshots * pendingExits, maximumPayloadBytes / (sizeof(int) * 2));
            ActiveTreeClips = (int)Math.Min((long)maximumSnapshots * trees,
                maximumPayloadBytes / (sizeof(int) * 5 + sizeof(ulong) * 2));
        }

        internal readonly int MaximumSnapshots;
        internal readonly int MaximumPayloadBytes;
        internal readonly int ActiveClips;
        internal readonly int TreeDecisionExits;
        internal readonly int PendingTreeDecisionExits;
        internal readonly int ActiveTreeClips;

        internal long ScalarTextValues(int declaredIdentityCount) =>
            (long)MaximumSnapshots * 12 + Math.Min((long)MaximumSnapshots * 2, declaredIdentityCount) + ActiveClips;
        internal long ExitTextValues => (long)TreeDecisionExits + PendingTreeDecisionExits;
        internal long TreeTextValues => (long)ActiveTreeClips * 4;
        internal int MaximumTextValues => SimulationSessionCheckpointPayloadLayout.MaximumNonEmptyTextValues(
            MaximumPayloadBytes, (long)MaximumSnapshots * 14 + ActiveClips, ExitTextValues, TreeTextValues);
    }

    public sealed class AbilityTimelineSnapshotStorageBank
    {
        readonly List<AbilityTimelineSnapshotStorage> m_Pages = new List<AbilityTimelineSnapshotStorage>();
        readonly int m_Clips;
        readonly int m_Exits;
        readonly int m_PendingExits;
        readonly int m_Trees;
        AbilityTimelineSnapshotStorage m_Current;

        public IAbilityTimelineSnapshotContentLifetime ContentLifetime { private get; set; }
        public int ActiveClipCapacity => m_Clips;
        public int TreeDecisionExitCapacity => m_Exits;
        public int PendingTreeDecisionExitCapacity => m_PendingExits;
        public int ActiveTreeClipCapacity => m_Trees;
        public int MaximumTextValues => TextValueCapacity(m_Clips, m_Exits, m_PendingExits, m_Trees);
        public static int TextValueCapacity(int clips, int exits, int pendingExits, int trees) =>
            checked(14 + clips + exits + pendingExits + 4 * trees);

        public AbilityTimelineSnapshotStorageBank(int clips, int exits, int pendingExits, int trees)
        {
            m_Clips = clips;
            m_Exits = exits;
            m_PendingExits = pendingExits;
            m_Trees = trees;
            PrepareCapacity(2);
        }

        public void PrepareCapacity(int count)
        {
            m_Pages.Capacity = Math.Max(m_Pages.Capacity, count);
            while (m_Pages.Count < count)
                m_Pages.Add(new AbilityTimelineSnapshotStorage(m_Clips, m_Exits, m_PendingExits, m_Trees));
        }

        public AbilityTimelineSnapshotStorage BeginWrite()
        {
            AbilityTimelineSnapshotStorage page = RentPage();
            page.BeginWrite(ContentLifetime);
            ReleaseCurrent();
            m_Current = page;
            return page;
        }

        internal AbilityTimelineSnapshotStorage BeginDecode()
        {
            AbilityTimelineSnapshotStorage page = RentPage();
            page.BeginDecode();
            return page;
        }

        AbilityTimelineSnapshotStorage RentPage()
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                AbilityTimelineSnapshotStorage page = m_Pages[index];
                if (page.Retainers != 0)
                    continue;
                return page;
            }
            throw new InvalidOperationException("Timeline snapshot page capacity does not cover retained state.");
        }

        public void ReleaseCurrent()
        {
            if (m_Current == null)
                return;
            m_Current.Release();
            m_Current = null;
        }
    }
}
