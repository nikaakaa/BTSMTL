using System;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationObjectParentEndPolicy : byte
    {
        Continue = 1,
        Cancel = 2
    }

    public readonly struct OrchestrationObjectRequestOrigin
    {
        public OrchestrationObjectRequestOrigin(ObjectHandle carrier,
            in OrchestrationExecutionOwnerToken execution, ObjectHandle initiator)
        {
            Carrier = carrier;
            Execution = execution;
            Initiator = initiator;
        }

        public ObjectHandle Carrier { get; }
        public readonly OrchestrationExecutionOwnerToken Execution;
        public ObjectHandle Initiator { get; }
    }

    public readonly struct OrchestrationObjectSpawnRequest<TValue> where TValue : unmanaged
    {
        public OrchestrationObjectSpawnRequest(in OrchestrationObjectRequestOrigin parent,
            int abilityIdentityId, int carrierDefinitionIdentityId, in TValue position, in TValue yaw,
            OrchestrationObjectParentEndPolicy parentEndPolicy)
        {
            Parent = parent;
            AbilityIdentityId = abilityIdentityId;
            CarrierDefinitionIdentityId = carrierDefinitionIdentityId;
            Position = position;
            Yaw = yaw;
            ParentEndPolicy = parentEndPolicy;
        }

        public readonly OrchestrationObjectRequestOrigin Parent;
        public int AbilityIdentityId { get; }
        public int CarrierDefinitionIdentityId { get; }
        public readonly TValue Position;
        public readonly TValue Yaw;
        public OrchestrationObjectParentEndPolicy ParentEndPolicy { get; }
    }

    public readonly struct OrchestrationObjectRequestResult
    {
        public OrchestrationObjectRequestResult(OrchestrationExtensionResult result,
            ObjectHandle handle, int failureCode)
        {
            Result = result;
            Handle = handle;
            FailureCode = failureCode;
        }

        public OrchestrationExtensionResult Result { get; }
        public ObjectHandle Handle { get; }
        public int FailureCode { get; }
    }

    public interface IOrchestrationObjectBackend<TValue, TContext>
        where TValue : unmanaged
        where TContext : struct
    {
        ObjectHandle ObjectHandle(in TValue value);
        TValue FromObjectHandle(ObjectHandle value);
        TValue FromBoolean(bool value);
        TValue FromInt32(int value);
        ObjectHandle Carrier(in TContext context);
        ObjectHandle Initiator(in TContext context, in OrchestrationExecutionOwnerToken execution);
        bool Exists(in TContext context, ObjectHandle handle);
        bool TryReadPose(in TContext context, ObjectHandle handle, out TValue position, out TValue yaw);
        TValue ReadStartupInput(in TContext context, in OrchestrationExecutionOwnerToken execution, int inputIndex);
        OrchestrationObjectRequestResult SpawnAndActivate(ref TContext context,
            in OrchestrationObjectSpawnRequest<TValue> request, ReadOnlySpan<TValue> startupInputs);
        OrchestrationObjectRequestResult NotifyParent(ref TContext context,
            in OrchestrationObjectRequestOrigin origin, int eventIdentityId, int eventTagIdentityId);
        OrchestrationObjectRequestResult SendCharacterEvent(ref TContext context,
            in OrchestrationObjectRequestOrigin origin, ObjectHandle recipient,
            int eventIdentityId, int eventTagIdentityId);
        OrchestrationObjectRequestResult Destroy(ref TContext context,
            in OrchestrationObjectRequestOrigin origin, ObjectHandle target);
        void CancelRequest(ref TContext context, in OrchestrationObjectRequestOrigin origin);
    }
}
