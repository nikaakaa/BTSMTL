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
            var parameters = new SimulationOperationParameterValues[semanticOperations.Count];
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
                        operation.LiteralReferences);
                    definitions.Add(definition);
                    definitionByIdentity.Add(operation.TemplateIdentity, definition);
                }
                else
                {
                    RequireMatchingDefinition(definition, operation);
                }
                parameters[i] = new SimulationOperationParameterValues(
                    operation.Integer0, operation.Integer1, operation.Unsigned0,
                    scalar0, operation.Text0, operation.Flags);
                operations[i] = new SimulationOperation(
                    operation.Handle,
                    definition,
                    operation.Operands,
                    operation.StateSlots,
                    parameters,
                    i);
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

        internal static Float32GraphExecutionData LowerParameters(GameplayAbilitySemanticIr semanticIr,
            Float32GraphExecutionData structure, List<Float32ScalarConversion> conversions)
        {
            var constants = new ProgramConstant[semanticIr.Literals.Count];
            for (int i = 0; i < constants.Length; i++)
                constants[i] = LowerLiteral(semanticIr.Literals[i], conversions);
            var parameters = new SimulationOperationParameterValues[semanticIr.Operations.Count];
            var operations = new SimulationOperation[parameters.Length];
            for (int i = 0; i < operations.Length; i++)
            {
                SemanticOperation source = semanticIr.Operations[i];
                string identity = source.Number0SourceIdentity.Length == 0
                    ? $"operation:{source.Handle.Value}/number0" : source.Number0SourceIdentity;
                parameters[i] = new SimulationOperationParameterValues(source.Integer0, source.Integer1, source.Unsigned0,
                    LowerNumber(source.Number0, identity, SemanticNumericPrecision.TargetRounded, conversions), source.Text0, source.Flags);
                operations[i] = new SimulationOperation(structure.Operations[i], parameters);
            }
            return structure.WithParameters(operations, constants, semanticIr.GraphSources);
        }

        static void RequireMatchingDefinition(
            SimulationOperationDefinition definition,
            SemanticOperation operation)
        {
            bool matches = definition.Code == operation.Code &&
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
                    return ProgramConstant.FromBoolean(literal.Identity, literal.Index, literal.Boolean);
                case SemanticLiteralKind.Int32:
                    return ProgramConstant.FromInt32(literal.Identity, literal.Index, literal.Int32);
                case SemanticLiteralKind.UInt64:
                    return ProgramConstant.FromUInt64(literal.Identity, literal.Index, literal.UInt64);
                case SemanticLiteralKind.Number:
                    return ProgramConstant.FromScalar(literal.Identity, literal.Index, LowerNumber(literal.Number.Value, literal.Identity, literal.Number.Precision, conversions));
                case SemanticLiteralKind.Vector2:
                    return ProgramConstant.FromVector2(literal.Identity, literal.Index, new Float32Vector2(
                        LowerNumber(literal.Vector2.X, $"{literal.Identity}.x", literal.Vector2.Precision, conversions),
                        LowerNumber(literal.Vector2.Y, $"{literal.Identity}.y", literal.Vector2.Precision, conversions)));
                case SemanticLiteralKind.Vector3:
                    return ProgramConstant.FromVector3(literal.Identity, literal.Index, new Float32Vector3(
                        LowerNumber(literal.Vector3.X, $"{literal.Identity}.x", literal.Vector3.Precision, conversions),
                        LowerNumber(literal.Vector3.Y, $"{literal.Identity}.y", literal.Vector3.Precision, conversions),
                        LowerNumber(literal.Vector3.Z, $"{literal.Identity}.z", literal.Vector3.Precision, conversions)));
                case SemanticLiteralKind.Yaw:
                    return ProgramConstant.FromYaw(literal.Identity, literal.Index, new Float32Yaw(LowerNumber(literal.Yaw.Value, literal.Identity, literal.Yaw.Precision, conversions)));
                case SemanticLiteralKind.String:
                    return ProgramConstant.FromString(literal.Identity, literal.Index, literal.String);
                case SemanticLiteralKind.Quaternion:
                    return ProgramConstant.FromQuaternion(literal.Identity, literal.Index, new Float32Quaternion(
                        LowerNumber(literal.Quaternion.X, literal.Identity + ".x", SemanticNumericPrecision.TargetRounded, conversions),
                        LowerNumber(literal.Quaternion.Y, literal.Identity + ".y", SemanticNumericPrecision.TargetRounded, conversions),
                        LowerNumber(literal.Quaternion.Z, literal.Identity + ".z", SemanticNumericPrecision.TargetRounded, conversions),
                        LowerNumber(literal.Quaternion.W, literal.Identity + ".w", SemanticNumericPrecision.TargetRounded, conversions)));
                case SemanticLiteralKind.Enum:
                    return ProgramConstant.FromEnum(literal.Identity, literal.Index, new ProgramEnumConstant(literal.Enum.TypeIdentity, literal.Enum.Value));
                case SemanticLiteralKind.Document:
                    return ProgramConstant.FromBytes(literal.Identity, literal.Index, LowerDocument(literal.Document, conversions));
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
                    case SemanticDataTokenKind.String: writer.WriteText(token.String); break;
                    case SemanticDataTokenKind.Number: writer.WriteScalar(LowerNumber(token.Number.Value.Value, token.Number.SourceIdentity, token.Number.Value.Precision, conversions)); break;
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
