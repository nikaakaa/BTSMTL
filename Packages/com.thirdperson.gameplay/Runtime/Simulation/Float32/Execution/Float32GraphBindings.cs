using System;
using System.Collections.Generic;
using System.Linq;

namespace ThirdPersonSimulation
{
    public sealed class Float32GraphBindings
    {
        public readonly Float32GraphExecutionData Data;
        public readonly string GraphId;
        public readonly int[] RequiredInputSlots;
        public readonly int InputCapacity;
        public readonly int[] InputSlots;
        public readonly int[] OperationSlots;
        public readonly int[] PublishedSlots;
        public readonly ProgramExecutionSource[] Variables;
        public readonly IReadOnlyList<ProgramCatalogEntry> VariableDeclarations;
        public readonly string[] OperationNodes;

        public Float32GraphBindings(Float32GraphExecutionData data, string graphId,
            Func<string, string, int> bindInput)
        {
            Data = data;
            GraphId = graphId;
            InputSlots = new int[data.Operations.Count];
            OperationSlots = new int[data.Operations.Count];
            OperationNodes = new string[data.Operations.Count];
            Array.Fill(OperationSlots, -1);
            Variables = data.ExecutionSources.Where(map => map.TargetKind == ProgramSourceTargetKind.StateSlot &&
                map.GraphId == graphId && data.StateSlots[map.TargetIndex].Semantic == ProgramStateSemantic.BlackboardValue)
                .OrderBy(map => map.DeclarationId, StringComparer.Ordinal).ToArray();
            VariableDeclarations = data.CatalogIndex.BindBlackboardDeclarations(data.StateSlots, data.References);
            PublishedSlots = Variables.Where(map => Field(VariableDeclarations[map.TargetIndex], "Public").Boolean)
                .Select(map => map.TargetIndex).ToArray();
            foreach (ProgramExecutionSource map in data.ExecutionSources)
                if (map.TargetKind == ProgramSourceTargetKind.Operation)
                    OperationNodes[map.TargetIndex] = map.NodeId;
            foreach (ProgramReference reference in data.References)
            {
                if (reference.HasSourceOperation && reference.Kind == ProgramReferenceKind.StateSlot)
                    OperationSlots[reference.SourceOperation.Value] = reference.TargetIndex;
            }
            foreach (SimulationOperation operation in data.Operations)
            {
                if (operation.Code == SimulationOperationCode.Cycle)
                    OperationSlots[operation.Handle.Value] = data.Topology.RequireOperationStateSlot(
                        operation.Handle, ProgramStateSemantic.RunnableChildCursor);
                if (operation.Code == SimulationOperationCode.HostInput)
                    InputSlots[operation.Handle.Value] = bindInput(operation.Text0,
                        data.Constants[operation.ConstantReferences[0]].Text.ToString());
            }
            RequiredInputSlots = data.Operations.Where(operation => operation.Code == SimulationOperationCode.HostInput)
                .Select(operation => InputSlots[operation.Handle.Value]).Distinct().OrderBy(slot => slot).ToArray();
            InputCapacity = RequiredInputSlots.Length == 0 ? 0 : RequiredInputSlots[^1] + 1;
        }

        public OperationHandle FindEntry(string identity)
        {
            foreach (ProgramReference reference in Data.References)
                if (!reference.HasSourceOperation && reference.Kind == ProgramReferenceKind.Operation && reference.Identity == identity)
                    return new OperationHandle(reference.TargetIndex);
            return OperationHandle.Invalid;
        }

        public ProgramConstant Field(ProgramCatalogEntry entry, string name)
        {
            foreach (ProgramCatalogField field in entry.Fields)
                if (field.Name == name)
                    return Data.Constants[field.ConstantIndex];
            throw new InvalidOperationException($"变量声明 '{entry.Identity}' 缺少 '{name}'。");
        }
    }
}
