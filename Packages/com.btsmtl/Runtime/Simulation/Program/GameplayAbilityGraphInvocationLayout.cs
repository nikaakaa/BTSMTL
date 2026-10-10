using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public sealed class GameplayAbilityGraphInvocationLayout
    {
        readonly Dictionary<string, ProgramSourceMapEntry> m_Invocations = new(StringComparer.Ordinal);
        readonly Dictionary<string, List<ProgramSourceMapEntry>> m_InvocationsBySourcePath = new(StringComparer.Ordinal);
        readonly Dictionary<(string SourcePath, CanonicalTextValue ClipId, string Hook), ProgramSourceMapEntry> m_TreeClipInvocations = new();
        readonly Dictionary<string, int> m_GenerationSlots = new(StringComparer.Ordinal);
        readonly ProgramSourceMapEntry[] m_OperationSources;

        public GameplayAbilityGraphInvocationLayout(IReadOnlyList<ProgramSourceMapEntry> sources, int operationCount,
            Func<OperationHandle, int> generationSlot)
        {
            m_OperationSources = new ProgramSourceMapEntry[operationCount];
            foreach (ProgramSourceMapEntry source in sources)
            {
                if (source.TargetKind != ProgramSourceTargetKind.GraphInvocation)
                    continue;
                if (!m_Invocations.TryAdd(source.GraphInvocationPath, source))
                    throw new InvalidOperationException("图调用路径重复。");
                int slot = generationSlot(new OperationHandle(source.TargetIndex));
                if (slot < 0)
                    throw new InvalidOperationException("图调用缺少已声明的生命周期代次槽。");
                m_GenerationSlots.Add(source.GraphInvocationPath, slot);
                if (!m_InvocationsBySourcePath.TryGetValue(source.SourceInvocationPath, out List<ProgramSourceMapEntry> invocations))
                {
                    invocations = new List<ProgramSourceMapEntry>();
                    m_InvocationsBySourcePath.Add(source.SourceInvocationPath, invocations);
                }
                invocations.Add(source);
                if (source.InvocationCallerKind == ProgramInvocationCallerKind.TimelineClip &&
                    !m_TreeClipInvocations.TryAdd((
                        source.SourceInvocationPath,
                        source.InvocationCallerClipId,
                        source.InvocationCallerId), source))
                {
                    throw new InvalidOperationException("TreeClip图调用重复。");
                }
            }
            foreach (ProgramSourceMapEntry source in sources)
            {
                if (source.TargetKind != ProgramSourceTargetKind.Operation || string.IsNullOrEmpty(source.GraphInvocationPath))
                    continue;
                if (!m_InvocationsBySourcePath.ContainsKey(source.GraphInvocationPath))
                    throw new InvalidOperationException("操作引用了未声明的图调用。");
                if (m_OperationSources[source.TargetIndex] != null)
                    throw new InvalidOperationException("操作图调用来源重复。");
                m_OperationSources[source.TargetIndex] = source;
            }
            foreach (ProgramSourceMapEntry source in sources)
            {
                if (source.TargetKind != ProgramSourceTargetKind.OperationPort)
                    continue;
                ProgramSourceMapEntry operation = m_OperationSources[source.TargetIndex];
                if (operation == null || !string.Equals(
                        source.SourceInvocationPath,
                        operation.GraphInvocationPath,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("端口来源与操作不属于同一执行代次。");
                }
            }
        }

        public int GenerationSlot(OperationHandle operation) =>
            ResolveGenerationSlot(operation, null);

        public int GenerationSlot(OperationHandle operation, AbilityTreeClipInvocation treeClipInvocation) =>
            ResolveGenerationSlot(operation, treeClipInvocation);

        public int ParentGenerationSlot(OperationHandle operation) =>
            ResolveParentGenerationSlot(operation, null);

        public int ParentGenerationSlot(OperationHandle operation, AbilityTreeClipInvocation treeClipInvocation) =>
            ResolveParentGenerationSlot(operation, treeClipInvocation);

        public ProgramSourceMapEntry Invocation(string path) => m_Invocations[path];

        public string InvocationPath(OperationHandle operation) =>
            ResolveOperationInvocation(operation, null).GraphInvocationPath;

        public string InvocationPath(OperationHandle operation, in AbilityTreeClipInvocation treeClipInvocation) =>
            ResolveOperationInvocation(operation, treeClipInvocation).GraphInvocationPath;

        int ResolveGenerationSlot(OperationHandle operation, AbilityTreeClipInvocation? treeClipInvocation)
        {
            ProgramSourceMapEntry invocation = ResolveOperationInvocation(operation, treeClipInvocation);
            return m_GenerationSlots[invocation.GraphInvocationPath];
        }

        int ResolveParentGenerationSlot(OperationHandle operation, AbilityTreeClipInvocation? treeClipInvocation)
        {
            ProgramSourceMapEntry invocation = ResolveOperationInvocation(operation, treeClipInvocation);
            if (string.IsNullOrEmpty(invocation.ParentInvocationPath))
                return -1;
            ProgramSourceMapEntry parent = ResolveInvocation(
                invocation.ParentInvocationPath,
                treeClipInvocation);
            return m_GenerationSlots[parent.GraphInvocationPath];
        }

        ProgramSourceMapEntry ResolveOperationInvocation(
            OperationHandle operation,
            AbilityTreeClipInvocation? treeClipInvocation)
        {
            if (!operation.IsValid || operation.Value >= m_OperationSources.Length ||
                m_OperationSources[operation.Value] == null)
            {
                throw new InvalidOperationException("操作缺少图调用来源。");
            }
            return ResolveInvocation(
                m_OperationSources[operation.Value].GraphInvocationPath,
                treeClipInvocation);
        }

        ProgramSourceMapEntry ResolveInvocation(
            string sourcePath,
            AbilityTreeClipInvocation? treeClipInvocation)
        {
            if (!m_InvocationsBySourcePath.TryGetValue(sourcePath, out List<ProgramSourceMapEntry> invocations) ||
                invocations.Count == 0)
            {
                throw new InvalidOperationException("图调用来源未声明。");
            }
            if (invocations.Count == 1)
                return invocations[0];
            if (!treeClipInvocation.HasValue)
                throw new InvalidOperationException("多入口TreeClip图调用缺少当前入口。");
            AbilityTreeClipInvocation treeClip = treeClipInvocation.Value;
            if (!m_TreeClipInvocations.TryGetValue((
                    sourcePath,
                    treeClip.ClipAuthoringId,
                    HookName(treeClip.Hook)), out ProgramSourceMapEntry invocation))
            {
                throw new InvalidOperationException("TreeClip图调用入口未声明。");
            }
            return invocation;
        }

        static string HookName(AbilityTreeClipHook hook) => hook switch
        {
            AbilityTreeClipHook.OnEnable => nameof(AbilityTreeClipHook.OnEnable),
            AbilityTreeClipHook.OnDisable => nameof(AbilityTreeClipHook.OnDisable),
            AbilityTreeClipHook.OnDestroy => nameof(AbilityTreeClipHook.OnDestroy),
            AbilityTreeClipHook.Root => nameof(AbilityTreeClipHook.Root),
            _ => throw new ArgumentOutOfRangeException(nameof(hook))
        };
    }
}
