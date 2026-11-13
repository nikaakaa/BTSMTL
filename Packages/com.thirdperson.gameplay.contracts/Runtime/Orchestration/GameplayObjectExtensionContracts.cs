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
            static readonly OrchestrationExtensionPorts<Facts> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<Facts> Carrier = Ports.Output("carrier", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Carrier);
            public static readonly OrchestrationExtensionOutputPort<Facts> Initiator = Ports.Output("initiator", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionDefinition<Facts> Definition = Read("object-facts", Ports);
        }

        public readonly struct Exists
        {
            static readonly OrchestrationExtensionPorts<Exists> Ports = new();
            public static readonly OrchestrationExtensionInputPort<Exists> Handle = Ports.Input("handle", OrchestrationValueKind.ObjectHandle, OrchestrationObjectKind.Any);
            public static readonly OrchestrationExtensionOutputPort<Exists> Result = Ports.Output("exists", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<Exists> Definition = Read("object-exists", Ports);
        }

        public readonly struct Pose
        {
            static readonly OrchestrationExtensionPorts<Pose> Ports = new();
            public static readonly OrchestrationExtensionInputPort<Pose> Handle = Ports.Input("handle", OrchestrationValueKind.ObjectHandle, OrchestrationObjectKind.Any);
            public static readonly OrchestrationExtensionOutputPort<Pose> Valid = Ports.Output("valid", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<Pose> Position = Ports.Output("position", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionOutputPort<Pose> Yaw = Ports.Output("yaw", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionDefinition<Pose> Definition = Read("object-pose", Ports);
        }

        // 按句柄当前格子的种类显式窄化；空、失效或种类不符时成功为假、输出空句柄。
        public readonly struct CastToActor
        {
            static readonly OrchestrationExtensionPorts<CastToActor> Ports = new();
            public static readonly OrchestrationExtensionInputPort<CastToActor> Handle = Ports.Input("handle", OrchestrationValueKind.ObjectHandle, OrchestrationObjectKind.Any);
            public static readonly OrchestrationExtensionOutputPort<CastToActor> Succeeded = Ports.Output("succeeded", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<CastToActor> Result = Ports.Output("result", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionDefinition<CastToActor> Definition = Read("object-cast-actor", Ports);
        }

        public readonly struct CastToCarrier
        {
            static readonly OrchestrationExtensionPorts<CastToCarrier> Ports = new();
            public static readonly OrchestrationExtensionInputPort<CastToCarrier> Handle = Ports.Input("handle", OrchestrationValueKind.ObjectHandle, OrchestrationObjectKind.Any);
            public static readonly OrchestrationExtensionOutputPort<CastToCarrier> Succeeded = Ports.Output("succeeded", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<CastToCarrier> Result = Ports.Output("result", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Carrier);
            public static readonly OrchestrationExtensionDefinition<CastToCarrier> Definition = Read("object-cast-carrier", Ports);
        }

        // 值输出端口的种类随所选启动输入声明变化，由每份内容追加一次。
        public readonly struct StartupInput
        {
            public static readonly OrchestrationExtensionDefinition<StartupInput> Definition = Read("startup-input",
                new OrchestrationExtensionPorts<StartupInput>(), Fields.InputDeclaration, Fields.InputIndex);

            public static OrchestrationExtensionContract Contract(OrchestrationValueKind valueKind,
                out OrchestrationExtensionOutputPort<StartupInput> value)
            {
                var ports = new OrchestrationExtensionPorts<StartupInput>();
                value = ports.Output("value", valueKind,
                    valueKind == OrchestrationValueKind.ObjectHandle ? OrchestrationObjectKind.Any : null);
                return Definition.Declaration.WithAdditionalPorts(ports.Seal()).Contract;
            }
        }

        // 启动输入端口由技能实例声明追加在固定输入之后。
        public readonly struct SpawnAndActivate
        {
            static readonly OrchestrationExtensionPorts<SpawnAndActivate> Ports = new();
            public static readonly OrchestrationExtensionInputPort<SpawnAndActivate> Position = Ports.Input("position", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionInputPort<SpawnAndActivate> Yaw = Ports.Input("yaw", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionOutputPort<SpawnAndActivate> Succeeded = Ports.Output("succeeded", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<SpawnAndActivate> Handle = Ports.Output("handle", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Carrier);
            public static readonly OrchestrationExtensionOutputPort<SpawnAndActivate> FailureCode = Ports.Output("failure-code", OrchestrationValueKind.Int32);
            public static readonly int StartupInputOffset = Ports.InputCount;
            public static readonly OrchestrationExtensionDefinition<SpawnAndActivate> Definition = Write("spawn-and-activate", Ports,
                Fields.Ability, Fields.CarrierDefinition, Fields.ParentEndPolicy);
        }

        public readonly struct NotifyParent
        {
            static readonly OrchestrationExtensionPorts<NotifyParent> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<NotifyParent> Delivered = Ports.Output("delivered", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<NotifyParent> FailureCode = Ports.Output("failure-code", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionDefinition<NotifyParent> Definition = Write("notify-parent-instance", Ports,
                Fields.Event, Fields.EventTag);
        }

        public readonly struct CharacterEvent
        {
            static readonly OrchestrationExtensionPorts<CharacterEvent> Ports = new();
            public static readonly OrchestrationExtensionInputPort<CharacterEvent> Recipient = Ports.Input("recipient", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<CharacterEvent> Delivered = Ports.Output("delivered", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<CharacterEvent> FailureCode = Ports.Output("failure-code", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionDefinition<CharacterEvent> Definition = Write("send-character-event", Ports,
                Fields.Event, Fields.EventTag);
        }

        public readonly struct Destroy
        {
            static readonly OrchestrationExtensionPorts<Destroy> Ports = new();
            public static readonly OrchestrationExtensionInputPort<Destroy> Handle = Ports.Input("handle", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Carrier);
            public static readonly OrchestrationExtensionOutputPort<Destroy> Destroyed = Ports.Output("destroyed", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<Destroy> FailureCode = Ports.Output("failure-code", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionDefinition<Destroy> Definition = Write("destroy-object", Ports);
        }

        static OrchestrationExtensionDefinition<TOperation> Read<TOperation>(string operation,
            OrchestrationExtensionPorts<TOperation> ports, params OrchestrationExtensionContentField[] fields)
            where TOperation : struct =>
            new(new OrchestrationExtensionIdentity(DomainId, operation), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value, OrchestrationExtensionAccess.ReadOnly,
                    OrchestrationExtensionInvocationKind.Evaluate, OrchestrationExtensionCompletion.Immediate,
                    ports.Seal(), fields));

        static OrchestrationExtensionDefinition<TOperation> Write<TOperation>(string operation,
            OrchestrationExtensionPorts<TOperation> ports, params OrchestrationExtensionContentField[] fields)
            where TOperation : struct =>
            new(new OrchestrationExtensionIdentity(DomainId, operation), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action, OrchestrationExtensionAccess.CandidateWrite,
                    OrchestrationExtensionInvocationKind.Execute | OrchestrationExtensionInvocationKind.RequestStop |
                    OrchestrationExtensionInvocationKind.ContinueStop | OrchestrationExtensionInvocationKind.ForceStop,
                    OrchestrationExtensionCompletion.Resumable, ports.Seal(), fields));
    }
}
