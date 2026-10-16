using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTSMTL.Diagnostics
{
    public readonly struct RuntimeGraphInvocation
    {
        public RuntimeGraphInvocation(string path, string graphId, string parentPath,
            RuntimeSourceElementKey caller, string callerClipId, string callerId)
        {
            Path = path;
            GraphId = graphId;
            ParentPath = parentPath ?? string.Empty;
            Caller = caller;
            CallerClipId = callerClipId ?? string.Empty;
            CallerId = callerId ?? string.Empty;
        }
        public string Path { get; }
        public string GraphId { get; }
        public string ParentPath { get; }
        public RuntimeSourceElementKey Caller { get; }
        public string CallerClipId { get; }
        public string CallerId { get; }
    }

    public readonly struct DebugSourceMapEntry
    {
        public DebugSourceMapEntry(
            RuntimeSourceElementHandle handle,
            RuntimeSourceElementKey source,
            RuntimeSourceElementHandle parent,
            string displayName,
            string contentHash,
            RuntimeSourceTarget target)
        {
            Handle = handle;
            Source = source;
            Parent = parent;
            DisplayName = displayName ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
            Target = target;
        }

        public RuntimeSourceElementHandle Handle { get; }
        public RuntimeSourceElementKey Source { get; }
        public RuntimeSourceElementHandle Parent { get; }
        public string DisplayName { get; }
        public string ContentHash { get; }
        public RuntimeSourceTarget Target { get; }
    }

    public interface IDebugSourceMap
    {
        RuntimeContentRevision Revision { get; }
        IReadOnlyList<DebugSourceMapEntry> Entries { get; }
        IReadOnlyList<RuntimeGraphInvocation> GraphInvocations { get; }
        int TimelineCount { get; }
        int TrackCount { get; }
        bool TryGetInvocation(string path, out RuntimeGraphInvocation invocation);
        bool TryGet(RuntimeSourceElementHandle handle, out DebugSourceMapEntry entry);
        bool TryGetHandle(RuntimeSourceElementKey source, out RuntimeSourceElementHandle handle);
        bool TryGetIndexedTarget(RuntimeSourceTarget target, out RuntimeSourceElementHandle handle);
        IReadOnlyList<RuntimeSourceElementHandle> FindHandles(RuntimeSourceElementKey source);
    }

    public sealed class DebugSourceMap : IDebugSourceMap, IRuntimeDebugSourceMap
    {
        readonly List<DebugSourceMapEntry> m_Entries;
        readonly Dictionary<RuntimeSourceElementKey, List<RuntimeSourceElementHandle>> m_BySource;
        readonly Dictionary<RuntimeSourceTarget, RuntimeSourceElementHandle> m_ByIndexedTarget;
        bool m_Sealed;
        readonly List<RuntimeGraphInvocation> m_GraphInvocations;
        readonly Dictionary<string, RuntimeGraphInvocation> m_Invocations;

        public DebugSourceMap(RuntimeContentRevision revision)
        {
            m_Entries = new List<DebugSourceMapEntry>();
            m_BySource = new Dictionary<RuntimeSourceElementKey, List<RuntimeSourceElementHandle>>();
            m_ByIndexedTarget = new Dictionary<RuntimeSourceTarget, RuntimeSourceElementHandle>();
            m_GraphInvocations = new List<RuntimeGraphInvocation>();
            m_Invocations = new Dictionary<string, RuntimeGraphInvocation>(StringComparer.Ordinal);
            Revision = revision;
        }

        DebugSourceMap(DebugSourceMap source)
        {
            m_Entries = source.m_Entries;
            m_BySource = source.m_BySource;
            m_ByIndexedTarget = source.m_ByIndexedTarget;
            m_GraphInvocations = source.m_GraphInvocations;
            m_Invocations = source.m_Invocations;
            TimelineCount = source.TimelineCount;
            TrackCount = source.TrackCount;
            m_Sealed = true;
            Revision = source.Revision;
        }

        public DebugSourceMap CreateTimeRangeRevisionView() => new DebugSourceMap(this);
        public void BindTimeRangeRevision(RuntimeContentRevision revision) => Revision = revision;

        public RuntimeContentRevision Revision { get; private set; }
        public IDebugSourceMap SourceMap => this;
        public IReadOnlyList<DebugSourceMapEntry> Entries => m_Entries;
        public IReadOnlyList<RuntimeGraphInvocation> GraphInvocations => m_GraphInvocations;
        public int TimelineCount { get; private set; }
        public int TrackCount { get; private set; }
        public bool TryGetInvocation(string path, out RuntimeGraphInvocation invocation) =>
            m_Invocations.TryGetValue(path, out invocation);

        public RuntimeSourceElementHandle Add(
            RuntimeSourceElementKey source,
            RuntimeSourceElementHandle parent,
            string displayName,
            string contentHash,
            RuntimeSourceTarget target)
        {
            if (m_Sealed)
                throw new InvalidOperationException("Debug Source Map is sealed.");
            if (!source.IsValid)
                throw new InvalidOperationException("Debug Source Map source identity is invalid.");

            var handle = new RuntimeSourceElementHandle(m_Entries.Count + 1, source.Kind);
            var entry = new DebugSourceMapEntry(handle, source, parent, displayName, contentHash, target);
            m_Entries.Add(entry);
            if (!m_BySource.TryGetValue(source, out List<RuntimeSourceElementHandle> handles))
            {
                handles = new List<RuntimeSourceElementHandle>();
                m_BySource.Add(source, handles);
                if (source.Kind == RuntimeSourceElementKind.Timeline)
                    TimelineCount++;
                else if (source.Kind == RuntimeSourceElementKind.Track)
                    TrackCount++;
            }
            handles.Add(handle);
            if (target.IsIndexedTarget && !m_ByIndexedTarget.TryAdd(target, handle))
                throw new InvalidOperationException($"Debug Source Map indexed target is duplicated: {target}");
            return handle;
        }
        public void AddGraphInvocation(RuntimeGraphInvocation invocation)
        {
            if (m_Sealed)
                throw new InvalidOperationException("Debug Source Map is sealed.");
            m_GraphInvocations.Add(invocation);
            m_Invocations.Add(invocation.Path, invocation);
        }

        public void Seal()
        {
            if (!Revision.IsValid)
                throw new InvalidOperationException("Debug Source Map revision is invalid.");
            for (int i = 0; i < m_Entries.Count; i++)
            {
                DebugSourceMapEntry entry = m_Entries[i];
                if (entry.Parent.IsValid && entry.Parent.Value > m_Entries.Count)
                    throw new InvalidOperationException($"Debug Source Map parent handle is missing: {entry.Parent}.");
            }
            m_Sealed = true;
        }

        public bool TryGet(RuntimeSourceElementHandle handle, out DebugSourceMapEntry entry)
        {
            if (!handle.IsValid || handle.Value > m_Entries.Count)
            {
                entry = default;
                return false;
            }

            entry = m_Entries[handle.Value - 1];
            return entry.Handle.Kind == handle.Kind;
        }

        public bool TryGetHandle(RuntimeSourceElementKey source, out RuntimeSourceElementHandle handle)
        {
            if (m_BySource.TryGetValue(source, out List<RuntimeSourceElementHandle> handles) && handles.Count > 0)
            {
                handle = handles[0];
                return true;
            }
            handle = default;
            return false;
        }

        public IReadOnlyList<RuntimeSourceElementHandle> FindHandles(RuntimeSourceElementKey source)
        {
            return m_BySource.TryGetValue(source, out List<RuntimeSourceElementHandle> handles)
                ? handles
                : Array.Empty<RuntimeSourceElementHandle>();
        }

        public bool TryGetIndexedTarget(RuntimeSourceTarget target, out RuntimeSourceElementHandle handle)
        {
            if (target.IsIndexedTarget && m_ByIndexedTarget.TryGetValue(target, out handle))
                return true;
            handle = default;
            return false;
        }
    }

    public static class SourceContentHasher
    {
        public static string Hash(params string[] values)
        {
            ulong hash = 14695981039346656037UL;
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    string value = values[i] ?? string.Empty;
                    for (int character = 0; character < value.Length; character++)
                    {
                        hash ^= value[character];
                        hash *= 1099511628211UL;
                    }
                    hash ^= 0xff;
                    hash *= 1099511628211UL;
                }
            }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
