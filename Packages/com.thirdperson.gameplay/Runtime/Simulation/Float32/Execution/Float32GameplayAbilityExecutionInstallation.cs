using System;
using ThirdPersonGameplay.Configuration;

namespace ThirdPersonSimulation
{
    internal sealed class Float32AbilityExecutionContext
    {
        public Float32AbilityExecutionContext(
            Float32GameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout,
            Float32GameplayAbilityExecutionServices services)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public Float32GameplayAbilityExecutionData Data { get; }
        public GameplayAbilityExecutionLayout Layout { get; }
        public Float32GameplayAbilityExecutionServices Services { get; }
    }

    public sealed class Float32GameplayAbilityExecutionInstallation
    {
        readonly Float32AbilityExecutionContext m_Execution;
        readonly EquipmentProgramLayout m_EquipmentLayout;

        internal Float32GameplayAbilityExecutionInstallation(
            Float32GameplayAbilityExecutionData data,
            GameplayAbilityGrantInputBinding inputBinding,
            Float32GameplayEffectRuntimeCatalog gameplayEffectCatalog,
            EquipmentProgramLayout equipmentLayout,
            GeneratedBlackboardConfiguration<AbilityStateValue> blackboards,
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
            Float32GameplayAbilityExecutionServices services = new Float32GameplayAbilityExecutionServices(
                data,
                layout,
                BuildOperationSourcePaths(layout),
                gameplayEffectCatalog,
                blackboards, configurations);
            m_EquipmentLayout = executionEquipmentLayout;
            m_Execution = new Float32AbilityExecutionContext(
                data,
                layout,
                services);
        }

        public Float32GameplayAbilityExecutionData Data => m_Execution.Data;
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
        internal Float32GameplayAbilityExecutionServices Services => m_Execution.Services;
        internal Float32AbilityExecutionContext Execution => m_Execution;
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
