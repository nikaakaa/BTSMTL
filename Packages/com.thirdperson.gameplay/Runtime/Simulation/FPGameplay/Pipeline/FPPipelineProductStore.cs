using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation.FPGameplay
{
    public enum FPPipelineProductLifetime : byte
    {
        OuterTransaction = 1,
        SimulationStep = 2
    }

    public interface IFPPipelineProductSlot
    {
        SimulationPipelineProductContract Contract { get; }
        FPPipelineProductLifetime Lifetime { get; }
        void BeginOuterTransaction();
        void BeginSimulationStep();
    }

    public interface IFPPipelineProductSlotFactory
    {
        SimulationPipelineProductContract Contract { get; }
        FPPipelineProductLifetime Lifetime { get; }
        IFPPipelineProductSlot Create();
    }

    public sealed class FPExclusiveProductSlotFactory<T> : IFPPipelineProductSlotFactory
    {
        public FPExclusiveProductSlotFactory(
            SimulationPipelineProductContract contract,
            FPPipelineProductLifetime lifetime)
        {
            if (contract == null || contract.Multiplicity != SimulationPipelineProductMultiplicity.Exclusive)
                throw new ArgumentException("Exclusive Product slot factory requires an exclusive contract.", nameof(contract));
            Contract = contract;
            Lifetime = lifetime;
        }

        public SimulationPipelineProductContract Contract { get; }
        public FPPipelineProductLifetime Lifetime { get; }
        public IFPPipelineProductSlot Create() => new FPExclusiveProductSlot<T>(Contract, Lifetime);
    }

    public sealed class FPAppendProductSlotFactory<T> : IFPPipelineProductSlotFactory
    {
        public FPAppendProductSlotFactory(
            SimulationPipelineProductContract contract,
            FPPipelineProductLifetime lifetime)
        {
            if (contract == null || contract.Multiplicity != SimulationPipelineProductMultiplicity.AppendOnly)
                throw new ArgumentException("Append Product slot factory requires an append-only contract.", nameof(contract));
            Contract = contract;
            Lifetime = lifetime;
        }

        public SimulationPipelineProductContract Contract { get; }
        public FPPipelineProductLifetime Lifetime { get; }
        public IFPPipelineProductSlot Create() => new FPAppendProductSlot<T>(Contract, Lifetime);
    }

    public sealed class FPExclusiveProductSlot<T> :
        IFPPipelineProductSlot,
        IReadOnlySimulationPipelineProductPort<T>,
        IExclusiveSimulationPipelineProductWriter<T>
    {
        T m_Value;

        public FPExclusiveProductSlot(
            SimulationPipelineProductContract contract,
            FPPipelineProductLifetime lifetime)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            if (contract.Multiplicity != SimulationPipelineProductMultiplicity.Exclusive)
                throw new ArgumentException("Product slot contract is not exclusive.", nameof(contract));
            Lifetime = lifetime;
        }

        public SimulationPipelineProductContract Contract { get; }
        public FPPipelineProductLifetime Lifetime { get; }
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
            if (Lifetime == FPPipelineProductLifetime.SimulationStep)
                Reset();
        }

        void Reset()
        {
            m_Value = default;
            HasValue = false;
        }
    }

    public sealed class FPAppendProductSlot<T> :
        IFPPipelineProductSlot,
        IReadOnlySimulationPipelineAppendPort<T>,
        IAppendOnlySimulationPipelineProductWriter<T>
    {
        static readonly Comparison<SimulationPipelineAppendProductEntry<T>> s_EntryComparison = CompareEntries;
        readonly List<SimulationPipelineAppendProductEntry<T>> m_Entries =
            new List<SimulationPipelineAppendProductEntry<T>>();
        bool m_Sealed;

        static FPAppendProductSlot() => Array.Sort(Array.Empty<SimulationPipelineAppendProductEntry<T>>(), s_EntryComparison);

        public FPAppendProductSlot(
            SimulationPipelineProductContract contract,
            FPPipelineProductLifetime lifetime)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            if (contract.Multiplicity != SimulationPipelineProductMultiplicity.AppendOnly)
                throw new ArgumentException("Product slot contract is not append-only.", nameof(contract));
            Lifetime = lifetime;
        }

        public SimulationPipelineProductContract Contract { get; }
        public FPPipelineProductLifetime Lifetime { get; }
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

        public void PrepareCapacity(int capacity) => m_Entries.Capacity = capacity;

        internal int UnsealedCount => m_Entries.Count;
        internal SimulationPipelineAppendProductEntry<T> GetUnsealed(int index) => m_Entries[index];

        public void BeginOuterTransaction() => Reset();

        public void BeginSimulationStep()
        {
            if (Lifetime == FPPipelineProductLifetime.SimulationStep)
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
            for (int i = 0; i < m_Entries.Count; i++)
                if (m_Entries[i].Value is IFPPipelineProductValueRelease releasable)
                    releasable.ReleaseOwnedValue();
            m_Entries.Clear();
            m_Sealed = false;
        }

        static int CompareEntries(SimulationPipelineAppendProductEntry<T> left,
            SimulationPipelineAppendProductEntry<T> right) => left.Identity.CompareTo(right.Identity);
    }

    public sealed class FPPipelineProductRuntimeCatalog
    {
        readonly ReadOnlyCollection<IFPPipelineProductSlotFactory> m_Factories;

        public FPPipelineProductRuntimeCatalog(IEnumerable<IFPPipelineProductSlotFactory> factories)
        {
            var values = factories == null
                ? new List<IFPPipelineProductSlotFactory>()
                : new List<IFPPipelineProductSlotFactory>(factories);
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

        public IReadOnlyList<IFPPipelineProductSlotFactory> Factories => m_Factories;

        public void RequireProducts(IReadOnlyList<SimulationPipelineProductContract> products)
        {
            if (products == null)
                throw new ArgumentNullException(nameof(products));
            for (int i = 0; i < products.Count; i++)
                FindRequired(products[i]);
        }

        public FPPipelineProductStore CreateStore(IReadOnlyList<SimulationPipelineProductContract> products)
        {
            var slots = new List<IFPPipelineProductSlot>();
            for (int i = 0; i < products.Count; i++)
            {
                IFPPipelineProductSlotFactory factory = FindRequired(products[i]);
                slots.Add(factory.Create());
            }
            return new FPPipelineProductStore(slots);
        }

        IFPPipelineProductSlotFactory FindRequired(SimulationPipelineProductContract product)
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

    public sealed class FPPipelineProductStore : IDisposable
    {
        readonly ReadOnlyCollection<IFPPipelineProductSlot> m_Slots;

        public FPPipelineProductStore(IEnumerable<IFPPipelineProductSlot> slots)
        {
            var values = slots == null
                ? new List<IFPPipelineProductSlot>()
                : new List<IFPPipelineProductSlot>(slots);
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

        public FPPipelineProductPortBinder Bind(SimulationPipelinePassDescriptor pass)
        {
            return new FPPipelineProductPortBinder(pass, this);
        }

        internal TSlot GetRequired<TSlot>(SimulationPipelineProductContract contract)
            where TSlot : class, IFPPipelineProductSlot
        {
            for (int i = 0; i < m_Slots.Count; i++)
            {
                IFPPipelineProductSlot slot = m_Slots[i];
                if (!slot.Contract.ProductId.Equals(contract.ProductId))
                    continue;
                if (!slot.Contract.Equals(contract) || slot is not TSlot typed)
                    throw new InvalidOperationException($"Pipeline Product slot '{contract.ProductId}' has another contract or value type.");
                return typed;
            }
            throw new KeyNotFoundException($"Pipeline Product slot '{contract.ProductId}' is missing.");
        }

        internal bool TryGet<TSlot>(SimulationPipelineProductContract contract, out TSlot typed)
            where TSlot : class, IFPPipelineProductSlot
        {
            for (int i = 0; i < m_Slots.Count; i++)
            {
                IFPPipelineProductSlot slot = m_Slots[i];
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

    public sealed class FPPipelineProductPortBinder
    {
        readonly SimulationPipelinePassDescriptor m_Pass;
        readonly FPPipelineProductStore m_Store;
        readonly HashSet<string> m_BoundAccesses = new HashSet<string>(StringComparer.Ordinal);

        internal FPPipelineProductPortBinder(
            SimulationPipelinePassDescriptor pass,
            FPPipelineProductStore store)
        {
            m_Pass = pass ?? throw new ArgumentNullException(nameof(pass));
            m_Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public IReadOnlySimulationPipelineProductPort<T> BindExclusiveReader<T>(SimulationPipelineProductContract contract)
        {
            RequireAccess(contract, SimulationPipelineProductAccessKind.ReadOnlyConsumer);
            return m_Store.GetRequired<FPExclusiveProductSlot<T>>(contract);
        }

        public IReadOnlySimulationPipelineAppendPort<T> BindAppendReader<T>(SimulationPipelineProductContract contract)
        {
            RequireAccess(contract, SimulationPipelineProductAccessKind.ReadOnlyConsumer);
            return m_Store.GetRequired<FPAppendProductSlot<T>>(contract);
        }

        public IExclusiveSimulationPipelineProductWriter<T> BindExclusiveWriter<T>(SimulationPipelineProductContract contract)
        {
            RequireAccess(contract, SimulationPipelineProductAccessKind.ExclusiveProducer);
            return m_Store.GetRequired<FPExclusiveProductSlot<T>>(contract);
        }

        public IAppendOnlySimulationPipelineProductWriter<T> BindAppendWriter<T>(SimulationPipelineProductContract contract)
        {
            RequireAccess(contract, SimulationPipelineProductAccessKind.AppendOnlyProducer);
            return m_Store.GetRequired<FPAppendProductSlot<T>>(contract);
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

