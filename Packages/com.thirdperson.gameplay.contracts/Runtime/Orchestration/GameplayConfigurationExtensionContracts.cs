using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayConfigurationExtensionContracts
    {
        public const string DomainId = "gameplay.configuration";

        public static class Fields
        {
            public static readonly OrchestrationExtensionContentField Field =
                OrchestrationExtensionContentField.Identity("Field", "gameplay.configuration-field");
            public static readonly OrchestrationExtensionContentField BooleanValue =
                OrchestrationExtensionContentField.Value("Value", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionContentField Int32Value =
                OrchestrationExtensionContentField.Value("Value", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionContentField UInt64Value =
                OrchestrationExtensionContentField.Value("Value", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionContentField ScalarValue =
                OrchestrationExtensionContentField.Value("Value", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionContentField TextValue =
                OrchestrationExtensionContentField.Text("Value");
        }

        public readonly struct Boolean
        {
            static readonly OrchestrationExtensionPorts<Boolean> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<Boolean> Value =
                Ports.Output("value", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<Boolean> Definition =
                Read("configuration-bool", Ports, Fields.BooleanValue);
        }

        public readonly struct Int32
        {
            static readonly OrchestrationExtensionPorts<Int32> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<Int32> Value =
                Ports.Output("value", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionDefinition<Int32> Definition =
                Read("configuration-int32", Ports, Fields.Int32Value);
        }

        public readonly struct UInt64
        {
            static readonly OrchestrationExtensionPorts<UInt64> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<UInt64> Value =
                Ports.Output("value", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionDefinition<UInt64> Definition =
                Read("configuration-uint64", Ports, Fields.UInt64Value);
        }

        public readonly struct Number
        {
            static readonly OrchestrationExtensionPorts<Number> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<Number> Value =
                Ports.Output("value", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionDefinition<Number> Definition =
                Read("configuration-number", Ports, Fields.ScalarValue);
        }

        public readonly struct Text
        {
            static readonly OrchestrationExtensionPorts<Text> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<Text> Value =
                Ports.Output("value", OrchestrationValueKind.Identity);
            public static readonly OrchestrationExtensionDefinition<Text> Definition =
                Read("configuration-text", Ports, Fields.TextValue);
        }

        static OrchestrationExtensionDefinition<TOperation> Read<TOperation>(string operation,
            OrchestrationExtensionPorts<TOperation> ports, OrchestrationExtensionContentField value)
            where TOperation : struct =>
            new(new OrchestrationExtensionIdentity(DomainId, operation), 4,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate, ports.Seal(), new[] { Fields.Field, value }));
    }
}
