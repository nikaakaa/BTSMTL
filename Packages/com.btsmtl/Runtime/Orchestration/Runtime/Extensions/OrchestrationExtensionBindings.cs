using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationExtensionBindings<TValue, TContext> : IOrchestrationExtensionBindings<TValue, TContext>
        where TValue : struct
        where TContext : struct
    {
        private readonly Binding[] m_Bindings;

        private OrchestrationExtensionBindings(Binding[] bindings)
        {
            m_Bindings = bindings;
        }

        public int Count => m_Bindings.Length;

        public ref readonly OrchestrationExtensionContract Contract(int bindingIndex) =>
            ref m_Bindings[bindingIndex].Contract;

        public bool ShouldExecute(int bindingIndex, in TContext context)
        {
            ref readonly Binding binding = ref m_Bindings[bindingIndex];
            return binding.ExecutionGate == null || binding.ExecutionGate.ShouldExecute(binding.ContentIndex, in context);
        }

        public OrchestrationExtensionResult InvokeReadOnly(int bindingIndex, in TContext context,
            in OrchestrationExtensionArguments<TValue> invocation)
        {
            ref readonly Binding binding = ref m_Bindings[bindingIndex];
            return binding.ReadOnly.Dispatch(binding.ContentIndex, in context, in invocation);
        }

        public OrchestrationExtensionResult InvokeCandidate(int bindingIndex, ref TContext context,
            in OrchestrationExtensionArguments<TValue> invocation)
        {
            ref readonly Binding binding = ref m_Bindings[bindingIndex];
            OrchestrationExtensionResult result = binding.Candidate.Dispatch(binding.ContentIndex, ref context, in invocation);
            if (binding.Contract.Completion == OrchestrationExtensionCompletion.Immediate &&
                result == OrchestrationExtensionResult.Running)
                throw new InvalidOperationException($"立即完成的扩展绑定 '{bindingIndex}' 在操作 '{invocation.OperationIndex}' 返回了 Running。");
            return result;
        }

        public static OrchestrationExtensionBindings<TValue, TContext> Prepare(
            ReadOnlySpan<OrchestrationExtensionReference> references,
            ReadOnlySpan<int> requiredBindingIndices,
            IReadOnlyList<IOrchestrationExtension> implementations,
            Func<int, IOrchestrationExtension, int> prepareContent = null)
        {
            var bindings = new Binding[references.Length];
            foreach (int bindingIndex in requiredBindingIndices)
            {
                OrchestrationExtensionReference reference = references[bindingIndex];
                IOrchestrationExtension implementation = Resolve(in reference, implementations);
                if (prepareContent != null)
                    reference = new OrchestrationExtensionReference(reference.Identity, reference.SemanticVersion,
                        prepareContent(bindingIndex, implementation), reference.Kind, reference.Access,
                        reference.Ports, reference.Fields, reference.RequiredInvocations,
                        reference.Completion, reference.SourcePath);
                OrchestrationExtensionContract contract = implementation.Describe(reference.ContentIndex);
                Validate(in reference, in contract);
                if (contract.Access == OrchestrationExtensionAccess.ReadOnly)
                {
                    if (!(implementation is ReadOnlyOrchestrationExtensionBinding<TValue, TContext> readOnly))
                        throw Failure(in reference, $"实现未提供宿主句柄 {typeof(TContext)} 的只读调用接口");
                    bindings[bindingIndex] = new Binding(in contract, reference.ContentIndex, readOnly);
                }
                else
                {
                    if (!(implementation is CandidateOrchestrationExtensionBinding<TValue, TContext> candidate))
                        throw Failure(in reference, $"实现未提供宿主句柄 {typeof(TContext)} 的候选写入调用接口");
                    var executionGate = candidate as ICandidateOrchestrationExtensionExecutionGate<TContext>;
                    if (executionGate != null && (contract.Kind != OrchestrationExtensionKind.Action ||
                        (contract.Invocations & OrchestrationExtensionInvocationKind.Execute) == 0))
                        throw Failure(in reference, "执行前判断只适用于支持 Execute 的 Action");
                    bindings[bindingIndex] = new Binding(in contract, reference.ContentIndex, candidate, executionGate);
                }
            }
            return new OrchestrationExtensionBindings<TValue, TContext>(bindings);
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

            if (reference.Completion != OrchestrationExtensionCompletion.Immediate &&
                reference.Completion != OrchestrationExtensionCompletion.Resumable)
                throw Failure(in reference, $"引用的完成保证无效：{reference.Completion}");
            if (contract.Completion != reference.Completion)
                throw Failure(in reference, $"完成保证不匹配：引用为 {reference.Completion}，实现为 {contract.Completion}");
            if (reference.Access == OrchestrationExtensionAccess.ReadOnly &&
                reference.Completion != OrchestrationExtensionCompletion.Immediate)
                throw Failure(in reference, "只读求值必须在本次调用内完成");

            ValidateInvocations(in reference, reference.RequiredInvocations, "引用");
            ValidateInvocations(in reference, contract.Invocations, "实现");
            if ((contract.Invocations & reference.RequiredInvocations) != reference.RequiredInvocations)
                throw Failure(in reference, $"实现不支持引用所需的调用阶段：{reference.RequiredInvocations}");

            ReadOnlySpan<OrchestrationExtensionPort> expected = reference.Ports.Span;
            ReadOnlySpan<OrchestrationExtensionPort> actual = contract.Ports.Span;
            if (actual.Length != expected.Length)
                throw Failure(in reference, $"端口数不匹配：引用为 {expected.Length}，实现为 {actual.Length}");
            int inputIndex = 0;
            int outputIndex = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                ref readonly OrchestrationExtensionPort expectedPort = ref expected[i];
                ref readonly OrchestrationExtensionPort actualPort = ref actual[i];
                if (expectedPort.Direction != OrchestrationExtensionPortDirection.Input &&
                    expectedPort.Direction != OrchestrationExtensionPortDirection.Output)
                    throw Failure(in reference, $"端口 {i} 的方向无效：{expectedPort.Direction}");
                int index = expectedPort.Direction == OrchestrationExtensionPortDirection.Input
                    ? inputIndex++ : outputIndex++;
                if (expectedPort.Index != index)
                    throw Failure(in reference, $"端口 {expectedPort.Id} 的句柄索引为 {expectedPort.Index}，合同位置为 {index}");
                if (!string.Equals(expectedPort.Id, actualPort.Id, StringComparison.Ordinal) ||
                    expectedPort.ValueKind != actualPort.ValueKind ||
                    expectedPort.Direction != actualPort.Direction || expectedPort.Index != actualPort.Index)
                    throw Failure(in reference,
                        $"端口 {i} 不匹配：引用为 {expectedPort.Id}/{expectedPort.ValueKind}/{expectedPort.Direction}/{expectedPort.Index}，" +
                        $"实现为 {actualPort.Id}/{actualPort.ValueKind}/{actualPort.Direction}/{actualPort.Index}");
            }
            ReadOnlySpan<OrchestrationExtensionContentField> expectedFields = reference.Fields.Span;
            ReadOnlySpan<OrchestrationExtensionContentField> actualFields = contract.Fields.Span;
            if (actualFields.Length != expectedFields.Length)
                throw Failure(in reference,
                    $"内容字段数不匹配：引用为 {expectedFields.Length}，实现为 {actualFields.Length}");
            for (int i = 0; i < expectedFields.Length; i++)
            {
                ref readonly OrchestrationExtensionContentField expectedField = ref expectedFields[i];
                ref readonly OrchestrationExtensionContentField actualField = ref actualFields[i];
                if (string.IsNullOrWhiteSpace(expectedField.Id))
                    throw Failure(in reference, $"内容字段 {i} 的身份缺失");
                for (int previous = 0; previous < i; previous++)
                    if (string.Equals(expectedFields[previous].Id, expectedField.Id, StringComparison.Ordinal))
                        throw Failure(in reference, $"内容字段身份重复：{expectedField.Id}");
                switch (expectedField.Kind)
                {
                    case OrchestrationExtensionFieldKind.Value:
                        if (!Enum.IsDefined(typeof(OrchestrationValueKind), expectedField.ValueKind) ||
                            expectedField.ValueKind == 0 ||
                            expectedField.ValueKind == OrchestrationValueKind.Enum ||
                            expectedField.ValueKind == OrchestrationValueKind.Identity)
                            throw Failure(in reference, $"内容字段 {expectedField.Id} 缺少普通值类型，枚举与身份必须使用各自字段种类");
                        break;
                    case OrchestrationExtensionFieldKind.Text:
                        break;
                    case OrchestrationExtensionFieldKind.Enum:
                    case OrchestrationExtensionFieldKind.Data:
                        if (string.IsNullOrWhiteSpace(expectedField.TypeIdentity))
                            throw Failure(in reference, $"内容字段 {expectedField.Id} 缺少类型身份");
                        break;
                    case OrchestrationExtensionFieldKind.Identity:
                        if (string.IsNullOrWhiteSpace(expectedField.IdentityDomainId))
                            throw Failure(in reference, $"内容字段 {expectedField.Id} 缺少身份域");
                        break;
                    default:
                        throw Failure(in reference, $"内容字段 {expectedField.Id} 的种类无效：{expectedField.Kind}");
                }
                if (expectedField.Cardinality != OrchestrationExtensionFieldCardinality.Single &&
                    expectedField.Cardinality != OrchestrationExtensionFieldCardinality.Multiple)
                    throw Failure(in reference, $"内容字段 {expectedField.Id} 的数量类型无效：{expectedField.Cardinality}");
                if (!expectedField.Equals(actualField))
                    throw Failure(in reference,
                        $"内容字段 {i} 不匹配：引用为 {expectedField.Id}/{expectedField.Kind}/" +
                        $"{expectedField.ValueKind}/{expectedField.TypeIdentity}/{expectedField.IdentityDomainId}/" +
                        $"{expectedField.Cardinality}/{expectedField.Required}，实现为 {actualField.Id}/" +
                        $"{actualField.Kind}/{actualField.ValueKind}/{actualField.TypeIdentity}/" +
                        $"{actualField.IdentityDomainId}/{actualField.Cardinality}/{actualField.Required}");
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
            $"领域扩展 {reference.Identity.DomainId}/{reference.Identity.OperationId} " +
            $"语义版本 {reference.SemanticVersion}，内容索引 {reference.ContentIndex}：{reason}。" +
            $"引用位置：{reference.SourcePath}");

        private readonly struct Binding
        {
            public Binding(in OrchestrationExtensionContract contract, int contentIndex,
                ReadOnlyOrchestrationExtensionBinding<TValue, TContext> readOnly)
            {
                Contract = contract;
                ContentIndex = contentIndex;
                ReadOnly = readOnly;
                Candidate = null;
                ExecutionGate = null;
            }

            public Binding(in OrchestrationExtensionContract contract, int contentIndex,
                CandidateOrchestrationExtensionBinding<TValue, TContext> candidate,
                ICandidateOrchestrationExtensionExecutionGate<TContext> executionGate)
            {
                Contract = contract;
                ContentIndex = contentIndex;
                ReadOnly = null;
                Candidate = candidate;
                ExecutionGate = executionGate;
            }

            public readonly OrchestrationExtensionContract Contract;
            public readonly int ContentIndex;
            public readonly ReadOnlyOrchestrationExtensionBinding<TValue, TContext> ReadOnly;
            public readonly CandidateOrchestrationExtensionBinding<TValue, TContext> Candidate;
            public readonly ICandidateOrchestrationExtensionExecutionGate<TContext> ExecutionGate;
        }
    }

    public static class OrchestrationExtensionRequirements
    {
        // 程序实际调用的扩展绑定：扩展操作、ActionInstance 黑板事实与状态转移钩子。
        public static int[] Collect<TValue>(OrchestrationProgram<TValue> program, int extensionCount)
            where TValue : unmanaged
        {
            var required = new bool[extensionCount];
            for (int operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
            {
                ref readonly OrchestrationOperation operation = ref program.Operation(operationIndex);
                if (operation.Code == OrchestrationOperationCode.Extension)
                    required[operation.ExtensionBindingIndex] = true;
                if (operation.BlackboardFactsBindingIndex >= 0)
                    required[operation.BlackboardFactsBindingIndex] = true;
                foreach (ref readonly OrchestrationHookBinding hook in program.Hooks(operationIndex))
                    required[hook.ExtensionBindingIndex] = true;
            }
            var indices = new List<int>();
            for (int index = 0; index < required.Length; index++)
                if (required[index])
                    indices.Add(index);
            return indices.ToArray();
        }
    }
}
