using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public sealed class GameplayAbilityGraphInvocationLayout
    {
        sealed class InvocationBinding
        {
            public InvocationBinding(ProgramExecutionSource source, int generationSlot)
            {
                Source = source;
                GenerationSlot = generationSlot;
            }

            public readonly ProgramExecutionSource Source;
            public readonly int GenerationSlot;
            public InvocationGroup Parent;
        }

        sealed class InvocationGroup
        {
            readonly InvocationBinding m_Single;
            readonly Dictionary<(CanonicalTextValue ClipId, string Hook), InvocationBinding> m_TreeClips;
            public readonly string SourcePath;

            public InvocationGroup(string sourcePath, List<InvocationBinding> invocations)
            {
                SourcePath = sourcePath;
                if (invocations.Count == 1)
                {
                    m_Single = invocations[0];
                    return;
                }
                m_TreeClips = new Dictionary<(CanonicalTextValue, string), InvocationBinding>(invocations.Count);
                foreach (InvocationBinding invocation in invocations)
                {
                    ProgramExecutionSource source = invocation.Source;
                    if (source.InvocationCallerKind == ProgramInvocationCallerKind.TimelineClip &&
                        !m_TreeClips.TryAdd((source.InvocationCallerClipId, source.InvocationCallerId), invocation))
                        throw new InvalidOperationException("TreeClip图调用重复。");
                }
            }

            public InvocationBinding Resolve(CanonicalTextValue? clipId, AbilityTreeClipHook hook)
            {
                if (m_Single != null)
                    return m_Single;
                if (!clipId.HasValue)
                    throw new InvalidOperationException("多入口TreeClip图调用缺少当前入口。");
                if (!m_TreeClips.TryGetValue((clipId.Value, HookName(hook)), out InvocationBinding invocation))
                    throw new InvalidOperationException("TreeClip图调用入口未声明。");
                return invocation;
            }
        }

        readonly Dictionary<string, InvocationBinding> m_Invocations = new(StringComparer.Ordinal);
        readonly InvocationGroup[] m_OperationInvocations;

        public GameplayAbilityGraphInvocationLayout(IReadOnlyList<ProgramExecutionSource> sources, int operationCount,
            Func<OperationHandle, int> generationSlot)
        {
            m_OperationInvocations = new InvocationGroup[operationCount];
            var groups = new Dictionary<string, List<InvocationBinding>>(StringComparer.Ordinal);
            foreach (ProgramExecutionSource source in sources)
            {
                if (source.TargetKind != ProgramSourceTargetKind.GraphInvocation)
                    continue;
                int slot = generationSlot(new OperationHandle(source.TargetIndex));
                if (slot < 0)
                    throw new InvalidOperationException("图调用缺少已声明的生命周期代次槽。");
                var invocation = new InvocationBinding(source, slot);
                if (!m_Invocations.TryAdd(source.GraphInvocationPath, invocation))
                    throw new InvalidOperationException("图调用路径重复。");
                if (!groups.TryGetValue(source.SourceInvocationPath, out List<InvocationBinding> invocations))
                {
                    invocations = new List<InvocationBinding>();
                    groups.Add(source.SourceInvocationPath, invocations);
                }
                invocations.Add(invocation);
            }
            var bindings = new Dictionary<string, InvocationGroup>(groups.Count, StringComparer.Ordinal);
            foreach (var group in groups)
                bindings.Add(group.Key, new InvocationGroup(group.Key, group.Value));
            foreach (InvocationBinding invocation in m_Invocations.Values)
            {
                if (string.IsNullOrEmpty(invocation.Source.ParentInvocationPath))
                    continue;
                if (!bindings.TryGetValue(invocation.Source.ParentInvocationPath, out invocation.Parent))
                    throw new InvalidOperationException("图调用来源未声明。");
            }
            foreach (ProgramExecutionSource source in sources)
            {
                if (source.TargetKind != ProgramSourceTargetKind.Operation || string.IsNullOrEmpty(source.GraphInvocationPath))
                    continue;
                if (!bindings.TryGetValue(source.GraphInvocationPath, out InvocationGroup group))
                    throw new InvalidOperationException("操作引用了未声明的图调用。");
                if (m_OperationInvocations[source.TargetIndex] != null)
                    throw new InvalidOperationException("操作图调用来源重复。");
                m_OperationInvocations[source.TargetIndex] = group;
            }
            foreach (ProgramExecutionSource source in sources)
            {
                if (source.TargetKind != ProgramSourceTargetKind.OperationPort)
                    continue;
                InvocationGroup operation = m_OperationInvocations[source.TargetIndex];
                if (operation == null || !string.Equals(
                        source.SourceInvocationPath,
                        operation.SourcePath,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("端口来源与操作不属于同一执行代次。");
                }
            }
        }

        public int GenerationSlot(OperationHandle operation) =>
            ResolveOperationInvocation(operation, null, default).GenerationSlot;

        public int GenerationSlot(OperationHandle operation, in AbilityTreeClipInvocation treeClipInvocation) =>
            ResolveOperationInvocation(operation, treeClipInvocation.ClipAuthoringId, treeClipInvocation.Hook).GenerationSlot;

        public int ParentGenerationSlot(OperationHandle operation) =>
            ResolveParentGenerationSlot(operation, null, default);

        public int ParentGenerationSlot(OperationHandle operation, in AbilityTreeClipInvocation treeClipInvocation) =>
            ResolveParentGenerationSlot(operation, treeClipInvocation.ClipAuthoringId, treeClipInvocation.Hook);

        public ProgramExecutionSource Invocation(string path) => m_Invocations[path].Source;

        public string InvocationPath(OperationHandle operation) =>
            ResolveOperationInvocation(operation, null, default).Source.GraphInvocationPath;

        public string InvocationPath(OperationHandle operation, in AbilityTreeClipInvocation treeClipInvocation) =>
            ResolveOperationInvocation(operation, treeClipInvocation.ClipAuthoringId, treeClipInvocation.Hook).Source.GraphInvocationPath;

        int ResolveParentGenerationSlot(OperationHandle operation, CanonicalTextValue? clipId, AbilityTreeClipHook hook)
        {
            InvocationGroup parent = ResolveOperationInvocation(operation, clipId, hook).Parent;
            return parent == null ? -1 : parent.Resolve(clipId, hook).GenerationSlot;
        }

        InvocationBinding ResolveOperationInvocation(
            OperationHandle operation,
            CanonicalTextValue? clipId, AbilityTreeClipHook hook)
        {
            if (!operation.IsValid || operation.Value >= m_OperationInvocations.Length ||
                m_OperationInvocations[operation.Value] == null)
            {
                throw new InvalidOperationException("操作缺少图调用来源。");
            }
            return m_OperationInvocations[operation.Value].Resolve(clipId, hook);
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
