using System;

namespace ThirdPersonSimulation
{
    public sealed class CharacterBodyMotionBinding
    {
        public CharacterBodyMotionBinding(
            string sourceIdentity,
            StableHash contentRevision,
            int semanticVersion,
            double gravityAcceleration,
            double maximumFallSpeed)
        {
            SourceIdentity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            if (!contentRevision.IsValid)
                throw new ArgumentException("Body Motion content revision is required.", nameof(contentRevision));
            if (semanticVersion != 1)
                throw new ArgumentOutOfRangeException(nameof(semanticVersion), "Body Motion semantic version is unsupported.");
            if (double.IsNaN(gravityAcceleration) || double.IsInfinity(gravityAcceleration) || gravityAcceleration >= 0d)
                throw new ArgumentOutOfRangeException(nameof(gravityAcceleration));
            if (double.IsNaN(maximumFallSpeed) || double.IsInfinity(maximumFallSpeed) || maximumFallSpeed <= 0d)
                throw new ArgumentOutOfRangeException(nameof(maximumFallSpeed));

            ContentRevision = contentRevision;
            SemanticVersion = semanticVersion;
            GravityAcceleration = gravityAcceleration;
            MaximumFallSpeed = maximumFallSpeed;
            BindingHash = StableHash.Compute(
                "character-body-motion-binding/1",
                SourceIdentity,
                ContentRevision.Value,
                SemanticVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                GravityAcceleration.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                MaximumFallSpeed.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        public string SourceIdentity { get; }
        public StableHash ContentRevision { get; }
        public int SemanticVersion { get; }
        public double GravityAcceleration { get; }
        public double MaximumFallSpeed { get; }
        public WorldCapability RequiredWorldCapability => WorldCapability.AirborneVerticalMotion;
        public StableHash BindingHash { get; }
    }
}
