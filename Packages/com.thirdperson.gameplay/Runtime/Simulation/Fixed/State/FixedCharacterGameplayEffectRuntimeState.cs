using System;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedCharacterGameplayEffectRuntimeState : IFixedGameplayEffectStatePort
    {
        FixedGameplayEffectRuntimeCatalog m_Catalog;
        int m_TickRate;
        GameplayEffectStateAggregate m_Aggregate;
        SimulationGameplayEffectState m_Working;
        FixedGameplayEffectExecutionScratch m_Scratch;
        bool m_Disposed;

        public FixedCharacterGameplayEffectRuntimeState()
        {
        }

        internal GameplayEffectStateAggregate CreateInitial(
            FixedGameplayEffectRuntimeCatalog catalog,
            FixedGameplayEffectExecutionScratch scratch)
        {
            m_Catalog = catalog;
            m_Scratch = scratch;
            m_Working = new SimulationGameplayEffectState(catalog, scratch);
            m_Aggregate = m_Working.Freeze();
            m_Disposed = false;
            return m_Aggregate;
        }

        public FixedCharacterGameplayEffectRuntimeState Restart(
            int tickRate,
            FixedGameplayEffectRuntimeCatalog catalog,
            GameplayEffectStateAggregate aggregate)
        {
            m_TickRate = tickRate;
            m_Catalog = catalog;
            m_Aggregate = aggregate;
            if (m_Working != null && m_Aggregate.IsValid && !m_Working.IsCurrentSnapshot(m_Aggregate))
                m_Working.Restore(m_Aggregate);
            m_Disposed = false;
            return this;
        }

        public int TickRate => m_TickRate;

        public SimulationGameplayEffectState GetGameplayEffectState(FixedGameplayEffectExecutionScratch scratch)
        {
            RequireActive();
            if (scratch == null)
                throw new ArgumentNullException(nameof(scratch));
            if (m_Catalog == null || !m_Aggregate.IsValid)
                throw new InvalidOperationException("Character does not install Gameplay Effect state.");
            if (!ReferenceEquals(m_Scratch, scratch))
                throw new InvalidOperationException("Gameplay Effect state is bound to another Actor workspace.");
            return m_Working;
        }

        public GameplayEffectStateAggregate GetGameplayEffectAggregate()
        {
            RequireActive();
            return !m_Aggregate.IsValid
                ? throw new InvalidOperationException("Character does not install Gameplay Effect state.")
                : m_Working.Freeze();
        }

        internal GameplayEffectStateAggregate Capture()
        {
            RequireActive();
            return !m_Aggregate.IsValid ? default : m_Working.Freeze();
        }

        internal GameplayEffectStateAggregate Commit()
        {
            RequireActive();
            if (!m_Aggregate.IsValid)
                return default;
            m_Aggregate = m_Working.Freeze();
            return m_Aggregate;
        }

        internal void Restore(GameplayEffectStateAggregate aggregate)
        {
            RequireActive();
            m_Aggregate = aggregate;
            if (m_Aggregate.IsValid)
                m_Working.Restore(m_Aggregate);
        }

        internal void Dispose()
        {
            m_Disposed = true;
        }

        internal void ReleaseStorage()
        {
            Dispose();
            m_Working.ReleaseStorage();
            m_Scratch.Reset();
            m_Aggregate = default;
            m_Working = null;
            m_Scratch = null;
            m_Catalog = null;
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FixedCharacterGameplayEffectRuntimeState));
        }
    }
}
