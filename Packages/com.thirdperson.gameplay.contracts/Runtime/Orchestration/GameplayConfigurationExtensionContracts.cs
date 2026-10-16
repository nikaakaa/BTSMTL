using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayConfigurationExtensionContracts
    {
        public const string DomainId = "gameplay.configuration";

        public static class Ports
        {
            public static readonly OrchestrationExtensionOutputPort Boolean =
                new OrchestrationExtensionOutputPort("value", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort Int32 =
                new OrchestrationExtensionOutputPort("value", OrchestrationValueKind.Int32, 0);
            public static readonly OrchestrationExtensionOutputPort UInt64 =
                new OrchestrationExtensionOutputPort("value", OrchestrationValueKind.UInt64, 0);
            public static readonly OrchestrationExtensionOutputPort Scalar =
                new OrchestrationExtensionOutputPort("value", OrchestrationValueKind.Scalar, 0);
            public static readonly OrchestrationExtensionOutputPort Text =
                new OrchestrationExtensionOutputPort("value", OrchestrationValueKind.Identity, 0);
        }

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

        public static readonly OrchestrationExtensionDefinition Boolean = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "configuration-bool"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Boolean.Declaration
                }, new[] { Fields.Field, Fields.BooleanValue }));

        public static readonly OrchestrationExtensionDefinition Int32 = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "configuration-int32"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Int32.Declaration
                }, new[] { Fields.Field, Fields.Int32Value }));

        public static readonly OrchestrationExtensionDefinition UInt64 = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "configuration-uint64"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.UInt64.Declaration
                }, new[] { Fields.Field, Fields.UInt64Value }));

        public static readonly OrchestrationExtensionDefinition Number = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "configuration-number"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Scalar.Declaration
                }, new[] { Fields.Field, Fields.ScalarValue }));

        public static readonly OrchestrationExtensionDefinition Text = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "configuration-text"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Text.Declaration
                }, new[] { Fields.Field, Fields.TextValue }));
    }
}
