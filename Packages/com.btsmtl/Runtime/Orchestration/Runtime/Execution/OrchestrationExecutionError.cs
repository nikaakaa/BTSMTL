using System;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationExecutionErrorCode : byte
    {
        None,
        WorkspaceRowDepth,
        WorkspaceInputWidth,
        WorkspaceOutputWidth,
        WorkspaceStateDepth,
        WorkspaceGraphCallDepth,
        CompleteEntryWaiting,
        CompleteOutputWaiting,
        BlackboardReadOnly,
        BlackboardScope,
        HookFailed,
        HookInput,
        TimelineForceStop,
        ActionForceStop,
        ActionStopResult,
        OutputBudget,
        TimelineInvocation,
        OperationNotExecutable,
        ReadOnlyExtension,
        ValueScheduleInstruction
    }

    // 推进路径抛出固定文字前先写入该记录；托管宿主在入口外用 Formatter 组织可读信息。
    public struct OrchestrationExecutionError
    {
        public OrchestrationExecutionErrorCode Code;
        public int OperationIndex;
        public int Slot;
        public int Detail;
        public int Aux;
        public long Required;
        public long Capacity;
        public ulong Identity;
    }

    public static class OrchestrationExecutionErrorFormatter
    {
        public static string Format(in OrchestrationExecutionError error, string graphId = null, string sourceRevision = null)
        {
            string program = graphId == null ? string.Empty
                : $"程序 '{graphId}'（修订 '{sourceRevision}'）";
            int op = error.OperationIndex;
            switch (error.Code)
            {
                case OrchestrationExecutionErrorCode.WorkspaceRowDepth:
                    return $"{program}操作 {op} 的工作区 Inputs/Outputs 容量不足：所需深度 {error.Required}，发布容量 {error.Capacity}。编译期 WorkspaceCapacity 分析少算了同步调用需求。";
                case OrchestrationExecutionErrorCode.WorkspaceStateDepth:
                    return $"{program}操作 {op} 的工作区 States 容量不足：所需深度 {error.Required}，发布容量 {error.Capacity}。编译期 WorkspaceCapacity 分析少算了同步调用需求。";
                case OrchestrationExecutionErrorCode.WorkspaceGraphCallDepth:
                    return $"{program}操作 {op} 的工作区 GraphCallEntries 容量不足：所需深度 {error.Required}，发布容量 {error.Capacity}。编译期 WorkspaceCapacity 分析少算了同步调用需求。";
                case OrchestrationExecutionErrorCode.WorkspaceInputWidth:
                case OrchestrationExecutionErrorCode.WorkspaceOutputWidth:
                    string region = error.Code == OrchestrationExecutionErrorCode.WorkspaceInputWidth ? "Inputs" : "Outputs";
                    return $"{program}操作 {op} 的工作区 {region} 容量不足：所需深度 {error.Slot + 1}，发布深度 {error.Aux}；本层所需宽度 {error.Required}，发布容量 {error.Capacity}。编译期 WorkspaceCapacity 分析少算了值行需求。";
                case OrchestrationExecutionErrorCode.CompleteEntryWaiting:
                    return $"{program}同次完成的图入口不能返回跨次等待。";
                case OrchestrationExecutionErrorCode.CompleteOutputWaiting:
                    return $"{program}同次完成的图输出不能返回跨次等待。";
                case OrchestrationExecutionErrorCode.BlackboardReadOnly:
                    return $"{program}Blackboard slot '{error.Slot}' is read-only configuration.";
                case OrchestrationExecutionErrorCode.BlackboardScope:
                    return $"{program}Blackboard slot '{error.Slot}' declares unsupported scope '{(OrchestrationScopeKind)error.Detail}'.";
                case OrchestrationExecutionErrorCode.HookFailed:
                    return $"{program}Lifecycle hook '{(OrchestrationLifecycleHook)error.Detail}' at operation '{op}' returned '{(OrchestrationExtensionResult)error.Aux}'.";
                case OrchestrationExecutionErrorCode.HookInput:
                    return $"{program}Lifecycle hook at operation '{op}' has unsupported input '{(OrchestrationHookInputSource)error.Detail}'.";
                case OrchestrationExecutionErrorCode.TimelineForceStop:
                    return $"{program}Timeline operation '{op}' force stop returned '{(OrchestrationStopStatus)error.Detail}'.";
                case OrchestrationExecutionErrorCode.ActionForceStop:
                    return $"{program}Action operation '{op}' force stop returned '{(OrchestrationStopStatus)error.Detail}'.";
                case OrchestrationExecutionErrorCode.ActionStopResult:
                    return $"{program}Action operation '{op}' returned an invalid stop result.";
                case OrchestrationExecutionErrorCode.OutputBudget:
                    return $"{program}Operation '{op}' exceeds the published output budget of its program invocation.";
                case OrchestrationExecutionErrorCode.TimelineInvocation:
                    return $"{program}Timeline playback '{error.Identity}' clip '{error.Slot}' hook '{(OrchestrationTimelineHook)error.Detail}' failed.";
                case OrchestrationExecutionErrorCode.OperationNotExecutable:
                    return $"{program}Operation '{op}' ({(OrchestrationOperationCode)error.Detail}) is not executable.";
                case OrchestrationExecutionErrorCode.ReadOnlyExtension:
                    return $"{program}Read-only extension '{error.Slot}' at operation '{op}' returned '{(OrchestrationExtensionResult)error.Detail}'.";
                case OrchestrationExecutionErrorCode.ValueScheduleInstruction:
                    return $"{program}未知的值调度指令 '{(OrchestrationValueScheduleCode)error.Detail}'。";
                default:
                    return program + "编排推进失败。";
            }
        }

        // 托管入口捕获异常后调用：有错误记录时组织完整信息，否则沿用异常原文。
        public static string Describe(Exception exception, in OrchestrationExecutionError error,
            string graphId = null, string sourceRevision = null) =>
            error.Code == OrchestrationExecutionErrorCode.None
                ? exception.Message : Format(in error, graphId, sourceRevision);
    }

    // Burst 只接受字面量异常文字；完整信息由宿主读取错误记录后经 Formatter 组织。
    static class OrchestrationExecutionErrorText
    {
        internal static void Throw(OrchestrationExecutionErrorCode code)
        {
            switch (code)
            {
                case OrchestrationExecutionErrorCode.WorkspaceRowDepth:
                case OrchestrationExecutionErrorCode.WorkspaceInputWidth:
                case OrchestrationExecutionErrorCode.WorkspaceOutputWidth:
                case OrchestrationExecutionErrorCode.WorkspaceStateDepth:
                case OrchestrationExecutionErrorCode.WorkspaceGraphCallDepth:
                    throw new InvalidOperationException("编排工作区容量不足。");
                case OrchestrationExecutionErrorCode.CompleteEntryWaiting:
                    throw new InvalidOperationException("同次完成的图入口不能返回跨次等待。");
                case OrchestrationExecutionErrorCode.CompleteOutputWaiting:
                    throw new InvalidOperationException("同次完成的图输出不能返回跨次等待。");
                case OrchestrationExecutionErrorCode.BlackboardReadOnly:
                    throw new InvalidOperationException("黑板槽是只读配置。");
                case OrchestrationExecutionErrorCode.BlackboardScope:
                    throw new InvalidOperationException("黑板槽声明了不支持的作用域。");
                case OrchestrationExecutionErrorCode.HookFailed:
                    throw new InvalidOperationException("生命周期钩子返回失败。");
                case OrchestrationExecutionErrorCode.HookInput:
                    throw new InvalidOperationException("生命周期钩子输入来源不受支持。");
                case OrchestrationExecutionErrorCode.TimelineForceStop:
                    throw new InvalidOperationException("Timeline 强制停止未完成。");
                case OrchestrationExecutionErrorCode.ActionForceStop:
                    throw new InvalidOperationException("动作强制停止未完成。");
                case OrchestrationExecutionErrorCode.ActionStopResult:
                    throw new InvalidOperationException("动作返回了无效的停止结果。");
                case OrchestrationExecutionErrorCode.OutputBudget:
                    throw new InvalidOperationException("编排输出超出发布预算。");
                case OrchestrationExecutionErrorCode.TimelineInvocation:
                    throw new InvalidOperationException("Timeline 调用失败。");
                case OrchestrationExecutionErrorCode.OperationNotExecutable:
                    throw new InvalidOperationException("操作不可执行。");
                case OrchestrationExecutionErrorCode.ReadOnlyExtension:
                    throw new InvalidOperationException("只读扩展返回失败。");
                case OrchestrationExecutionErrorCode.ValueScheduleInstruction:
                    throw new InvalidOperationException("未知的值调度指令。");
                default:
                    throw new InvalidOperationException("编排推进失败。");
            }
        }
    }
}
