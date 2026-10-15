using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationExtensionBindings<TValue> : IOrchestrationExtensionBindings<TValue>
        where TValue : struct
    {
        private readonly Binding[] m_Bindings;

        private OrchestrationExtensionBindings(Binding[] bindings)
        {
            m_Bindings = bindings;
        }

        public int Count => m_Bindings.Length;

        public ref readonly OrchestrationExtensionContract Contract(int bindingIndex) =>
            ref m_Bindings[bindingIndex].Contract;

        public OrchestrationExtensionResult InvokeReadOnly(int bindingIndex, in OrchestrationReadContextView context,
            in OrchestrationExtensionInvocation<TValue> invocation)
        {
            ref readonly Binding binding = ref m_Bindings[bindingIndex];
            return binding.ReadOnly.Invoke(binding.ContentIndex, in context, in invocation);
        }

        public OrchestrationExtensionResult InvokeCandidate(int bindingIndex, in OrchestrationCandidateContextView context,
            in OrchestrationExtensionInvocation<TValue> invocation)
        {
            ref readonly Binding binding = ref m_Bindings[bindingIndex];
            return binding.Candidate.Invoke(binding.ContentIndex, in context, in invocation);
        }

        public static OrchestrationExtensionBindings<TValue> Prepare(
            ReadOnlySpan<OrchestrationExtensionReference> references,
            IReadOnlyList<IOrchestrationExtension> implementations,
            ReadOnlySpan<object> readSlots, ReadOnlySpan<object> candidateSlots)
        {
            var bindings = new Binding[references.Length];
            for (int i = 0; i < references.Length; i++)
            {
                ref readonly OrchestrationExtensionReference reference = ref references[i];
                IOrchestrationExtension implementation = Resolve(in reference, implementations);
                OrchestrationExtensionContract contract = implementation.Describe(reference.ContentIndex);
                Validate(in reference, in contract);
                if (contract.Access == OrchestrationExtensionAccess.ReadOnly)
                {
                    if (!(implementation is IReadOnlyOrchestrationExtensionRoute<TValue> readOnly))
                        throw Failure(in reference, "实现未提供已声明的只读上下文路由接口");
                    ValidateContext(in reference, readOnly.ContextSlotIndex, readOnly.ContextType, readSlots);
                    bindings[i] = new Binding(in contract, reference.ContentIndex, readOnly);
                }
                else
                {
                    if (!(implementation is ICandidateOrchestrationExtensionRoute<TValue> candidate))
                        throw Failure(in reference, "实现未提供已声明的候选写入上下文路由接口");
                    ValidateContext(in reference, candidate.ContextSlotIndex, candidate.ContextType, candidateSlots);
                    bindings[i] = new Binding(in contract, reference.ContentIndex, candidate);
                }
            }
            return new OrchestrationExtensionBindings<TValue>(bindings);
        }

        private static void ValidateContext(in OrchestrationExtensionReference reference,
            int slotIndex, Type contextType, ReadOnlySpan<object> slots)
        {
            if ((uint)slotIndex >= (uint)slots.Length)
                throw Failure(in reference, $"上下文槽 {slotIndex} 未由宿主提供");
            if (!(slots[slotIndex] is Array array) || array.GetType() != contextType.MakeArrayType())
                throw Failure(in reference, $"上下文槽 {slotIndex} 必须提供元素类型为 {contextType} 的一维数组");
        }

        private static IOrchestrationExtension Resolve(in OrchestrationExtensionReference reference,
            IReadOnlyList<IOrchestrationExtension> implementations)
        {
            int implementationIndex = -1;
            bool identityFound = false;
            for (int i = 0; i < implementations.Count; i++)
            {
                IOrchestrationExtension implementation = implementations[i];
                if (!reference.Identity.Equals(implementation.Identity))
                    continue;
                identityFound = true;
                if (reference.SemanticVersion != implementation.SemanticVersion)
                    continue;
                if (implementationIndex >= 0)
                    throw Failure(in reference, "身份与语义版本存在多个实现");
                implementationIndex = i;
            }
            if (implementationIndex >= 0)
                return implementations[implementationIndex];
            throw Failure(in reference, identityFound ? "没有精确匹配的语义版本" : "缺少扩展实现");
        }

        private static void Validate(in OrchestrationExtensionReference reference,
            in OrchestrationExtensionContract contract)
        {
            if (reference.Kind != OrchestrationExtensionKind.Action &&
                reference.Kind != OrchestrationExtensionKind.Condition &&
                reference.Kind != OrchestrationExtensionKind.Value)
                throw Failure(in reference, $"引用的扩展种类无效：{reference.Kind}");
            if (reference.Access != OrchestrationExtensionAccess.ReadOnly &&
                reference.Access != OrchestrationExtensionAccess.CandidateWrite)
                throw Failure(in reference, $"引用的访问类别无效：{reference.Access}");
            if (contract.Kind != reference.Kind)
                throw Failure(in reference, $"扩展种类不匹配：引用为 {reference.Kind}，实现为 {contract.Kind}");
            if (contract.Access != reference.Access)
                throw Failure(in reference, $"访问类别不匹配：引用为 {reference.Access}，实现为 {contract.Access}");

            ValidateInvocations(in reference, reference.RequiredInvocations, "引用");
            ValidateInvocations(in reference, contract.Invocations, "实现");
            if ((contract.Invocations & reference.RequiredInvocations) != reference.RequiredInvocations)
                throw Failure(in reference, $"实现不支持引用所需的调用阶段：{reference.RequiredInvocations}");

            ReadOnlySpan<OrchestrationExtensionPort> expected = reference.Ports.Span;
            ReadOnlySpan<OrchestrationExtensionPort> actual = contract.Ports.Span;
            if (actual.Length != expected.Length)
                throw Failure(in reference, $"端口数不匹配：引用为 {expected.Length}，实现为 {actual.Length}");
            for (int i = 0; i < expected.Length; i++)
            {
                ref readonly OrchestrationExtensionPort expectedPort = ref expected[i];
                ref readonly OrchestrationExtensionPort actualPort = ref actual[i];
                if (expectedPort.Direction != OrchestrationExtensionPortDirection.Input &&
                    expectedPort.Direction != OrchestrationExtensionPortDirection.Output)
                    throw Failure(in reference, $"端口 {i} 的方向无效：{expectedPort.Direction}");
                if (!string.Equals(expectedPort.Id, actualPort.Id, StringComparison.Ordinal) ||
                    !string.Equals(expectedPort.ValueTypeId, actualPort.ValueTypeId, StringComparison.Ordinal) ||
                    expectedPort.Direction != actualPort.Direction)
                    throw Failure(in reference,
                        $"端口 {i} 不匹配：引用为 {expectedPort.Id}/{expectedPort.ValueTypeId}/{expectedPort.Direction}，" +
                        $"实现为 {actualPort.Id}/{actualPort.ValueTypeId}/{actualPort.Direction}");
            }
        }

        private static void ValidateInvocations(in OrchestrationExtensionReference reference,
            OrchestrationExtensionInvocationKind invocations, string owner)
        {
            OrchestrationExtensionInvocationKind allowed = reference.Kind == OrchestrationExtensionKind.Action
                ? OrchestrationExtensionInvocationKind.Execute |
                  OrchestrationExtensionInvocationKind.RequestStop |
                  OrchestrationExtensionInvocationKind.ContinueStop |
                  OrchestrationExtensionInvocationKind.ForceStop
                : OrchestrationExtensionInvocationKind.Evaluate;
            if (invocations == 0 || (invocations & ~allowed) != 0)
                throw Failure(in reference, $"{owner}的调用阶段不符合 {reference.Kind}：{invocations}");
        }

        private static InvalidOperationException Failure(in OrchestrationExtensionReference reference,
            string reason) => new InvalidOperationException(
            $"领域扩展 {reference.Identity.DomainId}/{reference.Identity.OperationId}/{reference.Identity.BindingRole} " +
            $"语义版本 {reference.SemanticVersion}，内容索引 {reference.ContentIndex}：{reason}。" +
            $"引用位置：{reference.SourcePath}");

        private readonly struct Binding
        {
            public Binding(in OrchestrationExtensionContract contract, int contentIndex,
                IReadOnlyOrchestrationExtensionRoute<TValue> readOnly)
            {
                Contract = contract;
                ContentIndex = contentIndex;
                ReadOnly = readOnly;
                Candidate = null;
            }

            public Binding(in OrchestrationExtensionContract contract, int contentIndex,
                ICandidateOrchestrationExtensionRoute<TValue> candidate)
            {
                Contract = contract;
                ContentIndex = contentIndex;
                ReadOnly = null;
                Candidate = candidate;
            }

            public readonly OrchestrationExtensionContract Contract;
            public readonly int ContentIndex;
            public readonly IReadOnlyOrchestrationExtensionRoute<TValue> ReadOnly;
            public readonly ICandidateOrchestrationExtensionRoute<TValue> Candidate;
        }
    }
}
