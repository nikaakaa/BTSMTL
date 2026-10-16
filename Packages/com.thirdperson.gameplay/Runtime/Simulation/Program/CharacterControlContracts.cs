using System;

namespace ThirdPersonSimulation
{
    public static class CharacterStateProviderFields
    {
        public const string OwnerPrefix = "control-definition:";
        public const string Position = "movement.position";
        public const string Velocity = "movement.velocity";
        public const string VerticalVelocity = "movement.vertical-velocity";
        public const string BodyYaw = "movement.body-yaw";
        public const string Grounded = "movement.grounded";
        public const string MoveFacingAngle = "move-facing-angle";
        public const string MemberPrefix = "character-state";
        public const int MemberRevision = 1;

        public static string Owner(string controlDefinitionIdentity) =>
            string.IsNullOrWhiteSpace(controlDefinitionIdentity)
                ? string.Empty
                : OwnerPrefix + controlDefinitionIdentity.Trim();

        public static bool IsVector3(string field) =>
            string.Equals(field, Position, StringComparison.Ordinal) ||
            string.Equals(field, Velocity, StringComparison.Ordinal);

        public static bool IsScalar(string field) =>
            string.Equals(field, VerticalVelocity, StringComparison.Ordinal);

        public static bool IsYaw(string field) =>
            string.Equals(field, BodyYaw, StringComparison.Ordinal);

        public static bool IsBoolean(string field) =>
            string.Equals(field, Grounded, StringComparison.Ordinal);

        public static bool IsValid(string field) =>
            IsVector3(field) || IsScalar(field) || IsYaw(field) || IsBoolean(field);

        public static bool IsOwner(string owner) =>
            !string.IsNullOrWhiteSpace(owner) &&
            owner.StartsWith(OwnerPrefix, StringComparison.Ordinal) &&
            owner.Length > OwnerPrefix.Length;

        public static bool IsOwnerForDefinition(string owner, string controlDefinitionIdentity) =>
            IsOwner(owner) &&
            !string.IsNullOrWhiteSpace(controlDefinitionIdentity) &&
            string.Equals(owner, Owner(controlDefinitionIdentity), StringComparison.Ordinal);

        public static SemanticValueKind ValueKind(string field)
        {
            if (IsVector3(field))
                return SemanticValueKind.Vector3;
            if (IsScalar(field))
                return SemanticValueKind.Number;
            if (IsYaw(field))
                return SemanticValueKind.Yaw;
            if (IsBoolean(field))
                return SemanticValueKind.Boolean;
            throw new InvalidOperationException($"Character State field '{field}' is not supported.");
        }

        public static GameplayAbilityProviderBindingEntry BuildProvider(string controlDefinitionIdentity)
        {
            string owner = Owner(controlDefinitionIdentity);
            var fields = new[] { Position, Velocity, VerticalVelocity, BodyYaw, Grounded, MoveFacingAngle };
            var members = new GameplayAbilityProviderMemberBinding[fields.Length];
            for (int i = 0; i < fields.Length; i++)
                members[i] = new GameplayAbilityProviderMemberBinding(GameplayAbilityProviderKind.CharacterState,
                    owner, $"{MemberPrefix}:{fields[i]}", ProviderValueKind(fields[i]), MemberRevision,
                    $"control-state:{controlDefinitionIdentity}:{fields[i]}");
            return new GameplayAbilityProviderBindingEntry(GameplayAbilityProviderKind.CharacterState,
                owner, GameplayAbilityProviderContractVersions.CharacterState, members);
        }

        public static GameplayAbilityProviderValueKind ProviderValueKind(string field) =>
            string.Equals(field, MoveFacingAngle, StringComparison.Ordinal)
                ? GameplayAbilityProviderValueKind.Number
                : ValueKind(field) switch
                {
                    SemanticValueKind.Boolean => GameplayAbilityProviderValueKind.Boolean,
                    SemanticValueKind.Number => GameplayAbilityProviderValueKind.Number,
                    SemanticValueKind.Vector3 => GameplayAbilityProviderValueKind.Vector3,
                    SemanticValueKind.Yaw => GameplayAbilityProviderValueKind.Yaw,
                    _ => throw new InvalidOperationException($"Character State field '{field}' is not supported.")
                };
    }

    public static class CharacterSkillProviderOwners
    {
        public const string AssetPrefix = "asset:";

        public static string Asset(string assetGuid) =>
            string.IsNullOrWhiteSpace(assetGuid)
                ? string.Empty
                : AssetPrefix + assetGuid.Trim();

        public static bool IsAssetOwner(string owner) =>
            !string.IsNullOrWhiteSpace(owner) &&
            owner.StartsWith(AssetPrefix, StringComparison.Ordinal) &&
            owner.Length > AssetPrefix.Length;

        public static bool IsAssetOwner(string owner, string expectedOwner) =>
            IsAssetOwner(owner) &&
            IsAssetOwner(expectedOwner) &&
            string.Equals(owner, expectedOwner, StringComparison.Ordinal);
    }

}
