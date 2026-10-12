using System;

namespace ThirdPersonSimulation
{
    internal sealed class Float32CharacterGameplayEffectRuntimeState : IFloat32GameplayEffectStatePort
    {
        Float32GameplayEffectRuntimeCatalog m_Catalog;
        int m_TickRate;
        GameplayEffectStateAggregate m_Aggregate;
        SimulationGameplayEffectState m_Working;
        Float32GameplayEffectExecutionScratch m_Scratch;
        readonly CanonicalReader m_DecodeReader = new CanonicalReader(Array.Empty<byte>());
        readonly CanonicalReader m_DecodeBlockReader = new CanonicalReader(Array.Empty<byte>());
        GameplayEffectStateAggregate m_DecodeCandidate;
        bool m_Disposed;

        public Float32CharacterGameplayEffectRuntimeState()
        {
        }

        internal GameplayEffectStateAggregate CreateInitial(
            Float32GameplayEffectRuntimeCatalog catalog,
            Float32GameplayEffectExecutionScratch scratch)
        {
            m_Catalog = catalog;
            m_Scratch = scratch;
            m_DecodeReader.DeclaredIdentityReader = catalog.LifecycleIdentities.ReadPublished;
            m_DecodeBlockReader.DeclaredIdentityReader = catalog.LifecycleIdentities.ReadPublished;
            m_DecodeReader.PublishedValues = catalog.PublishedValues;
            m_DecodeBlockReader.PublishedValues = catalog.PublishedValues;
            m_Working = new SimulationGameplayEffectState(catalog, scratch);
            m_Aggregate = m_Working.Freeze();
            m_Disposed = false;
            return m_Aggregate;
        }

        public Float32CharacterGameplayEffectRuntimeState Restart(
            int tickRate,
            Float32GameplayEffectRuntimeCatalog catalog,
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

        public SimulationGameplayEffectState GetGameplayEffectState(Float32GameplayEffectExecutionScratch scratch)
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
            m_DecodeCandidate.ReleaseSnapshots();
            m_DecodeCandidate = default;
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
            m_DecodeReader.DeclaredIdentityReader = null;
            m_DecodeBlockReader.DeclaredIdentityReader = null;
            m_DecodeReader.PublishedValues = null;
            m_DecodeBlockReader.PublishedValues = null;
            m_DecodeReader.Reset(Array.Empty<byte>());
            m_DecodeBlockReader.Reset(Array.Empty<byte>());
        }

        internal GameplayEffectStateAggregate Decode(ArraySegment<byte> bytes, EquipmentStatePageBank equipment,
            ICanonicalTextResolver textResolver)
        {
            m_DecodeCandidate.ReleaseSnapshots();
            m_DecodeCandidate = default;
            m_DecodeReader.Reset(bytes);
            m_DecodeReader.SetTextResolver(textResolver);
            m_DecodeBlockReader.SetTextResolver(textResolver);
            try
            {
                m_DecodeCandidate = m_Working.Decode(m_DecodeReader, m_DecodeBlockReader, equipment);
                return m_DecodeCandidate;
            }
            finally
            {
                m_DecodeReader.SetTextResolver(null);
                m_DecodeBlockReader.SetTextResolver(null);
            }
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Float32CharacterGameplayEffectRuntimeState));
        }
    }
}
