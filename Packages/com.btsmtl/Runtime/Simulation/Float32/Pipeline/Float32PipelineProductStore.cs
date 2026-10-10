using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public enum Float32PipelineProductLifetime : byte
    {
        OuterTransaction = 1,
        SimulationStep = 2
    }

    public interface IFloat32PipelineProductSlot
    {
        SimulationPipelineProductContract Contract { get; }
        Float32PipelineProductLifetime Lifetime { get; }
        void BeginOuterTransaction();
        void BeginSimulationStep();
    }

    public interface IFloat32PipelineProductSlotFactory
    {
        SimulationPipelineProductContract Contract { get; }
        Float32PipelineProductLifetime Lifetime { get; }
        IFloat32PipelineProductSlot Create();
    }

    public sealed class Float32ExclusiveProductSlotFactory<T> : IFloat32PipelineProductSlotFactory
    {
        public Float32ExclusiveProductSlotFactory(
            SimulationPipelineProductContract contract,
            Float32PipelineProductLifetime lifetime)
        {
            if (contract == null || contract.Multiplicity != SimulationPipelineProductMultiplicity.Exclusive)
                throw new ArgumentException("Exclusive Product slot factory requires an exclusive contract.", nameof(contract));
            Contract = contract;
            Lifetime = lifetime;
        }

        public SimulationPipelineProductContract Contract { get; }
        public Float32PipelineProductLifetime Lifetime { get; }
        public IFloat32PipelineProductSlot Create() => new Float32ExclusiveProductSlot<T>(Contract, Lifetime);
    }

    public sealed class Float32AppendProductSlotFactory<T> : IFloat32PipelineProductSlotFactory
    {
        public Float32AppendProductSlotFactory(
            SimulationPipelineProductContract contract,
            Float32PipelineProductLifetime lifetime)
        {
            if (contract == null || contract.Multiplicity != SimulationPipelineProductMultiplicity.AppendOnly)
                throw new ArgumentException("Append Product slot factory requires an append-only contract.", nameof(contract));
            Contract = contract;
            Lifetime = lifetime;
        }

        public SimulationPipelineProductContract Contract { get; }
        public Float32PipelineProductLifetime Lifetime { get; }
        public IFloat32PipelineProductSlot Create() => new Float32AppendProductSlot<T>(Contract, Lifetime);
    }

    public sealed class Float32ExclusiveProductSlot<T> :
        IFloat32PipelineProductSlot,
        IReadOnlySimulationPipelineProductPort<T>,
        IExclusiveSimulationPipelineProductWriter<T>
    {
        T m_Value;

        public Float32ExclusiveProductSlot(
            SimulationPipelineProductContract contract,
            Float32PipelineProductLifetime lifetime)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            if (contract.Multiplicity != SimulationPipelineProductMultiplicity.Exclusive)
                throw new ArgumentException("Product slot contract is not exclusive.", nameof(contract));
            Lifetime = lifetime;
        }

        public SimulationPipelineProductContract Contract { get; }
        public Float32PipelineProductLifetime Lifetime { get; }
        public bool HasValue { get; private set; }

        public T Read()
        {
            if (!HasValue)
                throw new InvalidOperationException($"Pipeline Product '{Contract.ProductId}' has not been produced.");
            return m_Value;
        }

        public void Write(T value)
        {
            if (HasValue)
                throw new InvalidOperationException($"Exclusive Pipeline Product '{Contract.ProductId}' already has a producer value.");
            if (!typeof(T).IsValueType && EqualityComparer<T>.Default.Equals(value, default))
                throw new ArgumentNullException(nameof(value));
            m_Value = value;
            HasValue = true;
        }

        public void BeginOuterTransaction() => Reset();

        public void BeginSimulationStep()
        {
            if (Lifetime == Float32PipelineProductLifetime.SimulationStep)
                Reset();
        }

        void Reset()
        {
            m_Value = default;
            HasValue = false;
        }
    }

    public sealed class Float32AppendProductSlot<T> :
        IFloat32PipelineProductSlot,
        IReadOnlySimulationPipelineAppendPort<T>,
        IAppendOnlySimulationPipelineProductWriter<T>
    {
        static readonly bool s_ReleasesValues = typeof(IFloat32PipelineProductValueRelease).IsAssignableFrom(typeof(T));
        static readonly Comparison<SimulationPipelineAppendProductEntry<T>> s_EntryComparison = CompareEntries;
        readonly List<SimulationPipelineAppendProductEntry<T>> m_Entries =
            new List<SimulationPipelineAppendProductEntry<T>>();
        bool m_Sealed;

        static Float32AppendProductSlot() => Array.Sort(Array.Empty<SimulationPipelineAppendProductEntry<T>>(), s_EntryComparison);

        public Float32AppendProductSlot(
            SimulationPipelineProductContract contract,
            Float32PipelineProductLifetime lifetime)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            if (contract.Multiplicity != SimulationPipelineProductMultiplicity.AppendOnly)
                throw new ArgumentException("Product slot contract is not append-only.", nameof(contract));
            Lifetime = lifetime;
        }

        public SimulationPipelineProductContract Contract { get; }
        public Float32PipelineProductLifetime Lifetime { get; }
        public int Count
        {
            get
            {
                Seal();
                return m_Entries.Count;
            }
        }

        public void Append(SimulationPipelineAppendEntryIdentity identity, T value)
        {
            if (m_Sealed)
                throw new InvalidOperationException($"Append-only Pipeline Product '{Contract.ProductId}' is already sealed.");
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            m_Entries.Add(new SimulationPipelineAppendProductEntry<T>(identity, value));
        }

        public SimulationPipelineAppendProductEntry<T> Get(int index)
        {
            Seal();
            return m_Entries[index];
        }

        internal int UnsealedCount => m_Entries.Count;
        internal SimulationPipelineAppendProductEntry<T> GetUnsealed(int index) => m_Entries[index];

        public void BeginOuterTransaction() => Reset();

        public void BeginSimulationStep()
        {
            if (Lifetime == Float32PipelineProductLifetime.SimulationStep)
                Reset();
        }

        void Seal()
        {
            if (m_Sealed)
                return;
            m_Entries.Sort(s_EntryComparison);
            for (int i = 1; i < m_Entries.Count; i++)
            {
                if (m_Entries[i - 1].Identity.CompareTo(m_Entries[i].Identity) == 0)
                    throw new InvalidOperationException($"Append-only Pipeline Product '{Contract.ProductId}' contains duplicate provenance.");
            }
            m_Sealed = true;
        }

        void Reset()
        {
            if (s_ReleasesValues)
                for (int index = 0; index < m_Entries.Count; index++)
                    ((IFloat32PipelineProductValueRelease)(object)m_Entries[index].Value).ReleaseOwnedValue();
            m_Entries.Clear();
            m_Sealed = false;
        }

        static int CompareEntries(SimulationPipelineAppendProductEntry<T> left,
            SimulationPipelineAppendProductEntry<T> right) => left.Identity.CompareTo(right.Identity);
    }

    public sealed class Float32PipelineProductRuntimeCatalog
    {
        readonly ReadOnlyCollection<IFloat32PipelineProductSlotFactory> m_Factories;

        public Float32PipelineProductRuntimeCatalog(IEnumerable<IFloat32PipelineProductSlotFactory> factories)
        {
            var values = factories == null
                ? new List<IFloat32PipelineProductSlotFactory>()
                : new List<IFloat32PipelineProductSlotFactory>(factories);
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null)
                    throw new ArgumentException("Product runtime catalog contains a missing factory.", nameof(factories));
            }
            values.Sort((left, right) => left.Contract.ProductId.CompareTo(right.Contract.ProductId));
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i - 1].Contract.ProductId.Equals(values[i].Contract.ProductId))
                    throw new ArgumentException("Product runtime catalog contains duplicate Product identity.", nameof(factories));
            }
            m_Factories = values.AsReadOnly();
        }

        public IReadOnlyList<IFloat32PipelineProductSlotFactory> Factories => m_Factories;

        public void RequireProducts(IReadOnlyList<SimulationPipelineProductContract> products)
        {
            if (products == null)
                throw new ArgumentNullException(nameof(products));
            for (int i = 0; i < products.Count; i++)
                FindRequired(products[i]);
        }

        public Float32PipelineProductStore CreateStore(IReadOnlyList<SimulationPipelineProductContract> products)
        {
            var slots = new List<IFloat32PipelineProductSlot>();
            for (int i = 0; i < products.Count; i++)
            {
                IFloat32PipelineProductSlotFactory factory = FindRequired(products[i]);
                slots.Add(factory.Create());
            }
            return new Float32PipelineProductStore(slots);
        }

        IFloat32PipelineProductSlotFactory FindRequired(SimulationPipelineProductContract product)
        {
            for (int i = 0; i < m_Factories.Count; i++)
            {
                if (!m_Factories[i].Contract.ProductId.Equals(product.ProductId))
                    continue;
                if (!m_Factories[i].Contract.Equals(product))
                    throw new InvalidOperationException($"Product runtime factory '{product.ProductId}' has another schema contract.");
                return m_Factories[i];
            }
            throw new KeyNotFoundException($"Product runtime factory '{product.ProductId}' is not installed.");
        }
    }

    public sealed class Float32PipelineProductStore : IDisposable
    {
        readonly ReadOnlyCollection<IFloat32PipelineProductSlot> m_Slots;

        public Float32PipelineProductStore(IEnumerable<IFloat32PipelineProductSlot> slots)
        {
            var values = slots == null
                ? new List<IFloat32PipelineProductSlot>()
                : new List<IFloat32PipelineProductSlot>(slots);
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null)
                    throw new ArgumentException("Pipeline Product store contains a missing slot.", nameof(slots));
            }
            values.Sort((left, right) => left.Contract.ProductId.CompareTo(right.Contract.ProductId));
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0 && values[i - 1].Contract.ProductId.Equals(values[i].Contract.ProductId))
                    throw new ArgumentException("Pipeline Product store contains a missing or duplicate slot.", nameof(slots));
            }
            m_Slots = values.AsReadOnly();
        }

        public void BeginOuterTransaction()
        {
            for (int i = 0; i < m_Slots.Count; i++)
                m_Slots[i].BeginOuterTransaction();
        }

        public void Dispose() => BeginOuterTransaction();

        public void BeginSimulationStep()
        {
            for (int i = 0; i < m_Slots.Count; i++)
                m_Slots[i].BeginSimulationStep();
        }

        public Float32PipelineProductPortBinder Bind(SimulationPipelinePassDescriptor pass)
        {
            return new Float32PipelineProductPortBinder(pass, this);
        }

        internal TSlot GetRequired<TSlot>(SimulationPipelineProductContract contract)
            where TSlot : class, IFloat32PipelineProductSlot
        {
            for (int i = 0; i < m_Slots.Count; i++)
            {
                IFloat32PipelineProductSlot slot = m_Slots[i];
                if (!slot.Contract.ProductId.Equals(contract.ProductId))
                    continue;
                if (!slot.Contract.Equals(contract) || slot is not TSlot typed)
                    throw new InvalidOperationException($"Pipeline Product slot '{contract.ProductId}' has another contract or value type.");
                return typed;
            }
            throw new KeyNotFoundException($"Pipeline Product slot '{contract.ProductId}' is missing.");
        }

        internal bool TryGet<TSlot>(SimulationPipelineProductContract contract, out TSlot typed)
            where TSlot : class, IFloat32PipelineProductSlot
        {
            for (int i = 0; i < m_Slots.Count; i++)
            {
                IFloat32PipelineProductSlot slot = m_Slots[i];
                if (!slot.Contract.ProductId.Equals(contract.ProductId))
                    continue;
                if (!slot.Contract.Equals(contract) || slot is not TSlot value)
                    throw new InvalidOperationException($"Pipeline Product slot '{contract.ProductId}' has another contract or value type.");
                typed = value;
                return true;
            }
            typed = null;
            return false;
        }
    }

    public sealed class Float32PipelineProductPortBinder
    {
        readonly SimulationPipelinePassDescriptor m_Pass;
        readonly Float32PipelineProductStore m_Store;
        readonly HashSet<string> m_BoundAccesses = new HashSet<string>(StringComparer.Ordinal);

        internal Float32PipelineProductPortBinder(
            SimulationPipelinePassDescriptor pass,
            Float32PipelineProductStore store)
        {
            m_Pass = pass ?? throw new ArgumentNullException(nameof(pass));
            m_Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public IReadOnlySimulationPipelineProductPort<T> BindExclusiveReader<T>(SimulationPipelineProductContract contract)
        {
            RequireAccess(contract, SimulationPipelineProductAccessKind.ReadOnlyConsumer);
            return m_Store.GetRequired<Float32ExclusiveProductSlot<T>>(contract);
        }

        public IReadOnlySimulationPipelineAppendPort<T> BindAppendReader<T>(SimulationPipelineProductContract contract)
        {
            RequireAccess(contract, SimulationPipelineProductAccessKind.ReadOnlyConsumer);
            return m_Store.GetRequired<Float32AppendProductSlot<T>>(contract);
        }

        public IExclusiveSimulationPipelineProductWriter<T> BindExclusiveWriter<T>(SimulationPipelineProductContract contract)
        {
            RequireAccess(contract, SimulationPipelineProductAccessKind.ExclusiveProducer);
            return m_Store.GetRequired<Float32ExclusiveProductSlot<T>>(contract);
        }

        public IAppendOnlySimulationPipelineProductWriter<T> BindAppendWriter<T>(SimulationPipelineProductContract contract)
        {
            RequireAccess(contract, SimulationPipelineProductAccessKind.AppendOnlyProducer);
            return m_Store.GetRequired<Float32AppendProductSlot<T>>(contract);
        }

        void RequireAccess(SimulationPipelineProductContract contract, SimulationPipelineProductAccessKind access)
        {
            for (int i = 0; i < m_Pass.ProductAccesses.Count; i++)
            {
                SimulationPipelineProductAccess declared = m_Pass.ProductAccesses[i];
                if (declared.Access == access && declared.Product.Equals(contract))
                {
                    string key = AccessKey(contract.ProductId, access);
                    if (!m_BoundAccesses.Add(key))
                        throw new InvalidOperationException($"Pass '{m_Pass.PassId}' bound Product '{contract.ProductId}' access '{access}' more than once.");
                    return;
                }
            }
            throw new InvalidOperationException($"Pass '{m_Pass.PassId}' did not declare '{access}' access to Product '{contract.ProductId}'.");
        }

        internal void RequireCompleteBindings()
        {
            for (int i = 0; i < m_Pass.ProductAccesses.Count; i++)
            {
                SimulationPipelineProductAccess access = m_Pass.ProductAccesses[i];
                if (!m_BoundAccesses.Contains(AccessKey(access.Product.ProductId, access.Access)))
                {
                    throw new InvalidOperationException($"Pass '{m_Pass.PassId}' did not bind declared Product '{access.Product.ProductId}' access '{access.Access}'.");
                }
            }
        }

        static string AccessKey(SimulationPipelineProductId productId, SimulationPipelineProductAccessKind access)
        {
            return $"{productId.Value}|{(int)access}";
        }
    }
}
