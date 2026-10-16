using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public readonly struct GraphOrchestrationHostInputPort
    {
        public GraphOrchestrationHostInputPort(string outputPortId, string inputId, OrchestrationValueKind valueKind,
            string valueTypeIdentity)
        {
            OutputPortId = outputPortId;
            InputId = inputId;
            ValueKind = valueKind;
            ValueTypeIdentity = valueTypeIdentity;
        }

        public string OutputPortId { get; }
        public string InputId { get; }
        public OrchestrationValueKind ValueKind { get; }
        public string ValueTypeIdentity { get; }
    }

    public enum OrchestrationGraphSourceTargetKind : byte
    {
        Operation = 1,
        Constant = 2,
        BlackboardValue = 3,
        Reference = 4,
        Producer = 5,
        CatalogEntry = 6,
        BodyMotion = 7,
        ControlModule = 8,
        ControlTransition = 10,
        OperationPort = 11,
        GraphInvocation = 12,
        OptimizedAway = 13,
        AuthorConstant = 14,
        AuthorStateSlot = 15
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationGraphEntry
    {
        public OrchestrationGraphEntry(int entryIdentityId, OrchestrationExecutionEntry execution)
        {
            EntryIdentityId = entryIdentityId;
            Execution = execution;
        }

        public int EntryIdentityId { get; }
        public OrchestrationExecutionEntry Execution { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationGraphVariable
    {
        public OrchestrationGraphVariable(int graphIdentityId, int declarationIdentityId,
            int keyIdentityId, int valueTypeIdentityId, bool isPublic, int valueSlot)
        {
            GraphIdentityId = graphIdentityId;
            DeclarationIdentityId = declarationIdentityId;
            KeyIdentityId = keyIdentityId;
            ValueTypeIdentityId = valueTypeIdentityId;
            IsPublic = isPublic;
            ValueSlot = valueSlot;
        }

        public int GraphIdentityId { get; }
        public int DeclarationIdentityId { get; }
        public int KeyIdentityId { get; }
        public int ValueTypeIdentityId { get; }
        public bool IsPublic { get; }
        public int ValueSlot { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationGraphHostInput
    {
        public OrchestrationGraphHostInput(int inputIdentityId, int valueTypeIdentityId, int inputSlot)
        {
            InputIdentityId = inputIdentityId;
            ValueTypeIdentityId = valueTypeIdentityId;
            InputSlot = inputSlot;
        }

        public int InputIdentityId { get; }
        public int ValueTypeIdentityId { get; }
        public int InputSlot { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationGraphPort
    {
        public OrchestrationGraphPort(int graphIdentityId, int nodeIdentityId, int portIdentityId,
            int operationIndex, bool isInput, bool isTrigger, int compiledPortIdentityId, int valueIndex)
        {
            GraphIdentityId = graphIdentityId;
            NodeIdentityId = nodeIdentityId;
            PortIdentityId = portIdentityId;
            OperationIndex = operationIndex;
            IsInput = isInput;
            IsTrigger = isTrigger;
            CompiledPortIdentityId = compiledPortIdentityId;
            ValueIndex = valueIndex;
        }

        public int GraphIdentityId { get; }
        public int NodeIdentityId { get; }
        public int PortIdentityId { get; }
        public int OperationIndex { get; }
        public bool IsInput { get; }
        public bool IsTrigger { get; }
        public int CompiledPortIdentityId { get; }
        public int ValueIndex { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationGraphExecutionSource
    {
        public OrchestrationGraphExecutionSource(OrchestrationGraphSourceTargetKind targetKind, int targetIndex,
            string graphId, string nodeId, string portId, string edgeId, string declarationId,
            string timelineId, string trackId, string clipId, bool isInputPort, string graphInvocationPath,
            string compiledPortId, string valuePortDirection, string sourceInvocationPath,
            string parentInvocationPath, string invocationCallerKind, string invocationCallerId,
            string invocationCallerClipId, int invocationCallerOperationIndex,
            OrchestrationExecutionEntry invocationEntry)
        {
            TargetKind = targetKind;
            TargetIndex = targetIndex;
            GraphId = graphId;
            NodeId = nodeId;
            PortId = portId;
            EdgeId = edgeId;
            DeclarationId = declarationId;
            TimelineId = timelineId;
            TrackId = trackId;
            ClipId = clipId;
            IsInputPort = isInputPort;
            GraphInvocationPath = graphInvocationPath;
            CompiledPortId = compiledPortId;
            ValuePortDirection = valuePortDirection;
            SourceInvocationPath = sourceInvocationPath;
            ParentInvocationPath = parentInvocationPath;
            InvocationCallerKind = invocationCallerKind;
            InvocationCallerId = invocationCallerId;
            InvocationCallerClipId = invocationCallerClipId;
            InvocationCallerOperationIndex = invocationCallerOperationIndex;
            InvocationEntry = invocationEntry;
        }

        public OrchestrationGraphSourceTargetKind TargetKind { get; }
        public int TargetIndex { get; }
        public string GraphId { get; }
        public string NodeId { get; }
        public string PortId { get; }
        public string EdgeId { get; }
        public string DeclarationId { get; }
        public string TimelineId { get; }
        public string TrackId { get; }
        public string ClipId { get; }
        public bool IsInputPort { get; }
        public string GraphInvocationPath { get; }
        public string CompiledPortId { get; }
        public string ValuePortDirection { get; }
        public string SourceInvocationPath { get; }
        public string ParentInvocationPath { get; }
        public string InvocationCallerKind { get; }
        public string InvocationCallerId { get; }
        public string InvocationCallerClipId { get; }
        public int InvocationCallerOperationIndex { get; }
        public OrchestrationExecutionEntry InvocationEntry { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationGraphSource
    {
        public OrchestrationGraphSource(OrchestrationGraphExecutionSource source,
            string sourceType, ReadOnlyMemory<string> displayPathSegments)
        {
            Source = source;
            SourceType = sourceType;
            DisplayPathSegments = displayPathSegments;
        }

        public OrchestrationGraphExecutionSource Source { get; }
        public string SourceType { get; }
        public ReadOnlyMemory<string> DisplayPathSegments { get; }
    }
}
