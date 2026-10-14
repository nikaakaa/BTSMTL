namespace ThirdPersonSimulation
{
    internal readonly struct Float32CharacterEvaluationInput
    {
        internal Float32CharacterEvaluationInput(Float32CharacterRuntime runtime, SimulationActorBinding actor,
            Float32CharacterRuntimeState state, SimulationTick tick, SimulationInput input,
            SimulationIngress[] ingress, int ingressCount, WorldBodyState body, bool diagnosticsEnabled,
            bool captureValues, bool captureControlFlow, bool captureBlackboard)
        {
            NumericProfile = runtime.NumericProfile;
            TickRate = runtime.TickRate;
            RequiredWorldCapabilities = runtime.RequiredWorldCapabilities;
            Control = runtime.ControlRuntime(actor.ActorId);
            State = state;
            Tick = tick;
            Input = input;
            Ingress = ingress;
            IngressCount = ingressCount;
            Body = body;
            DiagnosticsEnabled = diagnosticsEnabled;
            CaptureValues = captureValues;
            CaptureControlFlow = captureControlFlow;
            CaptureBlackboard = captureBlackboard;
        }
        internal readonly SimulationNumericProfile NumericProfile;
        internal readonly int TickRate;
        internal readonly WorldCapability RequiredWorldCapabilities;
        internal readonly Float32CharacterControlRuntime Control;
        internal readonly Float32CharacterRuntimeState State;
        internal readonly SimulationTick Tick;
        internal readonly SimulationInput Input;
        internal readonly SimulationIngress[] Ingress;
        internal readonly int IngressCount;
        internal readonly WorldBodyState Body;
        internal readonly bool DiagnosticsEnabled;
        internal readonly bool CaptureValues;
        internal readonly bool CaptureControlFlow;
        internal readonly bool CaptureBlackboard;
    }
}
