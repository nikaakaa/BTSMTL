using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
namespace ThirdPersonSimulation.FPGameplay
{
    public static class FPPresentationOutputPolicy
    {
        public static bool RequiresConfirmation(PresentationCommandKind kind) =>
            kind == PresentationCommandKind.Cue ||
            kind == PresentationCommandKind.CombatHit ||
            kind == PresentationCommandKind.Vfx ||
            kind == PresentationCommandKind.Audio ||
            kind == PresentationCommandKind.Ui ||
            kind == PresentationCommandKind.CompleteProducer ||
            kind == PresentationCommandKind.ReleaseProducer ||
            kind == PresentationCommandKind.ForceProducer;
    }
}
