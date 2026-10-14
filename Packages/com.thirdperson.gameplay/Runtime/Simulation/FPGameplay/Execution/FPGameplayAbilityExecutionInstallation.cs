using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using ThirdPersonGameplay.Configuration;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPAbilityExecutionContext
    {
        public FPAbilityExecutionContext(
            FPGameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout,
            FPGameplayAbilityExecutionServices services)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public FPGameplayAbilityExecutionData Data { get; }
        public GameplayAbilityExecutionLayout Layout { get; }
        public FPGameplayAbilityExecutionServices Services { get; }
    }

    public sealed class FPGameplayAbilityExecutionInstallation
    {
        readonly FPAbilityExecutionContext m_Execution;
        readonly EquipmentProgramLayout m_EquipmentLayout;

        internal FPGameplayAbilityExecutionInstallation(
            FPGameplayAbilityExecutionData data,
            GameplayAbilityGrantInputBinding inputBinding,
            FPGameplayEffectRuntimeCatalog gameplayEffectCatalog,
            EquipmentProgramLayout equipmentLayout,
            GeneratedBlackboardConfiguration<AbilityStateValue> blackboards,
            FPCombatAttackCatalog combatCatalog = null,
            GameplayConfigurationRuntimeBinding configurations = null)
        {
            data = data ?? throw new ArgumentNullException(nameof(data));
            data = data.WithInputBinding(in inputBinding);
            bool requiresEquipment = data.Capabilities.HasGameplayCapability("Equipment");
            if (requiresEquipment && equipmentLayout == null)
                throw new ArgumentNullException(nameof(equipmentLayout));
            GameplayAbilityExecutionLayout layout = data.ExecutionLayout;
            EquipmentProgramLayout executionEquipmentLayout = requiresEquipment
                ? equipmentLayout
                : null;
            FPGameplayAbilityExecutionServices services = new FPGameplayAbilityExecutionServices(
                data,
                layout,
                BuildOperationSourcePaths(layout),
                gameplayEffectCatalog,
                blackboards, combatCatalog, configurations);
            m_EquipmentLayout = executionEquipmentLayout;
            m_Execution = new FPAbilityExecutionContext(
                data,
                layout,
                services);
        }

        public void AppendBlackboardSnapshot(FPCharacterRuntimeState state,
            System.Collections.Generic.List<FPBlackboardValueSnapshot> destination) =>
            FPBlackboardSnapshotReader.Append(this, state, destination);

        public FPGameplayAbilityExecutionData Data => m_Execution.Data;
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
        internal FPGameplayAbilityExecutionServices Services => m_Execution.Services;
        internal FPAbilityExecutionContext Execution => m_Execution;
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
