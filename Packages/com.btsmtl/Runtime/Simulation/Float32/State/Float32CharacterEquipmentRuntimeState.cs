using System;

namespace ThirdPersonSimulation
{
    internal sealed class Float32CharacterEquipmentRuntimeState : IFloat32EquipmentStatePort
    {
        EquipmentStateAggregate m_State;
        EquipmentStatePageBank m_Storage;
        bool m_Disposed;

        public Float32CharacterEquipmentRuntimeState()
        {
        }

        internal EquipmentStatePageBank Storage => m_Storage;
        internal EquipmentProgramLayout Layout => m_Storage?.Layout;

        internal void Prepare(ActorId actorId, EquipmentProgramLayout layout, in GameplayEffectSessionCapacity session)
        {
            m_Storage = new EquipmentStatePageBank(actorId, layout, session.StatePageCount, session.SavepointDepth);
        }

        internal EquipmentStateAggregate CreateInitial() => m_Storage.CreateInitial();

        public Float32CharacterEquipmentRuntimeState Restart(EquipmentStateAggregate state)
        {
            state?.Retain();
            m_Storage?.ReleaseDecodedState();
            m_State = state;
            m_Disposed = false;
            return this;
        }

        public EquipmentStateAggregate GetEquipmentState()
        {
            RequireActive();
            return m_State ?? throw new InvalidOperationException("Character does not install Equipment state.");
        }

        public void SetEquipmentState(EquipmentStateAggregate state)
        {
            RequireActive();
            if (ReferenceEquals(m_State, state))
                return;
            state.Retain();
            m_State.Release();
            m_State = state;
        }

        internal EquipmentStateAggregate Capture()
        {
            RequireActive();
            return m_State;
        }

        internal void Restore(EquipmentStateAggregate state)
        {
            RequireActive();
            state?.Retain();
            m_State?.Release();
            m_State = state;
        }

        internal void Dispose()
        {
            m_State?.Release();
            m_State = null;
            m_Storage?.ReleaseDecodedState();
            m_Disposed = true;
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Float32CharacterEquipmentRuntimeState));
        }
    }
}
