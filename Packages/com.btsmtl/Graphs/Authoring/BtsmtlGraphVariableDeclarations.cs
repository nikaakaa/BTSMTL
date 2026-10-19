using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using NodeCanvas.Framework;
using NodeCanvas.Framework.Internal;
using ParadoxNotion.Serialization;
using UnityEngine;

namespace BTSMTL.Graphs
{
    public interface IBtsmtlGraphVariableOwner
    {
        UnityEngine.Object OwnerAsset { get; }
        BtsmtlGraphVariableDeclarations VariableDeclarations { get; }
    }

    [Serializable]
    public sealed class BtsmtlGraphVariableDeclarations : ISerializationCallbackReceiver
    {
        [SerializeField] string m_OwnerId = Guid.NewGuid().ToString("N");
        [SerializeField] string m_Revision = "1";
        [SerializeField] string m_SerializedBlackboard = string.Empty;
        [SerializeField] List<UnityEngine.Object> m_ObjectReferences = new();
        [SerializeField] List<BtsmtlBlackboardDeclaration> m_BlackboardDeclarations = new();
        [NonSerialized] BlackboardSource m_Blackboard = new();
        [NonSerialized] UnityEngine.Object m_Owner;
        [NonSerialized] int m_EditDepth;

        public string OwnerId => m_OwnerId;
        public string Revision => m_Revision;
        public IBlackboard Blackboard => m_Blackboard;
        public IReadOnlyList<BtsmtlBlackboardDeclaration> BlackboardDeclarations => m_BlackboardDeclarations;

        public BtsmtlBlackboardDeclaration RequireDeclaration(string variableId) =>
            m_BlackboardDeclarations.SingleOrDefault(declaration => declaration.VariableId == variableId) ??
            throw new InvalidOperationException($"图变量 '{variableId}' 缺少黑板声明。");

        public void Validate()
        {
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (BtsmtlBlackboardDeclaration declaration in m_BlackboardDeclarations)
            {
                Variable variable = m_Blackboard.variables.Values.SingleOrDefault(value => value.ID == declaration?.VariableId);
                if (declaration == null || !declared.Add(declaration.VariableId) || variable == null)
                    throw new InvalidOperationException($"'{OwnerId}' 的黑板声明重复或没有对应变量。");
                declaration.Validate(variable);
            }
            if (declared.Count != m_Blackboard.variables.Count)
                throw new InvalidOperationException($"'{OwnerId}' 有变量缺少黑板声明。");
        }

        public void BindOwner(UnityEngine.Object owner)
        {
            m_Owner = owner;
            m_Blackboard.unityContextObject = owner;
        }

        public GraphVariableContract CreateContract()
        {
            var descriptors = new List<GraphVariableDescriptor>(m_Blackboard.variables.Count);
            foreach (Variable variable in m_Blackboard.variables.Values)
            {
                if (!variable.hasStableIdentity || variable.isDataBound || variable.isPropertyBound)
                    throw new InvalidOperationException($"变量 '{variable.name}' 必须是具有稳定身份的本地数据声明。");
                descriptors.Add(new GraphVariableDescriptor(
                    new BtsmtlGraphVariableReference(OwnerId, variable.ID), variable.name,
                    variable.varType, GraphHostValue.FromObject(variable.value), variable.isExposedPublic));
            }
            return new GraphVariableContract(OwnerId, Revision, descriptors);
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() => Serialize();

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            m_Blackboard = m_SerializedBlackboard.Length == 0
                ? new BlackboardSource()
                : JSONSerializer.Deserialize<BlackboardSource>(m_SerializedBlackboard, m_ObjectReferences);
            m_Blackboard.unityContextObject = m_Owner;
        }

        void Serialize()
        {
            m_ObjectReferences.Clear();
            m_SerializedBlackboard = JSONSerializer.Serialize(typeof(BlackboardSource), m_Blackboard, m_ObjectReferences);
        }

#if UNITY_EDITOR
        public void ConfigureIdentity(string id, string revision)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(revision))
                throw new ArgumentException("变量声明必须指定所属身份与修订。");
            Edit("配置图变量身份", () =>
            {
                m_OwnerId = id;
                m_Revision = revision;
            }, false);
        }

        public Variable<T> Declare<T>(string id, string name, T defaultValue)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("图变量必须指定稳定身份与名称。");
            GraphHostValue.FromObject(defaultValue);
            Variable<T> variable = null;
            Edit("声明图变量", () =>
            {
                if (m_Blackboard.variables.Values.Any(value => value.ID == id))
                    throw new InvalidOperationException($"变量身份 '{id}' 已存在。");
                variable = new Variable<T>(name, id) { value = defaultValue };
                var declaration = new BtsmtlBlackboardDeclaration(id, PipelineBlackboardVariableScope.Graph,
                    PipelineBlackboardVariableLifetime.GraphInstance);
                declaration.Validate(variable);
                m_Blackboard.variables.Add(name, variable);
                m_BlackboardDeclarations.Add(declaration);
                Blackboard.TryInvokeOnVariableAdded(variable);
            });
            return variable;
        }

        public void ConfigureVariable(string id, bool isPublic) => Edit("配置图变量发布", () =>
            m_Blackboard.variables.Values.Single(variable => variable.ID == id).isExposedPublic = isPublic);

        public void ConfigureBlackboard(string id, PipelineBlackboardVariableScope scope,
            PipelineBlackboardVariableLifetime lifetime, string category = "", PipelineBlackboardInputBinding inputBinding = null) =>
            Edit("配置图变量黑板语义", () =>
            {
                var declaration = new BtsmtlBlackboardDeclaration(id, scope, lifetime, category, inputBinding);
                declaration.Validate(m_Blackboard.variables.Values.Single(variable => variable.ID == id));
                m_BlackboardDeclarations[m_BlackboardDeclarations.FindIndex(value => value.VariableId == id)] = declaration;
            });

        public void Edit(string title, Action mutation) => Edit(title, mutation, true);

        void Edit(string title, Action mutation, bool advanceRevision)
        {
            if (m_EditDepth != 0)
            {
                mutation();
                return;
            }
            Serialize();
            UnityEditor.Undo.IncrementCurrentGroup();
            int group = UnityEditor.Undo.GetCurrentGroup();
            UnityEditor.Undo.SetCurrentGroupName(title);
            UnityEditor.Undo.RegisterCompleteObjectUndo(m_Owner, title);
            m_EditDepth++;
            try
            {
                mutation();
                m_Blackboard.variables = m_Blackboard.variables.Values.ToDictionary(
                    variable => variable.name, variable => variable, StringComparer.Ordinal);
                var ids = new HashSet<string>(m_Blackboard.variables.Values.Select(variable => variable.ID), StringComparer.Ordinal);
                m_BlackboardDeclarations.RemoveAll(declaration => !ids.Contains(declaration.VariableId));
                Validate();
                if (advanceRevision)
                    m_Revision = Guid.NewGuid().ToString("N");
                Serialize();
                UnityEditor.EditorUtility.SetDirty(m_Owner);
                UnityEditor.Undo.CollapseUndoOperations(group);
            }
            catch
            {
                UnityEditor.Undo.FlushUndoRecordObjects();
                UnityEditor.Undo.RevertAllDownToGroup(group);
                throw;
            }
            finally
            {
                m_EditDepth--;
            }
        }

        public void Replace(IReadOnlyList<Variable> variables) => Edit("编辑图变量列表", () =>
            m_Blackboard.variables = variables.ToDictionary(variable => variable.name,
                variable => variable, StringComparer.Ordinal));

        public void Prune(IEnumerable<string> ids)
        {
            var keep = new HashSet<string>(ids, StringComparer.Ordinal);
            Edit("清理图变量声明", () =>
            {
                foreach (Variable variable in m_Blackboard.variables.Values.ToArray())
                    if (!keep.Contains(variable.ID))
                        m_Blackboard.RemoveVariable(variable.name);
            });
        }
#endif
    }
}
