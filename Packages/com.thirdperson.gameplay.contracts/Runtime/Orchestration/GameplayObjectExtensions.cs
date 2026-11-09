using System;
using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public readonly struct GameplayObjectStartupInputContent
    {
        public GameplayObjectStartupInputContent(int inputIndex, OrchestrationValueKind valueKind)
        {
            InputIndex = inputIndex;
            Value = new OrchestrationExtensionOutputPort<GameplayObjectExtensionContracts.StartupInput>(
                GameplayObjectExtensionContracts.StartupInput.ValuePortId, valueKind, 0,
                valueKind == OrchestrationValueKind.ObjectHandle ? OrchestrationObjectKind.Any : null);
            Contract = GameplayObjectExtensionContracts.StartupInput.Definition.Declaration
                .WithAdditionalPorts(new[] { Value.Declaration }).Contract;
        }

        public int InputIndex { get; }
        public OrchestrationExtensionOutputPort<GameplayObjectExtensionContracts.StartupInput> Value { get; }
        public OrchestrationExtensionContract Contract { get; }
    }

    public readonly struct GameplayObjectSpawnContent
    {
        public GameplayObjectSpawnContent(int abilityIdentityId, int carrierDefinitionIdentityId,
            OrchestrationObjectParentEndPolicy parentEndPolicy,
            ReadOnlySpan<OrchestrationExtensionPort> startupPorts)
        {
            AbilityIdentityId = abilityIdentityId;
            CarrierDefinitionIdentityId = carrierDefinitionIdentityId;
            ParentEndPolicy = parentEndPolicy;
            Contract = GameplayObjectExtensionContracts.SpawnAndActivate.Definition.Declaration
                .WithAdditionalPorts(startupPorts).Contract;
        }

        public int AbilityIdentityId { get; }
        public int CarrierDefinitionIdentityId { get; }
        public OrchestrationObjectParentEndPolicy ParentEndPolicy { get; }
        public OrchestrationExtensionContract Contract { get; }
    }

    public readonly struct GameplayObjectEventContent
    {
        public GameplayObjectEventContent(int eventIdentityId, int eventTagIdentityId)
        {
            EventIdentityId = eventIdentityId;
            EventTagIdentityId = eventTagIdentityId;
        }

        public int EventIdentityId { get; }
        public int EventTagIdentityId { get; }
    }

    static class GameplayObjectExtensionExecution<TValue, TContext, TBackend>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        internal static OrchestrationObjectRequestOrigin Origin<TOperation>(in TBackend backend,
            in TContext context, in OrchestrationExtensionInvocation<TValue, TOperation> invocation)
            where TOperation : struct
        {
            OrchestrationExecutionOwnerToken execution = OrchestrationExecutionOwnerToken.Create(
                in invocation.ExecutionFrame, invocation.OperationIndex, invocation.OperationGeneration);
            return new OrchestrationObjectRequestOrigin(backend.Carrier(in context), in execution,
                backend.Initiator(in context, in execution));
        }

        internal static OrchestrationExtensionResult Result<TOperation>(in TBackend backend,
            in OrchestrationExtensionInvocation<TValue, TOperation> invocation,
            in OrchestrationObjectRequestResult result,
            in OrchestrationExtensionOutputPort<TOperation> succeeded,
            in OrchestrationExtensionOutputPort<TOperation> failureCode) where TOperation : struct
        {
            invocation.WriteOutput(in succeeded, backend.FromBoolean(result.Result == OrchestrationExtensionResult.Success));
            invocation.WriteOutput(in failureCode, backend.FromInt32(result.FailureCode));
            return result.Result;
        }
    }

    public sealed class GameplayObjectFactsExtension<TValue, TContext, TBackend> :
        ReadOnlyOrchestrationExtension<TValue, TContext, GameplayObjectExtensionContracts.Facts>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        readonly TBackend m_Backend;

        public GameplayObjectFactsExtension(in TBackend backend) : base(GameplayObjectExtensionContracts.Facts.Definition) =>
            m_Backend = backend;

        public override OrchestrationExtensionResult Invoke(int contentIndex, in TContext context,
            in OrchestrationExtensionInvocation<TValue, GameplayObjectExtensionContracts.Facts> invocation)
        {
            OrchestrationObjectRequestOrigin origin = GameplayObjectExtensionExecution<TValue, TContext, TBackend>
                .Origin(in m_Backend, in context, in invocation);
            invocation.WriteOutput(in GameplayObjectExtensionContracts.Facts.Carrier, m_Backend.FromObjectHandle(origin.Carrier));
            invocation.WriteOutput(in GameplayObjectExtensionContracts.Facts.Initiator, m_Backend.FromObjectHandle(origin.Initiator));
            return OrchestrationExtensionResult.Success;
        }
    }

    public sealed class GameplayObjectExistsExtension<TValue, TContext, TBackend> :
        ReadOnlyOrchestrationExtension<TValue, TContext, GameplayObjectExtensionContracts.Exists>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        readonly TBackend m_Backend;

        public GameplayObjectExistsExtension(in TBackend backend) : base(GameplayObjectExtensionContracts.Exists.Definition) =>
            m_Backend = backend;

        public override OrchestrationExtensionResult Invoke(int contentIndex, in TContext context,
            in OrchestrationExtensionInvocation<TValue, GameplayObjectExtensionContracts.Exists> invocation)
        {
            ObjectHandle handle = m_Backend.ObjectHandle(in invocation.ReadInput(in GameplayObjectExtensionContracts.Exists.Handle));
            invocation.WriteOutput(in GameplayObjectExtensionContracts.Exists.Result,
                m_Backend.FromBoolean(m_Backend.Exists(in context, handle)));
            return OrchestrationExtensionResult.Success;
        }
    }

    public sealed class GameplayObjectPoseExtension<TValue, TContext, TBackend> :
        ReadOnlyOrchestrationExtension<TValue, TContext, GameplayObjectExtensionContracts.Pose>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        readonly TBackend m_Backend;

        public GameplayObjectPoseExtension(in TBackend backend) : base(GameplayObjectExtensionContracts.Pose.Definition) =>
            m_Backend = backend;

        public override OrchestrationExtensionResult Invoke(int contentIndex, in TContext context,
            in OrchestrationExtensionInvocation<TValue, GameplayObjectExtensionContracts.Pose> invocation)
        {
            ObjectHandle handle = m_Backend.ObjectHandle(in invocation.ReadInput(in GameplayObjectExtensionContracts.Pose.Handle));
            bool valid = m_Backend.TryReadPose(in context, handle, out TValue position, out TValue yaw);
            invocation.WriteOutput(in GameplayObjectExtensionContracts.Pose.Valid, m_Backend.FromBoolean(valid));
            invocation.WriteOutput(in GameplayObjectExtensionContracts.Pose.Position, in position);
            invocation.WriteOutput(in GameplayObjectExtensionContracts.Pose.Yaw, in yaw);
            return OrchestrationExtensionResult.Success;
        }
    }

    static class GameplayObjectCast<TValue, TContext, TBackend>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        internal static OrchestrationExtensionResult Invoke<TOperation>(in TBackend backend, in TContext context,
            in OrchestrationExtensionInvocation<TValue, TOperation> invocation,
            in OrchestrationExtensionInputPort<TOperation> input, in OrchestrationExtensionOutputPort<TOperation> succeeded,
            in OrchestrationExtensionOutputPort<TOperation> result) where TOperation : struct
        {
            ObjectHandle handle = backend.ObjectHandle(in invocation.ReadInput(in input));
            bool matched = backend.TryReadObjectKind(in context, handle, out OrchestrationObjectKind kind) &&
                kind.IsAssignableTo(result.Declaration.ObjectKind);
            invocation.WriteOutput(in succeeded, backend.FromBoolean(matched));
            invocation.WriteOutput(in result, backend.FromObjectHandle(matched ? handle : ObjectHandle.Empty));
            return OrchestrationExtensionResult.Success;
        }
    }

    public sealed class GameplayObjectCastToActorExtension<TValue, TContext, TBackend> :
        ReadOnlyOrchestrationExtension<TValue, TContext, GameplayObjectExtensionContracts.CastToActor>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        readonly TBackend m_Backend;

        public GameplayObjectCastToActorExtension(in TBackend backend) : base(GameplayObjectExtensionContracts.CastToActor.Definition) =>
            m_Backend = backend;

        public override OrchestrationExtensionResult Invoke(int contentIndex, in TContext context,
            in OrchestrationExtensionInvocation<TValue, GameplayObjectExtensionContracts.CastToActor> invocation) =>
            GameplayObjectCast<TValue, TContext, TBackend>.Invoke(in m_Backend, in context, in invocation,
                in GameplayObjectExtensionContracts.CastToActor.Handle, in GameplayObjectExtensionContracts.CastToActor.Succeeded,
                in GameplayObjectExtensionContracts.CastToActor.Result);
    }

    public sealed class GameplayObjectCastToCarrierExtension<TValue, TContext, TBackend> :
        ReadOnlyOrchestrationExtension<TValue, TContext, GameplayObjectExtensionContracts.CastToCarrier>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        readonly TBackend m_Backend;

        public GameplayObjectCastToCarrierExtension(in TBackend backend) : base(GameplayObjectExtensionContracts.CastToCarrier.Definition) =>
            m_Backend = backend;

        public override OrchestrationExtensionResult Invoke(int contentIndex, in TContext context,
            in OrchestrationExtensionInvocation<TValue, GameplayObjectExtensionContracts.CastToCarrier> invocation) =>
            GameplayObjectCast<TValue, TContext, TBackend>.Invoke(in m_Backend, in context, in invocation,
                in GameplayObjectExtensionContracts.CastToCarrier.Handle, in GameplayObjectExtensionContracts.CastToCarrier.Succeeded,
                in GameplayObjectExtensionContracts.CastToCarrier.Result);
    }

    public sealed class GameplayObjectStartupInputExtension<TValue, TContext, TBackend> :
        ReadOnlyOrchestrationExtension<TValue, TContext, GameplayObjectExtensionContracts.StartupInput>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        readonly TBackend m_Backend;
        readonly GameplayObjectStartupInputContent[] m_Contents;

        public GameplayObjectStartupInputExtension(in TBackend backend, ReadOnlySpan<GameplayObjectStartupInputContent> contents)
            : base(GameplayObjectExtensionContracts.StartupInput.Definition)
        {
            m_Backend = backend;
            m_Contents = contents.ToArray();
        }

        public override OrchestrationExtensionContract Describe(int contentIndex) => m_Contents[contentIndex].Contract;

        public override OrchestrationExtensionResult Invoke(int contentIndex, in TContext context,
            in OrchestrationExtensionInvocation<TValue, GameplayObjectExtensionContracts.StartupInput> invocation)
        {
            ref readonly GameplayObjectStartupInputContent content = ref m_Contents[contentIndex];
            OrchestrationExecutionOwnerToken execution = OrchestrationExecutionOwnerToken.Create(
                in invocation.ExecutionFrame, invocation.OperationIndex, invocation.OperationGeneration);
            TValue value = m_Backend.ReadStartupInput(in context, in execution, content.InputIndex);
            invocation.WriteOutput(content.Value, in value);
            return OrchestrationExtensionResult.Success;
        }
    }

    public sealed class GameplayObjectSpawnExtension<TValue, TContext, TBackend> :
        CandidateOrchestrationExtension<TValue, TContext, GameplayObjectExtensionContracts.SpawnAndActivate>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        readonly TBackend m_Backend;
        readonly GameplayObjectSpawnContent[] m_Contents;

        public GameplayObjectSpawnExtension(in TBackend backend, ReadOnlySpan<GameplayObjectSpawnContent> contents)
            : base(GameplayObjectExtensionContracts.SpawnAndActivate.Definition)
        {
            m_Backend = backend;
            m_Contents = contents.ToArray();
        }

        public override OrchestrationExtensionContract Describe(int contentIndex) => m_Contents[contentIndex].Contract;

        public override OrchestrationExtensionResult Invoke(int contentIndex, ref TContext context,
            in OrchestrationExtensionInvocation<TValue, GameplayObjectExtensionContracts.SpawnAndActivate> invocation)
        {
            OrchestrationObjectRequestOrigin origin = GameplayObjectExtensionExecution<TValue, TContext, TBackend>
                .Origin(in m_Backend, in context, in invocation);
            invocation.WriteOutput(in GameplayObjectExtensionContracts.SpawnAndActivate.Handle,
                m_Backend.FromObjectHandle(ObjectHandle.Empty));
            if (invocation.Kind != OrchestrationExtensionInvocationKind.Execute)
            {
                m_Backend.CancelRequest(ref context, in origin);
                return OrchestrationExtensionResult.Success;
            }
            ref readonly GameplayObjectSpawnContent content = ref m_Contents[contentIndex];
            var request = new OrchestrationObjectSpawnRequest<TValue>(in origin,
                content.AbilityIdentityId, content.CarrierDefinitionIdentityId,
                in invocation.ReadInput(in GameplayObjectExtensionContracts.SpawnAndActivate.Position),
                in invocation.ReadInput(in GameplayObjectExtensionContracts.SpawnAndActivate.Yaw),
                content.ParentEndPolicy);
            OrchestrationObjectRequestResult result = m_Backend.SpawnAndActivate(ref context, in request,
                invocation.InputRows.Row(0).Slice(GameplayObjectExtensionContracts.SpawnAndActivate.StartupInputOffset));
            if (result.Result == OrchestrationExtensionResult.Success)
                invocation.WriteOutput(in GameplayObjectExtensionContracts.SpawnAndActivate.Handle,
                    m_Backend.FromObjectHandle(result.Handle));
            return GameplayObjectExtensionExecution<TValue, TContext, TBackend>.Result(in m_Backend, in invocation,
                in result, in GameplayObjectExtensionContracts.SpawnAndActivate.Succeeded,
                in GameplayObjectExtensionContracts.SpawnAndActivate.FailureCode);
        }
    }

    public sealed class GameplayObjectNotifyParentExtension<TValue, TContext, TBackend> :
        CandidateOrchestrationExtension<TValue, TContext, GameplayObjectExtensionContracts.NotifyParent>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        readonly TBackend m_Backend;
        readonly GameplayObjectEventContent[] m_Contents;

        public GameplayObjectNotifyParentExtension(in TBackend backend, ReadOnlySpan<GameplayObjectEventContent> contents)
            : base(GameplayObjectExtensionContracts.NotifyParent.Definition)
        {
            m_Backend = backend;
            m_Contents = contents.ToArray();
        }

        public override OrchestrationExtensionResult Invoke(int contentIndex, ref TContext context,
            in OrchestrationExtensionInvocation<TValue, GameplayObjectExtensionContracts.NotifyParent> invocation)
        {
            OrchestrationObjectRequestOrigin origin = GameplayObjectExtensionExecution<TValue, TContext, TBackend>
                .Origin(in m_Backend, in context, in invocation);
            if (invocation.Kind != OrchestrationExtensionInvocationKind.Execute)
            {
                m_Backend.CancelRequest(ref context, in origin);
                return OrchestrationExtensionResult.Success;
            }
            ref readonly GameplayObjectEventContent content = ref m_Contents[contentIndex];
            OrchestrationObjectRequestResult result = m_Backend.NotifyParent(ref context, in origin,
                content.EventIdentityId, content.EventTagIdentityId);
            return GameplayObjectExtensionExecution<TValue, TContext, TBackend>.Result(in m_Backend, in invocation,
                in result, in GameplayObjectExtensionContracts.NotifyParent.Delivered,
                in GameplayObjectExtensionContracts.NotifyParent.FailureCode);
        }
    }

    public sealed class GameplayObjectCharacterEventExtension<TValue, TContext, TBackend> :
        CandidateOrchestrationExtension<TValue, TContext, GameplayObjectExtensionContracts.CharacterEvent>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        readonly TBackend m_Backend;
        readonly GameplayObjectEventContent[] m_Contents;

        public GameplayObjectCharacterEventExtension(in TBackend backend, ReadOnlySpan<GameplayObjectEventContent> contents)
            : base(GameplayObjectExtensionContracts.CharacterEvent.Definition)
        {
            m_Backend = backend;
            m_Contents = contents.ToArray();
        }

        public override OrchestrationExtensionResult Invoke(int contentIndex, ref TContext context,
            in OrchestrationExtensionInvocation<TValue, GameplayObjectExtensionContracts.CharacterEvent> invocation)
        {
            OrchestrationObjectRequestOrigin origin = GameplayObjectExtensionExecution<TValue, TContext, TBackend>
                .Origin(in m_Backend, in context, in invocation);
            if (invocation.Kind != OrchestrationExtensionInvocationKind.Execute)
            {
                m_Backend.CancelRequest(ref context, in origin);
                return OrchestrationExtensionResult.Success;
            }
            ref readonly GameplayObjectEventContent content = ref m_Contents[contentIndex];
            ObjectHandle recipient = m_Backend.ObjectHandle(in invocation.ReadInput(in GameplayObjectExtensionContracts.CharacterEvent.Recipient));
            OrchestrationObjectRequestResult result = m_Backend.SendCharacterEvent(ref context, in origin, recipient,
                content.EventIdentityId, content.EventTagIdentityId);
            return GameplayObjectExtensionExecution<TValue, TContext, TBackend>.Result(in m_Backend, in invocation,
                in result, in GameplayObjectExtensionContracts.CharacterEvent.Delivered,
                in GameplayObjectExtensionContracts.CharacterEvent.FailureCode);
        }
    }

    public sealed class GameplayObjectDestroyExtension<TValue, TContext, TBackend> :
        CandidateOrchestrationExtension<TValue, TContext, GameplayObjectExtensionContracts.Destroy>
        where TValue : unmanaged
        where TContext : struct
        where TBackend : struct, IOrchestrationObjectBackend<TValue, TContext>
    {
        readonly TBackend m_Backend;

        public GameplayObjectDestroyExtension(in TBackend backend) : base(GameplayObjectExtensionContracts.Destroy.Definition) =>
            m_Backend = backend;

        public override OrchestrationExtensionResult Invoke(int contentIndex, ref TContext context,
            in OrchestrationExtensionInvocation<TValue, GameplayObjectExtensionContracts.Destroy> invocation)
        {
            OrchestrationObjectRequestOrigin origin = GameplayObjectExtensionExecution<TValue, TContext, TBackend>
                .Origin(in m_Backend, in context, in invocation);
            if (invocation.Kind != OrchestrationExtensionInvocationKind.Execute)
            {
                m_Backend.CancelRequest(ref context, in origin);
                return OrchestrationExtensionResult.Success;
            }
            ObjectHandle target = m_Backend.ObjectHandle(in invocation.ReadInput(in GameplayObjectExtensionContracts.Destroy.Handle));
            OrchestrationObjectRequestResult result = m_Backend.Destroy(ref context, in origin, target);
            return GameplayObjectExtensionExecution<TValue, TContext, TBackend>.Result(in m_Backend, in invocation,
                in result, in GameplayObjectExtensionContracts.Destroy.Destroyed,
                in GameplayObjectExtensionContracts.Destroy.FailureCode);
        }
    }
}
