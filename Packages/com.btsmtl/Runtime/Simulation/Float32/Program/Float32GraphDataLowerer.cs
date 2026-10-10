using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public static class Float32GraphDataLowerer
    {
        public static void Lower(
            IReadOnlyList<SemanticOperation> semanticOperations,
            IReadOnlyList<SemanticLiteral> literals,
            IReadOnlyList<SemanticConstantInputBinding> constantInputBindings,
            List<Float32ScalarConversion> conversions,
            out List<SimulationOperationDefinition> definitions,
            out SimulationOperation[] operations,
            out ProgramConstant[] constants,
            out ProgramConstantInputBinding[] constantInputs)
        {
            constants = new ProgramConstant[literals.Count];
            for (int i = 0; i < literals.Count; i++)
                constants[i] = LowerLiteral(literals[i], conversions);

            definitions = new List<SimulationOperationDefinition>();
            var definitionByIdentity = new Dictionary<string, SimulationOperationDefinition>(StringComparer.Ordinal);
            operations = new SimulationOperation[semanticOperations.Count];
            for (int i = 0; i < semanticOperations.Count; i++)
            {
                SemanticOperation operation = semanticOperations[i];
                string sourceIdentity = operation.Number0SourceIdentity.Length == 0
                    ? $"operation:{operation.Handle.Value}/number0"
                    : operation.Number0SourceIdentity;
                Float32Scalar scalar0 = LowerNumber(operation.Number0, sourceIdentity, SemanticNumericPrecision.TargetRounded, conversions);
                if (!definitionByIdentity.TryGetValue(operation.TemplateIdentity, out SimulationOperationDefinition definition))
                {
                    definition = new SimulationOperationDefinition(
                        definitions.Count,
                        operation.TemplateIdentity,
                        operation.Code,
                        operation.LiteralReferences,
                        operation.Integer0,
                        operation.Integer1,
                        operation.Unsigned0,
                        scalar0,
                        operation.Text0,
                        operation.Flags);
                    definitions.Add(definition);
                    definitionByIdentity.Add(operation.TemplateIdentity, definition);
                }
                else
                {
                    RequireMatchingDefinition(definition, operation, scalar0);
                }
                operations[i] = new SimulationOperation(
                    operation.Handle,
                    definition,
                    operation.Operands,
                    operation.StateSlots);
            }

            constantInputs = new ProgramConstantInputBinding[constantInputBindings.Count];
            for (int i = 0; i < constantInputs.Length; i++)
            {
                SemanticConstantInputBinding inputBinding = constantInputBindings[i];
                constantInputs[i] = new ProgramConstantInputBinding(
                    inputBinding.TargetOperation,
                    inputBinding.TargetPort,
                    inputBinding.ConstantIndex,
                    inputBinding.ResolvedValueKind);
            }
        }

        static void RequireMatchingDefinition(
            SimulationOperationDefinition definition,
            SemanticOperation operation,
            Float32Scalar scalar0)
        {
            bool matches = definition.Code == operation.Code &&
                           definition.Integer0 == operation.Integer0 &&
                           definition.Integer1 == operation.Integer1 &&
                           definition.Unsigned0 == operation.Unsigned0 &&
                           definition.Scalar0 == scalar0 &&
                           string.Equals(definition.Text0, operation.Text0, StringComparison.Ordinal) &&
                           definition.Flags == operation.Flags &&
                           definition.ConstantReferences.Count == operation.LiteralReferences.Count;
            for (int i = 0; matches && i < definition.ConstantReferences.Count; i++)
                matches = definition.ConstantReferences[i] == operation.LiteralReferences[i];
            if (!matches)
                throw new InvalidOperationException($"Operation template '{operation.TemplateIdentity}' has conflicting immutable data across playback instances.");
        }

        static ProgramConstant LowerLiteral(SemanticLiteral literal, List<Float32ScalarConversion> conversions)
        {
            switch (literal.Kind)
            {
                case SemanticLiteralKind.Boolean:
                    return ProgramConstant.FromBoolean(literal.Index, literal.Identity, literal.Boolean);
                case SemanticLiteralKind.Int32:
                    return ProgramConstant.FromInt32(literal.Index, literal.Identity, literal.Int32);
                case SemanticLiteralKind.UInt64:
                    return ProgramConstant.FromUInt64(literal.Index, literal.Identity, literal.UInt64);
                case SemanticLiteralKind.Number:
                    return ProgramConstant.FromScalar(literal.Index, literal.Identity, LowerNumber(literal.X, literal.Identity, literal.Precision, conversions));
                case SemanticLiteralKind.Vector2:
                    return ProgramConstant.FromVector2(literal.Index, literal.Identity, new Float32Vector2(
                        LowerNumber(literal.X, $"{literal.Identity}.x", literal.Precision, conversions),
                        LowerNumber(literal.Y, $"{literal.Identity}.y", literal.Precision, conversions)));
                case SemanticLiteralKind.Vector3:
                    return ProgramConstant.FromVector3(literal.Index, literal.Identity, new Float32Vector3(
                        LowerNumber(literal.X, $"{literal.Identity}.x", literal.Precision, conversions),
                        LowerNumber(literal.Y, $"{literal.Identity}.y", literal.Precision, conversions),
                        LowerNumber(literal.Z, $"{literal.Identity}.z", literal.Precision, conversions)));
                case SemanticLiteralKind.Yaw:
                    return ProgramConstant.FromYaw(literal.Index, literal.Identity, new Float32Yaw(LowerNumber(literal.X, literal.Identity, literal.Precision, conversions)));
                case SemanticLiteralKind.String:
                    return ProgramConstant.FromString(literal.Index, literal.Identity, literal.Text);
                case SemanticLiteralKind.Quaternion:
                    return ProgramConstant.FromQuaternion(literal.Index, literal.Identity, new Float32Quaternion(
                        LowerNumber(literal.X, literal.Identity + ".x", literal.Precision, conversions),
                        LowerNumber(literal.Y, literal.Identity + ".y", literal.Precision, conversions),
                        LowerNumber(literal.Z, literal.Identity + ".z", literal.Precision, conversions),
                        LowerNumber(literal.W, literal.Identity + ".w", literal.Precision, conversions)));
                case SemanticLiteralKind.Enum:
                    return ProgramConstant.FromEnum(literal.Index, literal.Identity, literal.Text, literal.Int32);
                case SemanticLiteralKind.Document:
                    return ProgramConstant.FromBytes(literal.Index, literal.Identity, LowerDocument(literal.Document, conversions));
                default:
                    throw new InvalidOperationException($"Semantic literal '{literal.Identity}' kind '{literal.Kind}' is not supported by Float32 Target.");
            }
        }

        static byte[] LowerDocument(SemanticDataDocument document, List<Float32ScalarConversion> conversions)
        {
            using var writer = new CanonicalWriter();
            for (int i = 0; i < document.Tokens.Count; i++)
            {
                SemanticDataToken token = document.Tokens[i];
                switch (token.Kind)
                {
                    case SemanticDataTokenKind.Boolean: writer.WriteBoolean(token.Boolean); break;
                    case SemanticDataTokenKind.Int32: writer.WriteInt32(token.Int32); break;
                    case SemanticDataTokenKind.UInt32: writer.WriteUInt32(token.UInt32); break;
                    case SemanticDataTokenKind.UInt64: writer.WriteUInt64(token.UInt64); break;
                    case SemanticDataTokenKind.String: writer.WriteText(token.Text); break;
                    case SemanticDataTokenKind.Number: writer.WriteScalar(LowerNumber(token.Number, token.SourceIdentity, token.Precision, conversions)); break;
                    case SemanticDataTokenKind.Bytes: writer.WriteBytes(token.Bytes.ToArray()); break;
                    default: throw new InvalidOperationException($"Semantic document token '{token.Kind}' is not supported by Float32 Target.");
                }
            }
            return writer.ToArray();
        }

        static Float32Scalar LowerNumber(
            double value,
            string sourceIdentity,
            SemanticNumericPrecision precision,
            List<Float32ScalarConversion> conversions)
        {
            Float32ScalarConversion conversion = Float32ScalarBoundary.LowerAuthoring(value, sourceIdentity);
            if (precision == SemanticNumericPrecision.Exact && conversion.WasRounded)
                throw new InvalidOperationException($"Semantic number '{sourceIdentity}' requires exact representation in Float32 Target.");
            conversions.Add(conversion);
            return conversion.Value;
        }
    }
}
