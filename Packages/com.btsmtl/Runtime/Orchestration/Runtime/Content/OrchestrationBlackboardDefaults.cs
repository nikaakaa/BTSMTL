using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationBlackboardDefaults<TValue> where TValue : unmanaged
    {
        public OrchestrationBlackboardDefaults(OrchestrationIdentityTable identities,
            StableHash contentIdentity, ReadOnlyMemory<OrchestrationBlackboardDefault<TValue>> values)
        {
            Identities = identities;
            ContentIdentity = contentIdentity;
            Values = values;
        }

        public OrchestrationIdentityTable Identities { get; }
        public StableHash ContentIdentity { get; }
        public ReadOnlyMemory<OrchestrationBlackboardDefault<TValue>> Values { get; }
    }
}
