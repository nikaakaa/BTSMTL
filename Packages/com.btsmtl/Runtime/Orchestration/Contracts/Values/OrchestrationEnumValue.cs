using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true), ValueOperations]
    public readonly partial struct OrchestrationEnumValue : IEquatable<OrchestrationEnumValue>
    {
        public OrchestrationEnumValue(int typeContentId, int value)
        {
            TypeContentId = typeContentId;
            Value = value;
        }

        public int TypeContentId { get; }
        public int Value { get; }
    }
}
