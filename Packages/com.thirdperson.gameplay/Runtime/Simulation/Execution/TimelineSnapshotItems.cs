using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public interface IAbilityTimelineSnapshotContentLifetime
    {
        void Retain();
        void Release();
    }

    [ValueBorrowedSequence(typeof(TimelineSnapshotItemBuffer<>))]
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
        public int Capacity => m_Items.Length;
        public TimelineSnapshotItems<T> Items => new TimelineSnapshotItems<T>(this, 0, m_Count);
        internal uint Generation => m_Storage.Generation;
        internal void Retain(uint generation) => m_Storage.Retain(generation);
        internal void Release() => m_Storage.Release();
        internal ReadOnlySpan<T> Read(uint generation, int start, int count)
        {
            m_Storage.RequireGeneration(generation);
            return m_Items.AsSpan(start, count);
        }

        public void AddPrepared(T value)
        {
            if (m_Count == m_Items.Length)
                throw new InvalidOperationException("Timeline snapshot items exceed the prepared payload capacity.");
            m_RetainItem(value);
            m_Items[m_Count++] = value;
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

    [ValueTextCapacity(typeof(AbilityTimelineRuntimeSnapshot))]
    [ValueOperations(ExplicitResources = true, RetainMethod = "RetainText", ReleaseMethod = "ReleaseText")]
    public sealed partial class AbilityTimelineSnapshotStorage
    {
        IAbilityTimelineSnapshotContentLifetime m_ContentLifetime;

        internal AbilityTimelineSnapshotStorage(int clips, int exits, int pendingExits, int trees)
        {
            ActiveClipIds = new TimelineSnapshotItemBuffer<CanonicalTextValue>(this, clips, value => value.Retain(), value => value.Release());
            TreeDecisionExits = new TimelineSnapshotItemBuffer<AbilityTimelineTreeClipExit>(this, exits, value => value.Retain(), value => value.Release());
            PendingTreeDecisionExits = new TimelineSnapshotItemBuffer<AbilityTimelineTreeClipExit>(this, pendingExits, value => value.Retain(), value => value.Release());
            ActiveTreeClips = new TimelineSnapshotItemBuffer<AbilityTimelineTreeClipState>(this, trees, value => value.Retain(), value => value.Release());
        }

        [ValueResource("RetainItems", "ReleaseItems")]
        public TimelineSnapshotItemBuffer<CanonicalTextValue> ActiveClipIds { get; }
        [ValueResource("RetainItems", "ReleaseItems")]
        public TimelineSnapshotItemBuffer<AbilityTimelineTreeClipExit> TreeDecisionExits { get; }
        [ValueResource("RetainItems", "ReleaseItems")]
        public TimelineSnapshotItemBuffer<AbilityTimelineTreeClipExit> PendingTreeDecisionExits { get; }
        [ValueResource("RetainItems", "ReleaseItems")]
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

        public AbilityTimelineRuntimeSnapshot CompleteRead(in AbilityTimelineStartRequest request,
            in BTSMTL.Timeline.TimelinePlaybackState playback, IAbilityTimelineSnapshotContentLifetime contentLifetime)
        {
            var snapshot = new AbilityTimelineRuntimeSnapshot(request, playback, ActiveTreeClips.Items);
            m_ContentLifetime = contentLifetime;
            m_ContentLifetime?.Retain();
            snapshot.RetainStateStorage();
            Release();
            return snapshot;
        }

        public void DiscardPreparedRead() => Release();

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

    public sealed class AbilityTimelineSnapshotStorageBank
    {
        readonly List<AbilityTimelineSnapshotStorage> m_Pages = new List<AbilityTimelineSnapshotStorage>();
        readonly int m_Clips;
        readonly int m_Exits;
        readonly int m_PendingExits;
        readonly int m_Trees;
        AbilityTimelineSnapshotStorage m_Current;
        int m_NextPage;

        public IAbilityTimelineSnapshotContentLifetime ContentLifetime { private get; set; }
        public int ActiveClipCapacity => m_Clips;
        public int TreeDecisionExitCapacity => m_Exits;
        public int PendingTreeDecisionExitCapacity => m_PendingExits;
        public int ActiveTreeClipCapacity => m_Trees;
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

        public AbilityTimelineSnapshotStorage BeginPreparedRead()
        {
            AbilityTimelineSnapshotStorage page = RentPage();
            page.BeginWrite(null);
            return page;
        }

        AbilityTimelineSnapshotStorage RentPage()
        {
            for (int visited = 0; visited < m_Pages.Count; visited++)
            {
                AbilityTimelineSnapshotStorage page = m_Pages[m_NextPage];
                if (++m_NextPage == m_Pages.Count)
                    m_NextPage = 0;
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
