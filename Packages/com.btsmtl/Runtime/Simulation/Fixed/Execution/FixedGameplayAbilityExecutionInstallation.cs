using System;
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
            Trace = new FixedTraceSink(data, layout);
        }

        public FixedGameplayAbilityExecutionData Data { get; }
        public GameplayAbilityExecutionLayout Layout { get; }
        public FixedGameplayAbilityExecutionServices Services { get; }
        internal FixedTraceSink Trace { get; }
    }

    public sealed class FixedGameplayAbilityExecutionInstallation
    {
        readonly FixedAbilityExecutionContext m_Execution;
        readonly EquipmentProgramLayout m_EquipmentLayout;
        readonly FixedAbilityOperationControlRuntime m_Control;

        internal FixedGameplayAbilityExecutionInstallation(
            FixedGameplayAbilityExecutionData data,
            GameplayAbilityGrantInputBinding inputBinding,
            FixedGameplayEffectRuntimeCatalog gameplayEffectCatalog,
            EquipmentProgramLayout equipmentLayout)
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
                executionGameplayEffectCatalog);
            m_EquipmentLayout = executionEquipmentLayout;
            m_Execution = new FixedAbilityExecutionContext(
                data,
                layout,
                services);
            m_Control = new FixedAbilityOperationControlRuntime(data);
        }

        public void AppendBlackboardSnapshot(FixedCharacterRuntimeState state,
            System.Collections.Generic.List<FixedBlackboardValueSnapshot> destination) =>
            FixedBlackboardSnapshotReader.Append(this, state, destination);

        public FixedGameplayAbilityExecutionData Data => m_Execution.Data;
        public GameplayAbilityExecutionLayout Layout => m_Execution.Layout;
        public GameplayAbilityExecutionIdentity Identity => m_Execution.Services.Identity;
        public int EquipmentSlotCapacity => m_EquipmentLayout?.Slots.Count ?? 0;
        internal EquipmentProgramLayout EquipmentLayout => m_EquipmentLayout;
        internal FixedGameplayAbilityExecutionServices Services => m_Execution.Services;
        internal FixedAbilityExecutionContext Execution => m_Execution;
        internal FixedAbilityOperationControlRuntime Control => m_Control;

        static string[] BuildOperationSourcePaths(GameplayAbilityExecutionLayout layout)
        {
            var result = new string[layout.Operations.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = layout.SourcePath(new OperationHandle(i));
            return result;
        }
    }
}
