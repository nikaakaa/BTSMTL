#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using FlowCanvas;
using NodeCanvas.Framework;
using NodeCanvas.Framework.Internal;
using ParadoxNotion.Serialization.FullSerializer;
using ThirdPersonSimulation;
using UnityEditor;
using UnityEngine;

namespace BTSMTL
{
    public class BtsmtlGraphFingerprint
    {
        static readonly ConditionalWeakTable<fsMetaType, string[]> s_EditorProperties = new();
        static readonly ConditionalWeakTable<fsMetaType, string[]>.CreateValueCallback s_CollectEditorProperties = CollectEditorProperties;
        readonly Dictionary<Graph, string> m_Hashes = new();
        readonly Dictionary<Graph, (string Json, List<UnityEngine.Object> References)> m_GraphSources = new();
        readonly HashSet<Graph> m_Active = new();
        readonly Dictionary<string, string> m_AssetHashes = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> m_DependencyHashes = new(StringComparer.Ordinal);
        readonly Dictionary<string, string[]> m_AssetDependencies = new(StringComparer.Ordinal);
        readonly Dictionary<UnityEngine.Object, string> m_ObjectHashes = new();
        protected readonly fsSerializer m_Serializer = new();

        public BtsmtlGraphFingerprint()
        {
            m_Serializer.onAfterObjectSerialized += ExcludeEditorData;
        }

        public string Compute(Graph graph)
        {
            if (m_Hashes.TryGetValue(graph, out string existing))
                return existing;
            if (!m_Active.Add(graph))
                throw new InvalidOperationException("图资产引用形成循环，无法计算版本。");
            try
            {
                var parts = new List<string>();
                parts.Add("btsmtl-graph-source/5");
                List<UnityEngine.Object> references = AppendGraphSource(parts, graph);
                foreach (UnityEngine.Object reference in references)
                {
                    if (reference == null)
                        continue;
                    parts.Add(reference is Graph child
                        ? Compute(child)
                        : ComputeDependencies(AssetDatabase.GetAssetPath(reference)));
                }
                string hash = StableHash.Compute(parts.ToArray()).ToString();
                m_Hashes.Add(graph, hash);
                return hash;
            }
            finally
            {
                m_Active.Remove(graph);
            }
        }

        public string ComputeSourceAsset(string path)
        {
            if (m_AssetHashes.TryGetValue(path, out string existing))
                return existing;
            var parts = new List<string>();
            if (string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("btsmtl-source-asset/3");
                var assets = AssetDatabase.LoadAllAssetsAtPath(path)
                    .Select(asset =>
                    {
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long fileId);
                        return (Asset: asset, FileId: fileId);
                    })
                    .OrderBy(value => value.FileId)
                    .ToArray();
                parts.Add((assets.Length).ToString(CultureInfo.InvariantCulture));
                foreach (var source in assets)
                {
                    parts.Add((source.FileId).ToString(CultureInfo.InvariantCulture));
                    parts.Add(source.Asset.GetType().FullName);
                    parts.Add(ComputeSourceObject(source.Asset));
                }
            }
            else
            {
                parts.Add(SimulationCanonicalPayloadHash.Compute(File.ReadAllBytes(Path.GetFullPath(path))).Value);
            }
            string hash = StableHash.Compute(parts.ToArray()).ToString();
            m_AssetHashes.Add(path, hash);
            return hash;
        }

        public IReadOnlyList<string> CollectAssetDependencies(string path)
        {
            if (m_AssetDependencies.TryGetValue(path, out string[] existing))
                return existing;
            string[] dependencies = EditorUtility.CollectDependencies(AssetDatabase.LoadAllAssetsAtPath(path))
                .Select(AssetDatabase.GetAssetPath)
                .Where(dependency => !string.IsNullOrEmpty(dependency))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(dependency => dependency, StringComparer.Ordinal)
                .ToArray();
            m_AssetDependencies.Add(path, dependencies);
            return dependencies;
        }

        string ComputeDependencies(string path)
        {
            if (m_DependencyHashes.TryGetValue(path, out string existing))
                return existing;
            var parts = new List<string>();
            IReadOnlyList<string> dependencies = CollectAssetDependencies(path);
            parts.Add((dependencies.Count).ToString(CultureInfo.InvariantCulture));
            foreach (string dependency in dependencies)
            {
                parts.Add(dependency);
                parts.Add(AssetDatabase.AssetPathToGUID(dependency));
                parts.Add(string.Equals(Path.GetExtension(dependency), ".asset", StringComparison.OrdinalIgnoreCase)
                    ? ComputeSourceAsset(dependency)
                    : AssetDatabase.GetAssetDependencyHash(dependency).ToString());
            }
            string hash = StableHash.Compute(parts.ToArray()).ToString();
            m_DependencyHashes.Add(path, hash);
            return hash;
        }

        List<UnityEngine.Object> AppendGraphSource(List<string> parts, Graph graph)
        {
            if (!m_GraphSources.TryGetValue(graph, out var source))
            {
                source = SerializeGraphSource(graph);
                m_GraphSources.Add(graph, source);
            }
            parts.Add(source.Json);
            AppendLifecycle(parts, graph, true);
            AppendLifecycle(parts, graph, false);
            parts.Add((source.References.Count).ToString(CultureInfo.InvariantCulture));
            foreach (UnityEngine.Object reference in source.References)
                AppendReference(parts, reference);
            return source.References;
        }

        protected virtual (string Json, List<UnityEngine.Object> References) SerializeGraphSource(Graph graph)
        {
            var snapshot = new GraphSource
            {
                nodes = graph.allNodes.OrderBy(node => node.UID, StringComparer.Ordinal).ToList(),
                localBlackboard = new BlackboardSource { variables = graph.blackboard.variables
                    .OrderBy(value => value.Value.ID, StringComparer.Ordinal)
                    .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal) }
            };
            var references = new List<UnityEngine.Object>();
            m_Serializer.PurgeTemporaryData();
            m_Serializer.ReferencesDatabase = references;
            try
            {
                m_Serializer.TrySerialize(typeof(GraphSource), Pack(snapshot, graph), out fsData data).AssertSuccess();
                Canonicalize(data);
                return (fsJsonPrinter.ToJson(data, false), references);
            }
            finally
            {
                m_Serializer.ReferencesDatabase = null;
            }
        }

        public string ComputeSourceObject(UnityEngine.Object asset)
        {
            if (m_ObjectHashes.TryGetValue(asset, out string existing))
                return existing;
            var parts = new List<string>();
            parts.Add(asset.GetType().FullName);
            if (asset is Graph graph)
                AppendGraphSource(parts, graph);
            else
                AppendSerializedSource(parts, asset);
            string hash = StableHash.Compute(parts.ToArray()).Value;
            m_ObjectHashes.Add(asset, hash);
            return hash;
        }

        static void AppendLifecycle(List<string> parts, Graph graph, bool start)
        {
            FlowCanvas.Macros.MacroNodeWrapper[] children = graph.allNodes.OfType<FlowCanvas.Macros.MacroNodeWrapper>()
                .Where(macro => HasLifecycle(macro.macro, start)).ToArray();
            parts.Add((children.Length).ToString(CultureInfo.InvariantCulture));
            foreach (FlowCanvas.Macros.MacroNodeWrapper child in children)
                parts.Add(child.UID);
            Node[] entries = graph.allNodes.Where(node => start
                ? node is FlowCanvas.Nodes.StartEvent : node is FlowCanvas.Nodes.UpdateEvent).ToArray();
            parts.Add((entries.Length).ToString(CultureInfo.InvariantCulture));
            foreach (Node entry in entries)
                parts.Add(entry.UID);
        }

        static bool HasLifecycle(Graph graph, bool start) => graph.allNodes.Any(node =>
            (start ? node is FlowCanvas.Nodes.StartEvent : node is FlowCanvas.Nodes.UpdateEvent) ||
            node is FlowCanvas.Macros.MacroNodeWrapper macro && HasLifecycle(macro.macro, start));

        static void Canonicalize(fsData data)
        {
            if (data.IsDictionary)
            {
                var fields = data.AsDictionary.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
                data.AsDictionary.Clear();
                foreach (var field in fields)
                {
                    Canonicalize(field.Value);
                    data.AsDictionary.Add(field.Key, field.Value);
                }
            }
            else if (data.IsList)
                foreach (fsData item in data.AsList)
                    Canonicalize(item);
        }

        static void ExcludeEditorData(object instance, fsData data)
        {
            if (instance is not GraphSource && instance is not Node && instance is not Connection)
                return;
            fsMetaType metadata = fsMetaType.Get(instance.GetType());
            string[] properties = s_EditorProperties.GetValue(metadata, s_CollectEditorProperties);
            for (int i = 0; i < properties.Length; i++)
                data.AsDictionary.Remove(properties[i]);
        }

        static string[] CollectEditorProperties(fsMetaType metadata)
        {
            var properties = new List<string>();
            foreach (fsMetaProperty property in metadata.Properties)
                if (property.Field.IsDefined(typeof(fsIgnoreInBuildAttribute), true))
                    properties.Add(property.JsonName);
            return properties.ToArray();
        }

        static void AppendSerializedSource(List<string> parts, UnityEngine.Object asset)
        {
            using var serialized = new SerializedObject(asset);
            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                parts.Add(property.propertyPath);
                parts.Add(((int)property.propertyType).ToString(CultureInfo.InvariantCulture));
                enterChildren = false;
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Generic:
                        parts.Add(property.type);
                        enterChildren = true;
                        break;
                    case SerializedPropertyType.ManagedReference:
                        parts.Add(property.managedReferenceFullTypename);
                        enterChildren = true;
                        break;
                    case SerializedPropertyType.ObjectReference:
                        AppendReference(parts, property.objectReferenceValue);
                        break;
                    default:
                        parts.Add(property.contentHash.ToString());
                        break;
                }
            }
        }

        protected static void AppendReference(List<string> parts, UnityEngine.Object reference)
        {
            parts.Add(reference != null ? "1" : "0");
            if (reference == null)
                return;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string guid, out long fileId))
                throw new InvalidOperationException($"图引用'{reference.name}'不是正式资产。");
            parts.Add(guid);
            parts.Add((fileId).ToString(CultureInfo.InvariantCulture));
        }
        static GraphSource Pack(GraphSource source, Graph graph)
        {
            source.Pack(graph);
            Connection[] connections = source.connections.OrderBy(edge => edge.sourceNode.UID, StringComparer.Ordinal)
                .ThenBy(edge => edge is BinderConnection binder && binder.sourcePort is ValueOutput ? 1 : 0)
                .ThenBy(edge => edge is BinderConnection binder && binder.sourcePort is ValueOutput
                    ? 0 : edge.sourceNode.outConnections.IndexOf(edge))
                .ThenBy(edge => edge is BinderConnection binder ? binder.sourcePortID : "", StringComparer.Ordinal)
                .ThenBy(edge => edge.targetNode.UID, StringComparer.Ordinal)
                .ThenBy(edge => edge is BinderConnection binder ? binder.targetPortID : "", StringComparer.Ordinal)
                .ThenBy(edge => edge.UID, StringComparer.Ordinal).ToArray();
            source.connections.Clear();
            source.connections.AddRange(connections);
            return source;
        }

        public GraphExecutionIdentity CreateIdentity(Graph graph, string graphId, string encodingContract, string numericTarget)
        {
            string revision = Compute(graph);
            var dependencies = new Dictionary<string, GraphExecutionDependency>(StringComparer.Ordinal);
            Collect(graph);
            return new GraphExecutionIdentity(graphId, revision, encodingContract,
                GraphNodeSemanticContract.Identity, numericTarget,
                dependencies.Values.OrderBy(value => value.Identity, StringComparer.Ordinal).ToArray());

            void Collect(Graph current)
            {
                foreach (UnityEngine.Object reference in m_GraphSources[current].References)
                {
                    if (reference == null)
                        continue;
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string guid, out long file);
                    string id = guid + ":" + file.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (dependencies.ContainsKey(id))
                        continue;
                    dependencies.Add(id, new GraphExecutionDependency(id, reference is Graph child
                        ? Compute(child) : ComputeDependencies(AssetDatabase.GetAssetPath(reference))));
                    if (reference is Graph nested)
                        Collect(nested);
                }
            }
        }

    }
}
#endif
