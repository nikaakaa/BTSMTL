namespace ThirdPersonSimulation.Fixed
{
    public static class FixedPresentationOutputPolicy
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
