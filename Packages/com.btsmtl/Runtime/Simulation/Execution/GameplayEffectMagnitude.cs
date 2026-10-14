using System.IO;
using ThirdPersonGameplay.Configuration;

namespace ThirdPersonSimulation
{
    public enum PortableMagnitudeSource : byte
    {
        Constant = 0,
        SetByCaller = 1,
        SourceAttributeSnapshot = 2,
        TargetAttributeSnapshot = 3,
        TargetAttributeLive = 4
    }

    public interface IScalarContentLowering<TScalar> where TScalar : struct
    {
        TScalar FromSingle(float value);
    }

    public readonly struct GameplayEffectMagnitude<TScalar> where TScalar : struct
    {
        public GameplayEffectMagnitude(PortableMagnitudeSource source, TScalar constant,
            string setByCallerParameterId, string attributeId, TScalar coefficient, TScalar postAdd)
        {
            Source = source;
            Constant = constant;
            SetByCallerParameterId = setByCallerParameterId;
            AttributeId = attributeId;
            Coefficient = coefficient;
            PostAdd = postAdd;
        }

        public PortableMagnitudeSource Source { get; }
        public TScalar Constant { get; }
        public string SetByCallerParameterId { get; }
        public string AttributeId { get; }
        public TScalar Coefficient { get; }
        public TScalar PostAdd { get; }

        public static GameplayEffectMagnitude<TScalar> FromConfiguration<TLowering, TMath>(PublishedMagnitude value)
            where TLowering : struct, IScalarContentLowering<TScalar>
            where TMath : struct, IScalarMath<TScalar>
        {
            TLowering lowering = default;
            TMath math = default;
            switch (value.Kind)
            {
                case MagnitudeValueKind.ConstantMagnitude:
                    return new GameplayEffectMagnitude<TScalar>(PortableMagnitudeSource.Constant,
                        lowering.FromSingle(value.ConstantMagnitude.Value), string.Empty, string.Empty, math.One, math.Zero);
                case MagnitudeValueKind.ScaledConstantMagnitude:
                    PublishedScaledConstantMagnitude constant = value.ScaledConstantMagnitude;
                    return new GameplayEffectMagnitude<TScalar>(PortableMagnitudeSource.Constant,
                        lowering.FromSingle(constant.Value), string.Empty, string.Empty,
                        lowering.FromSingle(constant.Coefficient), lowering.FromSingle(constant.PostAdd));
                case MagnitudeValueKind.SetByCallerMagnitude:
                    PublishedSetByCallerMagnitude caller = value.SetByCallerMagnitude;
                    return new GameplayEffectMagnitude<TScalar>(PortableMagnitudeSource.SetByCaller,
                        math.Zero, caller.ParameterId, string.Empty,
                        lowering.FromSingle(caller.Coefficient), lowering.FromSingle(caller.PostAdd));
                case MagnitudeValueKind.SourceAttributeSnapshotMagnitude:
                    PublishedSourceAttributeSnapshotMagnitude source = value.SourceAttributeSnapshotMagnitude;
                    return FromAttribute<TLowering, TMath>(PortableMagnitudeSource.SourceAttributeSnapshot,
                        source.AttributeId, source.Coefficient, source.PostAdd);
                case MagnitudeValueKind.TargetAttributeSnapshotMagnitude:
                    PublishedTargetAttributeSnapshotMagnitude target = value.TargetAttributeSnapshotMagnitude;
                    return FromAttribute<TLowering, TMath>(PortableMagnitudeSource.TargetAttributeSnapshot,
                        target.AttributeId, target.Coefficient, target.PostAdd);
                case MagnitudeValueKind.TargetAttributeLiveMagnitude:
                    PublishedTargetAttributeLiveMagnitude live = value.TargetAttributeLiveMagnitude;
                    return FromAttribute<TLowering, TMath>(PortableMagnitudeSource.TargetAttributeLive,
                        live.AttributeId, live.Coefficient, live.PostAdd);
                default:
                    throw new InvalidDataException($"Magnitude kind '{value.Kind}' has no projection.");
            }
        }

        static GameplayEffectMagnitude<TScalar> FromAttribute<TLowering, TMath>(
            PortableMagnitudeSource source, string attributeId, float coefficient, float postAdd)
            where TLowering : struct, IScalarContentLowering<TScalar>
            where TMath : struct, IScalarMath<TScalar>
        {
            TLowering lowering = default;
            TMath math = default;
            return new GameplayEffectMagnitude<TScalar>(source, math.Zero, string.Empty,
                GameplayEffectRuntimeIdentity.Attribute(attributeId),
                lowering.FromSingle(coefficient), lowering.FromSingle(postAdd));
        }
    }
}
