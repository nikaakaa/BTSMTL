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
            public static readonly OrchestrationExtensionOutputPort<Boolean> Value =
                new OrchestrationExtensionOutputPort<Boolean>("value", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<Boolean> Definition = new OrchestrationExtensionDefinition<Boolean>(
                new OrchestrationExtensionIdentity(DomainId, "configuration-bool"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Value.Declaration
                    }, new[] { Fields.Field, Fields.BooleanValue }));
        }

        public readonly struct Int32
        {
            public static readonly OrchestrationExtensionOutputPort<Int32> Value =
                new OrchestrationExtensionOutputPort<Int32>("value", OrchestrationValueKind.Int32, 0);

            public static readonly OrchestrationExtensionDefinition<Int32> Definition = new OrchestrationExtensionDefinition<Int32>(
                new OrchestrationExtensionIdentity(DomainId, "configuration-int32"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Value.Declaration
                    }, new[] { Fields.Field, Fields.Int32Value }));
        }

        public readonly struct UInt64
        {
            public static readonly OrchestrationExtensionOutputPort<UInt64> Value =
                new OrchestrationExtensionOutputPort<UInt64>("value", OrchestrationValueKind.UInt64, 0);

            public static readonly OrchestrationExtensionDefinition<UInt64> Definition = new OrchestrationExtensionDefinition<UInt64>(
                new OrchestrationExtensionIdentity(DomainId, "configuration-uint64"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Value.Declaration
                    }, new[] { Fields.Field, Fields.UInt64Value }));
        }

        public readonly struct Number
        {
            public static readonly OrchestrationExtensionOutputPort<Number> Value =
                new OrchestrationExtensionOutputPort<Number>("value", OrchestrationValueKind.Scalar, 0);

            public static readonly OrchestrationExtensionDefinition<Number> Definition = new OrchestrationExtensionDefinition<Number>(
                new OrchestrationExtensionIdentity(DomainId, "configuration-number"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Value.Declaration
                    }, new[] { Fields.Field, Fields.ScalarValue }));
        }

        public readonly struct Text
        {
            public static readonly OrchestrationExtensionOutputPort<Text> Value =
                new OrchestrationExtensionOutputPort<Text>("value", OrchestrationValueKind.Identity, 0);

            public static readonly OrchestrationExtensionDefinition<Text> Definition = new OrchestrationExtensionDefinition<Text>(
                new OrchestrationExtensionIdentity(DomainId, "configuration-text"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Value.Declaration
                    }, new[] { Fields.Field, Fields.TextValue }));
        }
    }
}
