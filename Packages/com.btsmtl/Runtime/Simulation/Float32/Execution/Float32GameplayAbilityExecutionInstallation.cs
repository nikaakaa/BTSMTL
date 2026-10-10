using System;

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
            Trace = new Float32TraceSink(data, layout);
        }

        public Float32GameplayAbilityExecutionData Data { get; }
        public GameplayAbilityExecutionLayout Layout { get; }
        public Float32GameplayAbilityExecutionServices Services { get; }
        internal Float32TraceSink Trace { get; }
    }

    public sealed class Float32GameplayAbilityExecutionInstallation
    {
        readonly Float32AbilityExecutionContext m_Execution;
        readonly EquipmentProgramLayout m_EquipmentLayout;
        readonly Float32AbilityOperationControlRuntime m_Control;

        internal Float32GameplayAbilityExecutionInstallation(
            Float32GameplayAbilityExecutionData data,
            GameplayAbilityGrantInputBinding inputBinding,
            Float32GameplayEffectRuntimeCatalog gameplayEffectCatalog,
            EquipmentProgramLayout equipmentLayout)
        {
            data = data ?? throw new ArgumentNullException(nameof(data));
            data = data.WithInputBinding(in inputBinding);
            bool requiresEquipment = data.Capabilities.HasGameplayCapability("Equipment");
            bool requiresTagCatalog = Float32GameplayAbilityExecutionServices.RequiresTagCatalog(data);
            if (requiresTagCatalog && gameplayEffectCatalog == null)
                throw new ArgumentNullException(nameof(gameplayEffectCatalog));
            if (requiresEquipment && equipmentLayout == null)
                throw new ArgumentNullException(nameof(equipmentLayout));
            GameplayAbilityExecutionLayout layout = data.ExecutionLayout;
            Float32GameplayEffectRuntimeCatalog executionGameplayEffectCatalog = requiresTagCatalog
                ? gameplayEffectCatalog
                : null;
            EquipmentProgramLayout executionEquipmentLayout = requiresEquipment
                ? equipmentLayout
                : null;
            Float32GameplayAbilityExecutionServices services = new Float32GameplayAbilityExecutionServices(
                data,
                layout,
                BuildOperationSourcePaths(layout),
                executionGameplayEffectCatalog);
            m_EquipmentLayout = executionEquipmentLayout;
            m_Execution = new Float32AbilityExecutionContext(
                data,
                layout,
                services);
            m_Control = new Float32AbilityOperationControlRuntime(data);
        }

        public Float32GameplayAbilityExecutionData Data => m_Execution.Data;
        public GameplayAbilityExecutionLayout Layout => m_Execution.Layout;
        public GameplayAbilityExecutionIdentity Identity => m_Execution.Services.Identity;
        internal EquipmentProgramLayout EquipmentLayout => m_EquipmentLayout;
        internal Float32GameplayAbilityExecutionServices Services => m_Execution.Services;
        internal Float32AbilityExecutionContext Execution => m_Execution;
        internal Float32AbilityOperationControlRuntime Control => m_Control;

        static string[] BuildOperationSourcePaths(GameplayAbilityExecutionLayout layout)
        {
            var result = new string[layout.Operations.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = layout.SourcePath(new OperationHandle(i));
            return result;
        }
    }
}
