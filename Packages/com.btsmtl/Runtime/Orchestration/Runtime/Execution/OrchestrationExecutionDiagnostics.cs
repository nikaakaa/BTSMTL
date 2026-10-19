using System.Runtime.CompilerServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationDiagnosticLifecycle : byte
    {
        Initialize,
        BeginInvocation,
        EnterEntry,
        LeaveEntry,
        RetireOperation,
        RetireFrame
    }

    public interface IOrchestrationExecutionDiagnostics
    {
        int ScheduleEntry(in OrchestrationValueScheduleEntry entry);

        void Lifecycle<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics;

        int FindOverride<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics;

        void Observe<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            int operation, int slot, bool input, in TValue value)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics;

        void Trace<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics;

        int CaptureState<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            int operation, bool rootCompletedOverride)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics;
    }

    public readonly struct OrchestrationProductDiagnostics : IOrchestrationExecutionDiagnostics
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ScheduleEntry(in OrchestrationValueScheduleEntry entry) => entry.Product;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Lifecycle<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FindOverride<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics => -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Observe<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            int operation, int slot, bool input, in TValue value)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Trace<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CaptureState<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            int operation, bool rootCompletedOverride)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics => -1;
    }

    public readonly struct OrchestrationWorkbenchDiagnostics : IOrchestrationExecutionDiagnostics
    {
        public int ScheduleEntry(in OrchestrationValueScheduleEntry entry) => entry.Workbench;

        public void Lifecycle<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics =>
            cursor.DiagnosticLifecycle(lifecycle, operation, frame);

        public int FindOverride<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics =>
            cursor.FindDiagnosticOverride(operation, slot, kind, in owner);

        public void Observe<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            int operation, int slot, bool input, in TValue value)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics =>
            cursor.ObserveDiagnosticPort(operation, slot, input, in value);

        public void Trace<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics
        {
            if (kind == OrchestrationTraceKind.Edge)
                cursor.TraceDiagnosticEdge(edge, selected, passed, operation);
            else
                cursor.TraceDiagnosticOperation(kind, operation, result, stopCause, waitReason, valueSlot, in blackboardSource);
        }

        public int CaptureState<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage> cursor,
            int operation, bool rootCompletedOverride)
            where TValue : unmanaged where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where TContext : struct where TDiagnostics : struct, IOrchestrationExecutionDiagnostics =>
            cursor.CaptureDiagnosticState(operation, rootCompletedOverride);
    }
}
