using System;
using ThirdPersonGameplay.Configuration;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedAbilityExecutionContext
    {
        public FixedAbilityExecutionContext(
            FixedGameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout,
            FixedGameplayAbilityExecutionServices services)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public FixedGameplayAbilityExecutionData Data { get; }
        public GameplayAbilityExecutionLayout Layout { get; }
        public FixedGameplayAbilityExecutionServices Services { get; }
    }

    public sealed class FixedGameplayAbilityExecutionInstallation
    {
        readonly FixedAbilityExecutionContext m_Execution;
        readonly EquipmentProgramLayout m_EquipmentLayout;

        internal FixedGameplayAbilityExecutionInstallation(
            FixedGameplayAbilityExecutionData data,
            GameplayAbilityGrantInputBinding inputBinding,
            FixedGameplayEffectRuntimeCatalog gameplayEffectCatalog,
            EquipmentProgramLayout equipmentLayout,
            GeneratedBlackboardConfiguration<AbilityStateValue> blackboards,
            FixedCombatAttackCatalog combatCatalog = null,
            GameplayConfigurationRuntimeBinding configurations = null)
        {
            data = data ?? throw new ArgumentNullException(nameof(data));
            data = data.WithInputBinding(in inputBinding);
            bool requiresEquipment = data.Capabilities.HasGameplayCapability("Equipment");
            bool requiresTagCatalog = FixedGameplayAbilityExecutionServices.RequiresTagCatalog(data);
            if (requiresTagCatalog && gameplayEffectCatalog == null)
                throw new ArgumentNullException(nameof(gameplayEffectCatalog));
            if (requiresEquipment && equipmentLayout == null)
                throw new ArgumentNullException(nameof(equipmentLayout));
            GameplayAbilityExecutionLayout layout = data.ExecutionLayout;
            FixedGameplayEffectRuntimeCatalog executionGameplayEffectCatalog = requiresTagCatalog
                ? gameplayEffectCatalog
                : null;
            EquipmentProgramLayout executionEquipmentLayout = requiresEquipment
                ? equipmentLayout
                : null;
            FixedGameplayAbilityExecutionServices services = new FixedGameplayAbilityExecutionServices(
                data,
                layout,
                BuildOperationSourcePaths(layout),
                executionGameplayEffectCatalog,
                blackboards, combatCatalog, configurations);
            m_EquipmentLayout = executionEquipmentLayout;
            m_Execution = new FixedAbilityExecutionContext(
                data,
                layout,
                services);
        }

        public void AppendBlackboardSnapshot(FixedCharacterRuntimeState state,
            System.Collections.Generic.List<FixedBlackboardValueSnapshot> destination) =>
            FixedBlackboardSnapshotReader.Append(this, state, destination);

        public FixedGameplayAbilityExecutionData Data => m_Execution.Data;
        public GameplayAbilityExecutionLayout Layout => m_Execution.Layout;
        public GameplayAbilityExecutionIdentity Identity => m_Execution.Services.Identity;
        public GeneratedBlackboardStateBinding<AbilityStateValue> Blackboards => m_Execution.Services.Blackboards;
        internal ProgramCatalogEntry[] BlackboardDeclarations => m_Execution.Services.BlackboardDeclarations;
        internal void BindCharacterInputs(System.Collections.Generic.HashSet<int> projected) =>
            m_Execution.Services.BindCharacterInputs(CharacterBlackboardSlots, projected);
        internal int[] CharacterBlackboardSlots { get; set; }
        public AbilityStateValue DefaultStateValue(int slot) => m_Execution.Services.DefaultValue(slot);
        public int EquipmentSlotCapacity => m_EquipmentLayout?.Slots.Count ?? 0;
        internal EquipmentProgramLayout EquipmentLayout => m_EquipmentLayout;
        internal FixedGameplayAbilityExecutionServices Services => m_Execution.Services;
        internal FixedAbilityExecutionContext Execution => m_Execution;
        internal int MaximumOperationEvaluations => OperationControlRuntime.EvaluationCapacity(Data.Operations.Count);

        static string[] BuildOperationSourcePaths(GameplayAbilityExecutionLayout layout)
        {
            var result = new string[layout.Operations.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = layout.SourcePath(new OperationHandle(i));
            return result;
        }
    }
}
