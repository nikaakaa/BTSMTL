using System;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;

namespace ThirdPersonSimulation
{
    public static class GameplayAbilityOperationSet
    {
        public const string Id = "character-gameplay-operations";
        public static readonly OperationSetVersion Version = new OperationSetVersion(Id + "/34");

        public static void RequireVersion(OperationSetVersion version)
        {
            if (!version.Equals(Version))
                throw new InvalidOperationException($"Operation set '{version.Value}' is not supported; expected '{Version.Value}'.");
        }
    }

    public sealed class GameplayProgramStateSchema : ProgramStateSchema
    {
        public static readonly GameplayProgramStateSchema Instance = new GameplayProgramStateSchema();

        GameplayProgramStateSchema()
        {
        }

        public override string CodecIdentity(ProgramStateValueKind kind) =>
            kind == GameplayStateValueKinds.ActionTargetSnapshot ? "state.action-target-snapshot/v1" : base.CodecIdentity(kind);

        public override bool HasDefaultValue(ProgramStateValueKind kind) =>
            kind != GameplayStateValueKinds.ActionTargetSnapshot && base.HasDefaultValue(kind);

        public override OrchestrationValueKind RuntimeKind(ProgramStateValueKind kind) =>
            kind == GameplayStateValueKinds.ActionTargetSnapshot ? OrchestrationValueKind.SpatialIdentity : base.RuntimeKind(kind);

        protected override bool IsBlackboardValue(ProgramStateValueKind kind) =>
            kind == GameplayStateValueKinds.ActionTargetSnapshot || base.IsBlackboardValue(kind);
    }

    public enum ProgramOutputChannelKind : byte
    {
        GameplayFact = 1,
        Presentation = 2,
        Trace = 3
    }

    public sealed partial class ProgramOutputChannelLayout
    {
        public ProgramOutputChannelLayout(int index, string identity, ProgramOutputChannelKind kind)
        {
            Index = index;
            Identity = identity.Trim();
            Kind = kind;
            RequireRecord();
        }

        public int Index { get; }
        public string Identity { get; }
        public ProgramOutputChannelKind Kind { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0)
                throw new ArgumentOutOfRangeException(nameof(Index));
            SimulationIdentity.Require(Identity, nameof(Identity));
        }
    }

    public sealed partial class ProgramProducer
    {
        public ProgramProducer(
            int index,
            string identity,
            AnimationChannelId animationChannelId,
            string sourceIdentity,
            ProgramOutputChannelKind channelKind)
        {
            Index = index;
            Identity = identity.Trim();
            AnimationChannelId = animationChannelId;
            SourceIdentity = sourceIdentity.Trim();
            ChannelKind = channelKind;
            RequireRecord();
        }
        public int Index { get; }
        public string Identity { get; }
        public AnimationChannelId AnimationChannelId { get; }
        public string SourceIdentity { get; }
        public ProgramOutputChannelKind ChannelKind { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0)
                throw new ArgumentOutOfRangeException(nameof(Index));
            if (!AnimationChannelId.IsValid)
                throw new ArgumentException("Program Producer Animation Channel identity is invalid.");
            SimulationIdentity.Require(Identity, nameof(Identity));
            SimulationIdentity.Require(SourceIdentity, nameof(SourceIdentity));
        }
    }
}
