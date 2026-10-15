using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationEntryScope
    {
        OrchestrationEntryScope(OrchestrationExecutionEntry[] entries,
            int[] operationIndices, int[] extensionBindingIndices)
        {
            Entries = entries;
            OperationIndices = operationIndices;
            ExtensionBindingIndices = extensionBindingIndices;
        }

        public ReadOnlyMemory<OrchestrationExecutionEntry> Entries { get; }
        public ReadOnlyMemory<int> OperationIndices { get; }
        public ReadOnlyMemory<int> ExtensionBindingIndices { get; }

        public static OrchestrationEntryScope Prepare<TValue>(
            OrchestrationProgramContent<TValue> program,
            ReadOnlySpan<OrchestrationExecutionEntry> entries) where TValue : unmanaged
        {
            ReadOnlySpan<OrchestrationOperation> operations = program.Operations.Span;
            ReadOnlySpan<OrchestrationFlowEdge> edges = program.Edges.Span;
            var outgoing = new List<int>[operations.Length];
            for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
            {
                int source = edges[edgeIndex].SourceOperationIndex;
                if (outgoing[source] == null)
                    outgoing[source] = new List<int>();
                outgoing[source].Add(edgeIndex);
            }

            var reachable = new bool[operations.Length];
            var pending = new List<int>();
            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                ref readonly OrchestrationExecutionEntry entry = ref entries[entryIndex];
                if ((uint)entry.OperationIndex >= (uint)operations.Length)
                    throw new ArgumentOutOfRangeException(nameof(entries),
                        $"入口 {entryIndex} 引用了不存在的操作 {entry.OperationIndex}。");
                if (entry.Contract != OrchestrationExecutionContract.Complete &&
                    entry.Contract != OrchestrationExecutionContract.Resumable)
                    throw new ArgumentException($"入口 {entryIndex} 的执行合同无效：{entry.Contract}。", nameof(entries));
                AddOperation(entry.OperationIndex, reachable, pending);
            }

            var extensionBindings = new HashSet<int>();
            for (int cursor = 0; cursor < pending.Count; cursor++)
            {
                int operationIndex = pending[cursor];
                ref readonly OrchestrationOperation operation = ref operations[operationIndex];
                if (operation.Code == OrchestrationOperationCode.Extension)
                    extensionBindings.Add(operation.ExtensionBindingIndex);
                if (operation.BlackboardFactsBindingIndex >= 0)
                    extensionBindings.Add(operation.BlackboardFactsBindingIndex);
                ReadOnlySpan<OrchestrationHookBinding> hooks = program.Hooks.Span.Slice(
                    operation.LifecycleHooks.Offset, operation.LifecycleHooks.Count);
                foreach (ref readonly OrchestrationHookBinding hook in hooks)
                    extensionBindings.Add(hook.ExtensionBindingIndex);

                ReadOnlySpan<OrchestrationValueInputBinding> inputs = program.ValueInputs.Span.Slice(
                    operation.Inputs.Offset, operation.Inputs.Count);
                foreach (ref readonly OrchestrationValueInputBinding input in inputs)
                    if (input.Source == OrchestrationValueInputSource.Operation)
                        AddOperation(input.SourceIndex, reachable, pending);
                if (operation.Code == OrchestrationOperationCode.SubGraph)
                    AddOperation(program.GraphCalls.Span[operation.GraphCallIndex].EntryOperationIndex,
                        reachable, pending);

                List<int> operationEdges = outgoing[operationIndex];
                if (operationEdges == null)
                    continue;
                foreach (int edgeIndex in operationEdges)
                {
                    ref readonly OrchestrationFlowEdge edge = ref edges[edgeIndex];
                    AddOperation(edge.TargetOperationIndex, reachable, pending);
                    if (edge.HasCondition)
                        AddOperation(edge.ConditionOperationIndex, reachable, pending);
                }
            }

            int[] operationIndices = pending.ToArray();
            Array.Sort(operationIndices);
            var extensionBindingIndices = new int[extensionBindings.Count];
            extensionBindings.CopyTo(extensionBindingIndices);
            Array.Sort(extensionBindingIndices);
            return new OrchestrationEntryScope(entries.ToArray(), operationIndices, extensionBindingIndices);
        }

        static void AddOperation(int operationIndex, bool[] reachable, List<int> pending)
        {
            if (reachable[operationIndex])
                return;
            reachable[operationIndex] = true;
            pending.Add(operationIndex);
        }
    }
}
