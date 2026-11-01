using System;
using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayObjectExtensionContracts
    {
        public const string DomainId = "gameplay.objects";

        public static class Fields
        {
            public static readonly OrchestrationExtensionContentField Ability =
                OrchestrationExtensionContentField.Identity("Ability", "gameplay.ability");
            public static readonly OrchestrationExtensionContentField CarrierDefinition =
                OrchestrationExtensionContentField.Identity("CarrierDefinition", "gameplay.carrier-definition");
            public static readonly OrchestrationExtensionContentField ParentEndPolicy =
                OrchestrationExtensionContentField.Enum<OrchestrationObjectParentEndPolicy>("ParentEndPolicy");
            public static readonly OrchestrationExtensionContentField ChildEndPolicy =
                OrchestrationExtensionContentField.Enum<OrchestrationObjectChildEndPolicy>("ChildEndPolicy");
            public static readonly OrchestrationExtensionContentField Event =
                OrchestrationExtensionContentField.Identity("Event", "gameplay.action-event");
            public static readonly OrchestrationExtensionContentField EventTag =
                OrchestrationExtensionContentField.Identity("EventTag", "gameplay.tag");
            public static readonly OrchestrationExtensionContentField InputDeclaration =
                OrchestrationExtensionContentField.Identity("InputDeclaration", "gameplay.startup-input");
            public static readonly OrchestrationExtensionContentField InputIndex =
                OrchestrationExtensionContentField.Value("InputIndex", OrchestrationValueKind.Int32);
        }

        public readonly struct Facts
        {
            public static readonly OrchestrationExtensionOutputPort<Facts> Carrier = new("carrier", OrchestrationValueKind.ObjectHandle, 0);
            public static readonly OrchestrationExtensionOutputPort<Facts> Initiator = new("initiator", OrchestrationValueKind.ObjectHandle, 1);
            public static readonly OrchestrationExtensionDefinition<Facts> Definition = Read<Facts>("object-facts",
                new[] { Carrier.Declaration, Initiator.Declaration });
        }

        public readonly struct Exists
        {
            public static readonly OrchestrationExtensionInputPort<Exists> Handle = new("handle", OrchestrationValueKind.ObjectHandle, 0);
            public static readonly OrchestrationExtensionOutputPort<Exists> Result = new("exists", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionDefinition<Exists> Definition = Read<Exists>("object-exists",
                new[] { Handle.Declaration, Result.Declaration });
        }

        public readonly struct Pose
        {
            public static readonly OrchestrationExtensionInputPort<Pose> Handle = new("handle", OrchestrationValueKind.ObjectHandle, 0);
            public static readonly OrchestrationExtensionOutputPort<Pose> Valid = new("valid", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<Pose> Position = new("position", OrchestrationValueKind.Vector3, 1);
            public static readonly OrchestrationExtensionOutputPort<Pose> Yaw = new("yaw", OrchestrationValueKind.Scalar, 2);
            public static readonly OrchestrationExtensionDefinition<Pose> Definition = Read<Pose>("object-pose",
                new[] { Handle.Declaration, Valid.Declaration, Position.Declaration, Yaw.Declaration });
        }

        public readonly struct StartupInput
        {
            public const string ValuePortId = "value";
            public static readonly OrchestrationExtensionDefinition<StartupInput> Definition = Read<StartupInput>("startup-input",
                Array.Empty<OrchestrationExtensionPort>(), new[] { Fields.InputDeclaration, Fields.InputIndex });
        }

        public readonly struct SpawnAndActivate
        {
            public const int StartupInputOffset = 2;
            public static readonly OrchestrationExtensionInputPort<SpawnAndActivate> Position = new("position", OrchestrationValueKind.Vector3, 0);
            public static readonly OrchestrationExtensionInputPort<SpawnAndActivate> Yaw = new("yaw", OrchestrationValueKind.Scalar, 1);
            public static readonly OrchestrationExtensionOutputPort<SpawnAndActivate> Succeeded = new("succeeded", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<SpawnAndActivate> Handle = new("handle", OrchestrationValueKind.ObjectHandle, 1);
            public static readonly OrchestrationExtensionOutputPort<SpawnAndActivate> FailureCode = new("failure-code", OrchestrationValueKind.Int32, 2);
            public static readonly OrchestrationExtensionDefinition<SpawnAndActivate> Definition = Write<SpawnAndActivate>("spawn-and-activate",
                new[] { Position.Declaration, Yaw.Declaration, Succeeded.Declaration, Handle.Declaration, FailureCode.Declaration },
                new[] { Fields.Ability, Fields.CarrierDefinition, Fields.ParentEndPolicy, Fields.ChildEndPolicy });
        }

        public readonly struct NotifyParent
        {
            public static readonly OrchestrationExtensionOutputPort<NotifyParent> Delivered = new("delivered", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<NotifyParent> FailureCode = new("failure-code", OrchestrationValueKind.Int32, 1);
            public static readonly OrchestrationExtensionDefinition<NotifyParent> Definition = Write<NotifyParent>("notify-parent-instance",
                new[] { Delivered.Declaration, FailureCode.Declaration }, new[] { Fields.Event, Fields.EventTag });
        }

        public readonly struct CharacterEvent
        {
            public static readonly OrchestrationExtensionInputPort<CharacterEvent> Recipient = new("recipient", OrchestrationValueKind.ObjectHandle, 0);
            public static readonly OrchestrationExtensionOutputPort<CharacterEvent> Delivered = new("delivered", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<CharacterEvent> FailureCode = new("failure-code", OrchestrationValueKind.Int32, 1);
            public static readonly OrchestrationExtensionDefinition<CharacterEvent> Definition = Write<CharacterEvent>("send-character-event",
                new[] { Recipient.Declaration, Delivered.Declaration, FailureCode.Declaration }, new[] { Fields.Event, Fields.EventTag });
        }

        public readonly struct Destroy
        {
            public static readonly OrchestrationExtensionInputPort<Destroy> Handle = new("handle", OrchestrationValueKind.ObjectHandle, 0);
            public static readonly OrchestrationExtensionOutputPort<Destroy> Destroyed = new("destroyed", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<Destroy> FailureCode = new("failure-code", OrchestrationValueKind.Int32, 1);
            public static readonly OrchestrationExtensionDefinition<Destroy> Definition = Write<Destroy>("destroy-object",
                new[] { Handle.Declaration, Destroyed.Declaration, FailureCode.Declaration }, Array.Empty<OrchestrationExtensionContentField>());
        }

        static OrchestrationExtensionDefinition<TOperation> Read<TOperation>(string operation,
            OrchestrationExtensionPort[] ports, OrchestrationExtensionContentField[] fields = null) where TOperation : struct =>
            new(new OrchestrationExtensionIdentity(DomainId, operation), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value, OrchestrationExtensionAccess.ReadOnly,
                    OrchestrationExtensionInvocationKind.Evaluate, OrchestrationExtensionCompletion.Immediate,
                    ports, fields ?? Array.Empty<OrchestrationExtensionContentField>()));

        static OrchestrationExtensionDefinition<TOperation> Write<TOperation>(string operation,
            OrchestrationExtensionPort[] ports, OrchestrationExtensionContentField[] fields) where TOperation : struct =>
            new(new OrchestrationExtensionIdentity(DomainId, operation), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action, OrchestrationExtensionAccess.CandidateWrite,
                    OrchestrationExtensionInvocationKind.Execute | OrchestrationExtensionInvocationKind.RequestStop |
                    OrchestrationExtensionInvocationKind.ContinueStop | OrchestrationExtensionInvocationKind.ForceStop,
                    OrchestrationExtensionCompletion.Resumable, ports, fields));
    }
}
