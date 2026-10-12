using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    public enum ProgramBlackboardInputProjection : byte
    {
        Direct,
        ActionTargetIdentity,
        ActionTargetPosition,
        ActionTargetYaw
    }

    public readonly struct BlackboardTargetProjection
    {
        public const string IdentityRole = "TargetIdentity";
        public const string PositionRole = "TargetPosition";
        public const string YawRole = "TargetYaw";
        readonly bool m_IsValid;

        BlackboardTargetProjection(int identity, int position, int yaw)
        {
            Identity = identity;
            Position = position;
            Yaw = yaw;
            m_IsValid = true;
        }

        public int Identity { get; }
        public int Position { get; }
        public int Yaw { get; }
        public bool IsValid => m_IsValid;

        internal static BlackboardTargetProjection[] Build(IReadOnlyList<SimulationOperationCode> operations,
            IReadOnlyList<ProgramReference> references, IReadOnlyList<ProgramStateSlot> slots)
        {
            int operationCount = operations.Count;
            var identities = new int[operationCount];
            var positions = new int[operationCount];
            var yaws = new int[operationCount];
            Array.Fill(identities, -1);
            Array.Fill(positions, -1);
            Array.Fill(yaws, -1);
            for (int index = 0; index < references.Count; index++)
            {
                ProgramReference reference = references[index];
                if (!reference.HasSourceOperation || reference.Kind != ProgramReferenceKind.StateSlot)
                    continue;
                int operation = reference.SourceOperation.Value;
                switch (reference.ExternalIdentity)
                {
                    case IdentityRole: identities[operation] = reference.TargetIndex; break;
                    case PositionRole: positions[operation] = reference.TargetIndex; break;
                    case YawRole: yaws[operation] = reference.TargetIndex; break;
                }
            }
            var result = new BlackboardTargetProjection[operationCount];
            for (int operation = 0; operation < operationCount; operation++)
            {
                if (identities[operation] < 0 && positions[operation] < 0 && yaws[operation] < 0 &&
                    operations[operation] != SimulationOperationCode.ActionTargetRead &&
                    operations[operation] != SimulationOperationCode.CaptureActionTarget)
                    continue;
                if (identities[operation] < 0 || positions[operation] < 0 || yaws[operation] < 0)
                    throw new InvalidDataException($"Action target projection for operation '{operation}' requires identity, position and yaw.");
                if (slots[identities[operation]].ValueKind != ProgramStateValueKind.Identity ||
                    slots[positions[operation]].ValueKind != ProgramStateValueKind.Vector3 ||
                    slots[yaws[operation]].ValueKind != ProgramStateValueKind.Scalar)
                    throw new InvalidDataException($"Action target projection for operation '{operation}' has incompatible component types.");
                result[operation] = new BlackboardTargetProjection(identities[operation], positions[operation], yaws[operation]);
            }
            return result;
        }
    }
}
