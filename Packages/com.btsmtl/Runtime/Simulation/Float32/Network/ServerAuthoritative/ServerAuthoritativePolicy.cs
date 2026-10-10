using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public enum ServerAuthoritativeHardRecoveryPolicy : byte
    {
        RestoreLatestCompleteBaseline = 1,
        FailSession = 2
    }

    public enum ServerAuthoritativeMissingInputPolicy : byte
    {
        ReuseLastCanonicalInput = 1,
        NeutralCanonicalInput = 2
    }

    [Flags]
    public enum ServerAuthoritativeReliableGameplayFactKinds : ushort
    {
        Action = 1 << 0,
        Effect = 1 << 1,
        Attribute = 1 << 2,
        Cue = 1 << 3,
        All = Action | Effect | Attribute | Cue
    }

    public sealed class ServerAuthoritativeReplicationPolicy
    {
        readonly ReadOnlyCollection<string> m_ReliableProducerIds;
        readonly HashSet<CanonicalTextValue> m_ReliableProducerSet;

        public ServerAuthoritativeReplicationPolicy(
            ServerAuthoritativeReliableGameplayFactKinds reliableGameplayFactKinds,
            IEnumerable<string> reliableProducerIds)
        {
            if (reliableGameplayFactKinds == 0 ||
                (reliableGameplayFactKinds & ~ServerAuthoritativeReliableGameplayFactKinds.All) != 0)
            {
                throw new ArgumentException("ServerAuthoritative reliable GameplayFact coverage is invalid.");
            }
            var producers = reliableProducerIds == null
                ? new List<string>()
                : new List<string>(reliableProducerIds);
            producers.Sort(StringComparer.Ordinal);
            for (int i = 0; i < producers.Count; i++)
            {
                producers[i] = RequireIdentity(producers[i]).ToString();
                if (i > 0 && CanonicalTextValue.Equals(producers[i - 1], producers[i], StringComparison.Ordinal))
                    throw new ArgumentException($"Duplicate ServerAuthoritative producer policy '{producers[i]}'.");
            }
            if (producers.Count == 0)
                throw new ArgumentException("ServerAuthoritative replication policy requires explicit producer coverage.");
            ReliableGameplayFactKinds = reliableGameplayFactKinds;
            m_ReliableProducerIds = producers.AsReadOnly();
            m_ReliableProducerSet = new HashSet<CanonicalTextValue>(producers.Count);
            for (int producer = 0; producer < producers.Count; producer++)
                m_ReliableProducerSet.Add(producers[producer]);
            var hashParts = new List<string>(producers.Count + 2)
            {
                "server-authoritative-replication-policy/1",
                ((ushort)reliableGameplayFactKinds).ToString(CultureInfo.InvariantCulture)
            };
            hashParts.AddRange(producers);
            ConfigurationHash = StableHash.Compute(hashParts.ToArray());
        }

        public ServerAuthoritativeReliableGameplayFactKinds ReliableGameplayFactKinds { get; }
        public IReadOnlyList<string> ReliableProducerIds => m_ReliableProducerIds;
        public StableHash ConfigurationHash { get; }

        public void RequireCharacterCoverage(Float32CharacterRuntime characterRuntime)
        {
            if (characterRuntime == null)
                throw new ArgumentNullException(nameof(characterRuntime));
            ServerAuthoritativeReliableGameplayFactKinds requiredFacts = RequiredFacts(characterRuntime);
            if ((ReliableGameplayFactKinds & requiredFacts) != requiredFacts)
            {
                ServerAuthoritativeReliableGameplayFactKinds missing = requiredFacts & ~ReliableGameplayFactKinds;
                throw new InvalidOperationException($"ServerAuthoritative replication policy is missing GameplayFact coverage '{missing}'.");
            }
            var producers = new HashSet<string>(StringComparer.Ordinal);
            for (int abilityIndex = 0; abilityIndex < characterRuntime.Abilities.Count; abilityIndex++)
                for (int producerIndex = 0; producerIndex < characterRuntime.Abilities[abilityIndex].Producers.Count; producerIndex++)
                    producers.Add(characterRuntime.Abilities[abilityIndex].Producers[producerIndex].Identity);
            if (producers.Count != m_ReliableProducerIds.Count)
                throw new InvalidOperationException("ServerAuthoritative producer replication policy does not exactly cover the Character producer catalog.");
            foreach (string producer in producers)
                if (!m_ReliableProducerSet.Contains(producer))
                    throw new InvalidOperationException($"ServerAuthoritative replication policy is missing producer '{producer}'.");
        }

        static ServerAuthoritativeReliableGameplayFactKinds RequiredFacts(Float32CharacterRuntime characterRuntime)
        {
            ServerAuthoritativeReliableGameplayFactKinds required = 0;
            for (int abilityIndex = 0; abilityIndex < characterRuntime.Abilities.Count; abilityIndex++)
            {
                IReadOnlyList<ProgramCatalogEntry> entries = characterRuntime.Abilities[abilityIndex].CatalogEntries;
                for (int i = 0; i < entries.Count; i++)
                {
                    switch (entries[i].Kind)
                    {
                        case ProgramCatalogEntryKind.Action:
                            required |= ServerAuthoritativeReliableGameplayFactKinds.Action;
                            break;
                        case ProgramCatalogEntryKind.GameplayEffect:
                            required |= ServerAuthoritativeReliableGameplayFactKinds.Effect |
                                ServerAuthoritativeReliableGameplayFactKinds.Cue;
                            break;
                        case ProgramCatalogEntryKind.Attribute:
                            required |= ServerAuthoritativeReliableGameplayFactKinds.Attribute;
                            break;
                        case ProgramCatalogEntryKind.TimelineClip:
                            required |= ServerAuthoritativeReliableGameplayFactKinds.Cue;
                            break;
                    }
                }
            }
            return required;
        }

        public bool ShouldReplicateReliably(GameplayFact fact)
        {
            ServerAuthoritativeReliableGameplayFactKinds kind = fact.Kind switch
            {
                GameplayFactKind.Action => ServerAuthoritativeReliableGameplayFactKinds.Action,
                GameplayFactKind.Effect => ServerAuthoritativeReliableGameplayFactKinds.Effect,
                GameplayFactKind.Attribute => ServerAuthoritativeReliableGameplayFactKinds.Attribute,
                GameplayFactKind.Cue => ServerAuthoritativeReliableGameplayFactKinds.Cue,
                GameplayFactKind.Motion => 0,
                GameplayFactKind.State => 0,
                GameplayFactKind.Equipment => 0,
                GameplayFactKind.ActionWindow => 0,
                GameplayFactKind.Tag => 0,
                GameplayFactKind.Failure => 0,
                GameplayFactKind.Reconciliation => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(fact), fact.Kind, null)
            };
            if (kind == 0)
                return false;
            if ((ReliableGameplayFactKinds & kind) == 0)
                throw new InvalidOperationException($"ServerAuthoritative replication policy has no reliable mapping for GameplayFact kind '{fact.Kind}'.");
            return true;
        }

        public bool ShouldReplicateReliably(
            PresentationCommand command,
            Float32CharacterRuntime characterRuntime)
        {
            if (!m_ReliableProducerSet.Contains(command.ProducerId))
                throw new InvalidOperationException($"ServerAuthoritative replication policy has no producer mapping for '{command.ProducerId}'.");
            if (IsCameraProducer(characterRuntime, command.ProducerId))
                return false;
            return command.Kind == PresentationCommandKind.TimelineProgress ||
                   command.Kind == PresentationCommandKind.SelectProducer ||
                   command.Kind == PresentationCommandKind.CompleteProducer ||
                   command.Kind == PresentationCommandKind.ReleaseProducer ||
                   command.Kind == PresentationCommandKind.ForceReleaseProducer ||
                   command.Kind == PresentationCommandKind.Cue ||
                   command.Kind == PresentationCommandKind.Vfx ||
                   command.Kind == PresentationCommandKind.Ui;
        }

        public bool ShouldStream(
            PresentationCommand command,
            Float32CharacterRuntime characterRuntime)
        {
            if (!m_ReliableProducerSet.Contains(command.ProducerId))
                throw new InvalidOperationException($"ServerAuthoritative replication policy has no producer mapping for '{command.ProducerId}'.");
            return command.Kind == PresentationCommandKind.SampleProducer &&
                !IsCameraProducer(characterRuntime, command.ProducerId);
        }

        public static bool IsCameraProducer(
            Float32CharacterRuntime characterRuntime,
            CanonicalTextValue producerId)
        {
            if (characterRuntime == null)
                throw new ArgumentNullException(nameof(characterRuntime));
            CanonicalTextValue identity = RequireIdentity(producerId);
            bool found = false;
            bool camera = false;
            for (int abilityIndex = 0; abilityIndex < characterRuntime.Abilities.Count; abilityIndex++)
            {
                IReadOnlyList<ProgramProducer> producers = characterRuntime.Abilities[abilityIndex].Producers;
                for (int i = 0; i < producers.Count; i++)
                {
                    ProgramProducer producer = producers[i];
                    if (!CanonicalTextValue.Equals(producer.Identity, identity, StringComparison.Ordinal))
                        continue;
                    bool candidate = producer.ChannelKind == ProgramOutputChannelKind.Presentation &&
                        producer.AnimationChannelId == CameraProgramOperationSchema.ChannelId;
                    if (found && camera != candidate)
                        throw new InvalidOperationException($"Character producer '{identity}' has conflicting Camera ownership.");
                    found = true;
                    camera = candidate;
                }
            }
            if (!found)
                throw new InvalidOperationException($"Character has no presentation producer mapping for '{identity}'.");
            return camera;
        }

        static CanonicalTextValue RequireIdentity(CanonicalTextValue value)
        {
            if (CanonicalTextValue.IsNullOrWhiteSpace(value))
                throw new ArgumentException("ServerAuthoritative producer policy identity is required.");
            return value.Trim();
        }
    }

    public sealed class ServerAuthoritativeModelPolicy
    {
        public ServerAuthoritativeModelPolicy(
            int simulationTickRate,
            int commandPacketRate,
            int snapshotPacketRate,
            int commandSlackTicks,
            int maximumRemoteBodyExtrapolationTicks,
            int maxGameplayDatagramBytes,
            int historyCapacity,
            int maximumInputLeadTicks,
            int maximumInputLagTicks,
            int maximumReplayTicksPerOuterTick,
            float bodyPositionTolerance,
            float bodyYawToleranceDegrees,
            ServerAuthoritativeHardRecoveryPolicy hardRecoveryPolicy,
            ServerAuthoritativeMissingInputPolicy missingInputPolicy)
        {
            if (simulationTickRate < 20 || simulationTickRate > 240 ||
                commandPacketRate <= 0 || commandPacketRate > simulationTickRate || simulationTickRate % commandPacketRate != 0 ||
                snapshotPacketRate <= 0 || snapshotPacketRate > simulationTickRate || simulationTickRate % snapshotPacketRate != 0 ||
                commandSlackTicks <= 0 || maximumRemoteBodyExtrapolationTicks <= 0 ||
                maxGameplayDatagramBytes < 256 || maxGameplayDatagramBytes > 1200 ||
                historyCapacity <= commandSlackTicks + maximumRemoteBodyExtrapolationTicks ||
                maximumInputLeadTicks < commandSlackTicks || maximumInputLagTicks < 3 ||
                maximumReplayTicksPerOuterTick <= 0 || historyCapacity <= maximumReplayTicksPerOuterTick ||
                bodyPositionTolerance < 0f || bodyYawToleranceDegrees < 0f ||
                !Enum.IsDefined(typeof(ServerAuthoritativeHardRecoveryPolicy), hardRecoveryPolicy) ||
                !Enum.IsDefined(typeof(ServerAuthoritativeMissingInputPolicy), missingInputPolicy))
            {
                throw new ArgumentException("ServerAuthoritative model policy is incomplete.");
            }
            SimulationTickRate = simulationTickRate;
            CommandPacketRate = commandPacketRate;
            SnapshotPacketRate = snapshotPacketRate;
            CommandSlackTicks = commandSlackTicks;
            MaximumRemoteBodyExtrapolationTicks = maximumRemoteBodyExtrapolationTicks;
            MaxGameplayDatagramBytes = maxGameplayDatagramBytes;
            HistoryCapacity = historyCapacity;
            MaximumInputLeadTicks = maximumInputLeadTicks;
            MaximumInputLagTicks = maximumInputLagTicks;
            MaximumReplayTicksPerOuterTick = maximumReplayTicksPerOuterTick;
            BodyPositionTolerance = bodyPositionTolerance;
            BodyYawToleranceDegrees = bodyYawToleranceDegrees;
            HardRecoveryPolicy = hardRecoveryPolicy;
            MissingInputPolicy = missingInputPolicy;
            ConfigurationHash = StableHash.Compute(
                "server-authoritative-model-policy/5",
                simulationTickRate.ToString(CultureInfo.InvariantCulture),
                commandPacketRate.ToString(CultureInfo.InvariantCulture),
                snapshotPacketRate.ToString(CultureInfo.InvariantCulture),
                commandSlackTicks.ToString(CultureInfo.InvariantCulture),
                maximumRemoteBodyExtrapolationTicks.ToString(CultureInfo.InvariantCulture),
                maxGameplayDatagramBytes.ToString(CultureInfo.InvariantCulture),
                historyCapacity.ToString(CultureInfo.InvariantCulture),
                maximumInputLeadTicks.ToString(CultureInfo.InvariantCulture),
                maximumInputLagTicks.ToString(CultureInfo.InvariantCulture),
                maximumReplayTicksPerOuterTick.ToString(CultureInfo.InvariantCulture),
                bodyPositionTolerance.ToString("R", CultureInfo.InvariantCulture),
                bodyYawToleranceDegrees.ToString("R", CultureInfo.InvariantCulture),
                ((int)hardRecoveryPolicy).ToString(CultureInfo.InvariantCulture),
                ((int)missingInputPolicy).ToString(CultureInfo.InvariantCulture));
        }

        public int SimulationTickRate { get; }
        public int CommandPacketRate { get; }
        public int SnapshotPacketRate { get; }
        public int CommandSlackTicks { get; }
        public int MaximumRemoteBodyExtrapolationTicks { get; }
        public int MaxGameplayDatagramBytes { get; }
        public int HistoryCapacity { get; }
        public int MaximumInputLeadTicks { get; }
        public int MaximumInputLagTicks { get; }
        public int MaximumReplayTicksPerOuterTick { get; }
        public GameplayEffectScheduleCapacity PredictionGameplayEffectCapacity =>
            new GameplayEffectScheduleCapacity(checked(MaximumReplayTicksPerOuterTick + 2), 0,
                HistoryCapacity, HistoryCapacity, HistoryCapacity, HistoryCapacity);
        public GameplayEffectScheduleCapacity AuthorityGameplayEffectCapacity =>
            new GameplayEffectScheduleCapacity(1, 0, 0, 0, 0, MaximumInputLagTicks);
        public float BodyPositionTolerance { get; }
        public float BodyYawToleranceDegrees { get; }
        public ServerAuthoritativeHardRecoveryPolicy HardRecoveryPolicy { get; }
        public ServerAuthoritativeMissingInputPolicy MissingInputPolicy { get; }
        public StableHash ConfigurationHash { get; }
    }
}
