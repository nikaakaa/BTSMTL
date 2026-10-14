using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
namespace ThirdPersonSimulation.FPGameplay
{
    internal readonly struct FPCharacterEvaluationInput
    {
        internal FPCharacterEvaluationInput(FPCharacterRuntime runtime, SimulationActorBinding actor,
            FPCharacterRuntimeState state, SimulationTick tick, SimulationInput input,
            SimulationIngress[] ingress, int ingressCount, WorldBodyState body, bool diagnosticsEnabled,
            bool captureValues, bool captureControlFlow, bool captureBlackboard, bool captureMotion,
            bool applyBlackboardCommands, FPCombatEvaluation combat)
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
            CaptureMotion = captureMotion;
            ApplyBlackboardCommands = applyBlackboardCommands;
            Combat = combat;
        }
        internal readonly SimulationNumericProfile NumericProfile;
        internal readonly int TickRate;
        internal readonly WorldCapability RequiredWorldCapabilities;
        internal readonly FPCharacterControlRuntime Control;
        internal readonly FPCharacterRuntimeState State;
        internal readonly SimulationTick Tick;
        internal readonly SimulationInput Input;
        internal readonly SimulationIngress[] Ingress;
        internal readonly int IngressCount;
        internal readonly WorldBodyState Body;
        internal readonly bool DiagnosticsEnabled;
        internal readonly bool CaptureValues;
        internal readonly bool CaptureControlFlow;
        internal readonly bool CaptureBlackboard;
        internal readonly bool CaptureMotion;
        internal readonly bool ApplyBlackboardCommands;
        internal readonly FPCombatEvaluation Combat;
    }
}
