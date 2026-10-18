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

    public static class GameplayStateSemantics
    {
        public const ProgramStateSemantic BlackboardOwnerToken = (ProgramStateSemantic)61;
        public const ProgramStateSemantic BlackboardLifetime = (ProgramStateSemantic)63;
        public const ProgramStateSemantic BlackboardWriteStamp = (ProgramStateSemantic)64;
    }

    public sealed class GameplayProgramStateSchema : ProgramStateSchema
    {
        public static readonly GameplayProgramStateSchema Instance = new GameplayProgramStateSchema();

        GameplayProgramStateSchema()
        {
        }

        public override string SemanticIdentity(ProgramStateSemantic semantic) => semantic switch
        {
            GameplayStateSemantics.BlackboardOwnerToken => nameof(GameplayStateSemantics.BlackboardOwnerToken),
            GameplayStateSemantics.BlackboardLifetime => nameof(GameplayStateSemantics.BlackboardLifetime),
            GameplayStateSemantics.BlackboardWriteStamp => nameof(GameplayStateSemantics.BlackboardWriteStamp),
            _ => base.SemanticIdentity(semantic)
        };

        public override string CodecIdentity(ProgramStateValueKind kind) => kind switch
        {
            GameplayStateValueKinds.BlackboardOwnerToken => "state.blackboard-owner-token/v1",
            GameplayStateValueKinds.BlackboardWriteStamp => "state.blackboard-write-stamp/v3",
            GameplayStateValueKinds.ActionTargetSnapshot => "state.action-target-snapshot/v1",
            _ => base.CodecIdentity(kind)
        };

        public override bool HasDefaultValue(ProgramStateValueKind kind) =>
            kind != GameplayStateValueKinds.BlackboardOwnerToken &&
            kind != GameplayStateValueKinds.BlackboardWriteStamp &&
            kind != GameplayStateValueKinds.ActionTargetSnapshot;

        public override OrchestrationValueKind RuntimeKind(ProgramStateValueKind kind) =>
            kind == GameplayStateValueKinds.ActionTargetSnapshot ? OrchestrationValueKind.SpatialIdentity : base.RuntimeKind(kind);

        protected override bool Accepts(ProgramStateValueKind kind, ProgramStateOwnerKind owner, ProgramStateSemantic semantic) =>
            semantic switch
            {
                GameplayStateSemantics.BlackboardOwnerToken => kind == GameplayStateValueKinds.BlackboardOwnerToken && owner == ProgramStateOwnerKind.Blackboard,
                GameplayStateSemantics.BlackboardLifetime => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Blackboard,
                GameplayStateSemantics.BlackboardWriteStamp => kind == GameplayStateValueKinds.BlackboardWriteStamp && owner == ProgramStateOwnerKind.Blackboard,
                _ => base.Accepts(kind, owner, semantic)
            };

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
