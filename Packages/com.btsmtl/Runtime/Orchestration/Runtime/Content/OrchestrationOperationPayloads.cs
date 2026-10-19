using System.Runtime.InteropServices;

namespace BTSMTL.Orchestration
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationModePayload
    {
        public OrchestrationModePayload(int mode) { Mode = mode; }
        public readonly int Mode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationCyclePayload
    {
        public OrchestrationCyclePayload(int start, int count) { Start = start; Count = count; }
        public readonly int Start;
        public readonly int Count;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationGraphCallPayload
    {
        public OrchestrationGraphCallPayload(ushort callIndex) { CallIndex = callIndex; }
        public readonly ushort CallIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationStatePayload
    {
        public OrchestrationStatePayload(int identityId, ushort machineIndex)
        { Identity = new OrchestrationStateIdentityPayload(identityId); MachineIndex = machineIndex; }
        public readonly OrchestrationStateIdentityPayload Identity;
        public readonly ushort MachineIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationStateIdentityPayload
    {
        public OrchestrationStateIdentityPayload(int identityId) { IdentityId = identityId; }
        public readonly int IdentityId;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationTimelinePayload
    {
        public OrchestrationTimelinePayload(int contentIdentityId, int mode)
        { ContentIdentityId = contentIdentityId; Mode = mode; }
        public readonly int ContentIdentityId;
        public readonly int Mode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationTimelineSectionPayload
    {
        public OrchestrationTimelineSectionPayload(int contentIdentityId) { ContentIdentityId = contentIdentityId; }
        public readonly int ContentIdentityId;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationRequestPayload
    {
        public OrchestrationRequestPayload(int identityId) { IdentityId = identityId; }
        public readonly int IdentityId;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationBlackboardSetPayload
    {
        public OrchestrationBlackboardSetPayload(ushort slot, OrchestrationAssignmentOpcode assignment)
        { Slot = slot; Assignment = assignment; }
        public readonly ushort Slot;
        public readonly OrchestrationAssignmentOpcode Assignment;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationOutputPayload
    {
        public OrchestrationOutputPayload(ushort templateIndex, int producerIdentityId)
        { TemplateIndex = templateIndex; ProducerIdentityId = producerIdentityId; }
        public readonly ushort TemplateIndex;
        public readonly int ProducerIdentityId;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct OrchestrationExtensionPayload
    {
        public OrchestrationExtensionPayload(ushort bindingIndex, OrchestrationIndexRange outputAssignments)
        { BindingIndex = bindingIndex; OutputAssignments = outputAssignments; }
        public readonly ushort BindingIndex;
        public readonly OrchestrationIndexRange OutputAssignments;
    }
}
