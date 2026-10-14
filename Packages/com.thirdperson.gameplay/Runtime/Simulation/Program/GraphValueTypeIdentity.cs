namespace ThirdPersonSimulation
{
    public static class GraphValueTypeIdentity
    {
        public static string Of(ProgramStateValueKind kind, string enumIdentity = "") =>
            kind == ProgramStateValueKind.Enum ? "Enum:" + enumIdentity : kind.ToString();
    }
}

