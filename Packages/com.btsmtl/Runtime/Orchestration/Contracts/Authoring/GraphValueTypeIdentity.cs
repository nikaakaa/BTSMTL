using BTSMTL.Orchestration;

namespace ThirdPersonSimulation
{
    public static class GraphValueTypeIdentity
    {
        public static string Of(OrchestrationValueKind kind, string enumIdentity = "") =>
            kind == OrchestrationValueKind.Enum ? "Enum:" + enumIdentity : kind.ToString();
    }
}

