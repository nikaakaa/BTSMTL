namespace ThirdPersonSimulation
{
    public interface IGameplayEffectMagnitudeInputs<TScalar> where TScalar : struct
    {
        bool PreviewSelf { get; }
        bool TryParameter(string id, out TScalar value);
        bool TrySourceAttributeSnapshot(string id, out TScalar value);
        bool TryTargetAttributeSnapshot(string id, out TScalar value);
        TScalar ReadCurrentAttribute(string id);
    }

    public static class GameplayEffectMagnitudeCalculation<TScalar, TMath>
        where TScalar : struct
        where TMath : struct, IScalarMath<TScalar>
    {
        public static bool TryResolve<TInputs>(TInputs input, in GameplayEffectMagnitude<TScalar> magnitude,
            out TScalar value, out string liveAttribute)
            where TInputs : struct, IGameplayEffectMagnitudeInputs<TScalar>
        {
            TMath math = default;
            value = math.Zero;
            liveAttribute = string.Empty;
            switch (magnitude.Source)
            {
                case PortableMagnitudeSource.Constant:
                    value = magnitude.Constant;
                    break;
                case PortableMagnitudeSource.SetByCaller:
                    if (!input.TryParameter(magnitude.SetByCallerParameterId, out value))
                        return false;
                    break;
                case PortableMagnitudeSource.SourceAttributeSnapshot when !input.PreviewSelf:
                    if (!input.TrySourceAttributeSnapshot(magnitude.AttributeId, out value))
                        return false;
                    break;
                case PortableMagnitudeSource.TargetAttributeSnapshot when !input.PreviewSelf:
                    if (!input.TryTargetAttributeSnapshot(magnitude.AttributeId, out value))
                        return false;
                    break;
                case PortableMagnitudeSource.SourceAttributeSnapshot:
                case PortableMagnitudeSource.TargetAttributeSnapshot:
                case PortableMagnitudeSource.TargetAttributeLive:
                    value = input.ReadCurrentAttribute(magnitude.AttributeId);
                    if (magnitude.Source == PortableMagnitudeSource.TargetAttributeLive)
                        liveAttribute = magnitude.AttributeId;
                    break;
                default:
                    return false;
            }
            value = math.Add(math.Multiply(value, magnitude.Coefficient), magnitude.PostAdd);
            return true;
        }
    }
}
