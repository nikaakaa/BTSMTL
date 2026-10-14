using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    public enum SimulationRootKind : byte
    {
        Ability = 3
    }

    public readonly partial struct GameplayAbilityRootDescriptor : IEquatable<GameplayAbilityRootDescriptor>
    {
        public GameplayAbilityRootDescriptor(
            SimulationRootKind kind,
            string rootIdentity,
            string entryIdentity,
            string contentIdentity)
        {
            if (kind != SimulationRootKind.Ability)
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            RootIdentity = SimulationIdentity.Require(rootIdentity, nameof(rootIdentity));
            EntryIdentity = SimulationIdentity.Require(entryIdentity, nameof(entryIdentity));
            ContentIdentity = SimulationIdentity.Require(contentIdentity, nameof(contentIdentity));
        }

        public SimulationRootKind Kind { get; }
        public string RootIdentity { get; }
        public string EntryIdentity { get; }
        public string ContentIdentity { get; }
        [ValueValidation]
        void RequireCanonicalIdentity()
        {
            if (!IsValid)
                throw new ArgumentException("Ability root descriptor is invalid.");
        }
        public bool IsValid =>
            Enum.IsDefined(typeof(SimulationRootKind), Kind) &&
            IsGuid(RootIdentity) &&
            IsEntryIdentity(Kind, EntryIdentity) &&
            IsHash(ContentIdentity);
        public bool IsAbility => Kind == SimulationRootKind.Ability;

        public bool Equals(GameplayAbilityRootDescriptor other) =>
            Kind == other.Kind &&
            string.Equals(RootIdentity, other.RootIdentity, StringComparison.Ordinal) &&
            string.Equals(EntryIdentity, other.EntryIdentity, StringComparison.Ordinal) &&
            string.Equals(ContentIdentity, other.ContentIdentity, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is GameplayAbilityRootDescriptor other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Kind, RootIdentity, EntryIdentity, ContentIdentity);

        public override string ToString() =>
            IsValid
                ? $"{Kind}:{RootIdentity}:{EntryIdentity}:{ContentIdentity}"
                : "Invalid";

        public static bool operator ==(
            GameplayAbilityRootDescriptor left,
            GameplayAbilityRootDescriptor right) => left.Equals(right);

        public static bool operator !=(
            GameplayAbilityRootDescriptor left,
            GameplayAbilityRootDescriptor right) => !left.Equals(right);

        static bool IsEntryIdentity(SimulationRootKind kind, string value)
        {
            return kind == SimulationRootKind.Ability &&
                   !string.IsNullOrEmpty(value) &&
                   value.StartsWith("ability:", StringComparison.Ordinal) &&
                   value.Length > "ability:".Length;
        }

        static bool IsGuid(string value) => IsHex(value, 32);

        static bool IsHash(string value) => IsHex(value, 64);

        static bool IsHex(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length != length)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                    return false;
            }
            return true;
        }
    }

    public static class GameplayAbilityRootValidation
    {
        public static ProgramReference RequireEntryReference(
            GameplayAbilityRootDescriptor root,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<SemanticOperation> operations)
        {
            if (!root.IsValid)
                throw new ArgumentException("Ability root descriptor is invalid.", nameof(root));
            if (references == null)
                throw new ArgumentNullException(nameof(references));
            if (operations == null)
                throw new ArgumentNullException(nameof(operations));
            ProgramReference match = null;
            for (int i = 0; i < references.Count; i++)
            {
                ProgramReference reference = references[i];
                if (reference != null &&
                    !reference.HasSourceOperation &&
                    reference.Kind == ProgramReferenceKind.Operation &&
                    string.Equals(reference.Identity, "ability:root-operation", StringComparison.Ordinal))
                {
                    if (match != null)
                        throw new InvalidDataException("Ability root operation reference is duplicated.");
                    match = reference;
                }
            }
            if (match == null)
                throw new InvalidDataException("Ability root operation reference is missing.");
            if (match.TargetIndex < 0 || match.TargetIndex >= operations.Count)
                throw new InvalidDataException("Ability root operation reference targets an invalid operation.");
            if (!string.Equals(match.ExternalIdentity, root.EntryIdentity, StringComparison.Ordinal))
                throw new InvalidDataException("Ability root operation reference does not match the root entry identity.");
            if (operations[match.TargetIndex].Code != SimulationOperationCode.Root)
                throw new InvalidDataException("Ability root operation reference does not target a Root operation.");
            return match;
        }
    }

}
