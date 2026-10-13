using System;

namespace ThirdPersonSimulation
{
    internal sealed class MotionContributionBuffer<TScalar, TVector2, TVector3, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
    {
        SimulationMotionContribution<TScalar, TVector2, TVector3, TMath>[] m_Values =
            Array.Empty<SimulationMotionContribution<TScalar, TVector2, TVector3, TMath>>();

        public SimulationMotionContribution<TScalar, TVector2, TVector3, TMath>[] Values => m_Values;
        public int Count { get; private set; }

        public void PrepareCapacity(int capacity) =>
            m_Values = new SimulationMotionContribution<TScalar, TVector2, TVector3, TMath>[capacity];

        public void Append(in SimulationMotionContribution<TScalar, TVector2, TVector3, TMath> value)
        {
            ref SimulationMotionContribution<TScalar, TVector2, TVector3, TMath> slot = ref m_Values[Count];
            value.Retain();
            slot = value;
            Count++;
        }

        public void TransferTo(MotionContributionBuffer<TScalar, TVector2, TVector3, TMath> destination)
        {
            Array.Copy(m_Values, 0, destination.m_Values, destination.Count, Count);
            destination.Count += Count;
            Array.Clear(m_Values, 0, Count);
            Count = 0;
        }

        public void Clear()
        {
            for (int index = 0; index < Count; index++)
                m_Values[index].Release();
            Array.Clear(m_Values, 0, Count);
            Count = 0;
        }
    }
}
