using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Diagnostics
{
    public sealed class RuntimeDiagnosticsTarget
    {
        public RuntimeDiagnosticsTarget(string displayName, int hostInstanceId, RuntimeDiagnosticsContext context)
        {
            DisplayName = displayName ?? string.Empty;
            HostInstanceId = hostInstanceId;
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public IGameplayAbilityAvailabilityQuery AbilityAvailability { get; set; }
        public string DisplayName { get; }
        public int HostInstanceId { get; }
        public RuntimeDiagnosticsContext Context { get; }
        public Guid CharacterRuntimeId => Context.CharacterRuntimeId;
        public Guid SessionId => Context.SessionId;
        public RuntimeContentRevision Revision => Context.Revision;
        public ulong RuntimeEpoch => Context.RuntimeEpoch;
        public Guid ExecutionBranchId => Context.ExecutionBranchId;
        public IDebugSourceMap SourceMap => Context.SourceMap;
        public RuntimeDiagnosticsStore Store => Context.Store;

        public void Terminate()
        {
            Context.Store.Terminate();
        }

        public void Dispose()
        {
            AbilityAvailability = null;
            Context.Store.Dispose();
        }
    }

    public static class RuntimeDiagnosticsTargetRegistry
    {
        static readonly List<RuntimeDiagnosticsTarget> s_Targets = new List<RuntimeDiagnosticsTarget>();
        public static event Action<RuntimeDiagnosticsTarget> TargetRegistered;
        public static event Action<RuntimeDiagnosticsTarget> TargetUnregistered;
        public static IReadOnlyList<RuntimeDiagnosticsTarget> Targets => s_Targets;

        public static void Register(RuntimeDiagnosticsTarget target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            for (int i = 0; i < s_Targets.Count; i++)
            {
                if (s_Targets[i].CharacterRuntimeId == target.CharacterRuntimeId)
                    throw new InvalidOperationException($"Character diagnostics target is already registered: {target.CharacterRuntimeId:N}.");
            }
            s_Targets.Add(target);
            TargetRegistered?.Invoke(target);
        }

        public static void Unregister(RuntimeDiagnosticsTarget target)
        {
            if (target == null || !s_Targets.Remove(target))
                return;
            TargetUnregistered?.Invoke(target);
        }

        public static bool TryGet(Guid characterRuntimeId, out RuntimeDiagnosticsTarget target)
        {
            for (int i = 0; i < s_Targets.Count; i++)
            {
                if (s_Targets[i].CharacterRuntimeId == characterRuntimeId)
                {
                    target = s_Targets[i];
                    return true;
                }
            }
            target = null;
            return false;
        }

        public static bool TryGetByHost(int hostInstanceId, out RuntimeDiagnosticsTarget target)
        {
            if (hostInstanceId == 0)
            {
                target = null;
                return false;
            }
            for (int i = 0; i < s_Targets.Count; i++)
            {
                if (s_Targets[i].HostInstanceId == hostInstanceId)
                {
                    target = s_Targets[i];
                    return true;
                }
            }
            target = null;
            return false;
        }
    }
}
