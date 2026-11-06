using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using Newtonsoft.Json.Linq;
using NodeCanvas.Framework;
using NodeCanvas.Framework.Internal;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Graphs
{
    public interface IBtsmtlGraphVariableOwner
    {
        UnityEngine.Object OwnerAsset { get; }
        BtsmtlGraphVariableDeclarations VariableDeclarations { get; }
        BtsmtlGraphVariableEditor VariableEditor { get; }
    }

    public sealed class BtsmtlGraphVariableEditor
    {
        readonly Action<string, Action> m_Mutate;
        readonly BlackboardSource m_Blackboard = new();
        BtsmtlGraphVariableDeclarations m_Source;
        int m_EditDepth;

        public BtsmtlGraphVariableEditor(BtsmtlGraphVariableDeclarations source, UnityEngine.Object owner,
            Action<string, Action> mutate)
        {
            m_Mutate = mutate;
            m_Blackboard.unityContextObject = owner;
            Restore(source);
        }

        public BtsmtlGraphVariableDeclarations Source => m_Source;
        public IBlackboard Blackboard => m_Blackboard;

        public void Restore(BtsmtlGraphVariableDeclarations source)
        {
            m_Source = source;
            m_Blackboard.variables = source.Variables.ToDictionary(variable => variable.Name, variable =>
            {
                Type type = BtsmtlGraphVariableUnity.Type(variable.ValueType);
                var projection = (Variable)Activator.CreateInstance(typeof(Variable<>).MakeGenericType(type), variable.Name, variable.VariableId);
                projection.value = BtsmtlGraphVariableUnity.DefaultValue(variable);
                projection.isExposedPublic = variable.IsPublic;
                return projection;
            }, StringComparer.Ordinal);
        }

        public void Edit(string title, Action mutation)
        {
            if (m_EditDepth != 0)
            {
                mutation();
                return;
            }
            m_Mutate(title, () =>
            {
                m_EditDepth++;
                try
                {
                    mutation();
                    Capture();
                }
                finally { m_EditDepth--; }
            });
        }

        void Capture()
        {
            m_Blackboard.variables = m_Blackboard.variables.Values.ToDictionary(variable => variable.name,
                variable => variable, StringComparer.Ordinal);
            m_Source.Replace(m_Blackboard.variables.Values.Select(variable =>
            {
                if (!variable.hasStableIdentity || variable.isDataBound || variable.isPropertyBound || variable is not ISerializedVariableValue)
                    throw new InvalidOperationException("图变量编辑必须提交具有稳定身份的本地字面量声明。");
                BtsmtlGraphVariableDeclaration stored = m_Source.RequireVariable(variable.ID);
                return new BtsmtlGraphVariableDeclaration(stored.Declaration, variable.name,
                    BtsmtlGraphVariableUnity.TypeId(variable.varType), BtsmtlGraphVariableUnity.DefaultJson(variable.varType,
                        ((ISerializedVariableValue)variable).serializedValue), variable.isExposedPublic, stored.DeclarationOrder);
            }).ToArray());
        }

        public Variable<T> Declare<T>(string id, string name, T defaultValue)
        {
            Variable<T> created = null;
            Edit("声明图变量", () =>
            {
                var declaration = new BtsmtlBlackboardDeclaration(id, PipelineBlackboardVariableScope.Graph,
                    PipelineBlackboardVariableLifetime.GraphInstance);
                int order = m_Source.Variables.Count == 0 ? 0 : m_Source.Variables.Max(variable => variable.DeclarationOrder) + 1;
                m_Source.Replace(m_Source.Variables.Append(new BtsmtlGraphVariableDeclaration(declaration, name,
                    BtsmtlGraphVariableUnity.TypeId(typeof(T)), BtsmtlGraphVariableUnity.DefaultJson(typeof(T), defaultValue), false, order)).ToArray());
                created = new Variable<T>(name, id) { value = defaultValue };
                m_Blackboard.variables.Add(name, created);
                m_Blackboard.TryInvokeOnVariableAdded(created);
            });
            return created;
        }

        public void ConfigureVariable(string id, bool isPublic) => Edit("配置图变量发布", () =>
            m_Blackboard.variables.Values.Single(variable => variable.ID == id).isExposedPublic = isPublic);

        public void ConfigureBlackboard(string id, PipelineBlackboardVariableScope scope,
            PipelineBlackboardVariableLifetime lifetime, string category = "", PipelineBlackboardInputBinding inputBinding = null) =>
            Edit("配置图变量黑板语义", () => m_Source.ConfigureBlackboard(id, scope, lifetime, category, inputBinding));

        public void Replace(IReadOnlyList<Variable> variables) => Edit("编辑图变量列表", () =>
            m_Blackboard.variables = variables.ToDictionary(variable => variable.name, variable => variable, StringComparer.Ordinal));
    }

    public static class BtsmtlGraphVariableUnity
    {
        public static string TypeId(Type type) => CompileValueTypes.Id(type.IsEnum
            ? CompileValueTypes.Enum(type.AssemblyQualifiedName) : CompileValueTypes.FromPlatformName(type.FullName));

        public static Type Type(CompileValueType type) => type.Kind switch
        {
            SemanticValueKind.Boolean => typeof(bool), SemanticValueKind.Int32 => typeof(int),
            SemanticValueKind.Number => type.FullName == "System.Single" ? typeof(float) : typeof(double),
            SemanticValueKind.UInt64 => type.FullName == "System.UInt32" ? typeof(uint) : typeof(ulong),
            SemanticValueKind.Identity => typeof(string), SemanticValueKind.ObjectHandle => typeof(ObjectHandle),
            SemanticValueKind.Vector2 => typeof(Vector2), SemanticValueKind.Vector3 => typeof(Vector3),
            SemanticValueKind.Quaternion => typeof(Quaternion), SemanticValueKind.Enum => System.Type.GetType(type.EnumIdentity, true),
            _ => throw new InvalidOperationException($"图变量类型 '{type.FullName}' 没有引擎编辑投影。")
        };

        public static JToken DefaultJson(Type type, object value)
        {
            if (type.IsEnum) return new JValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));
            if (value is Vector2 vector2) return new JObject { ["x"] = vector2.x, ["y"] = vector2.y };
            if (value is Vector3 vector3) return new JObject { ["x"] = vector3.x, ["y"] = vector3.y, ["z"] = vector3.z };
            if (value is Quaternion rotation) return new JObject { ["x"] = rotation.x, ["y"] = rotation.y, ["z"] = rotation.z, ["w"] = rotation.w };
            if (value is ObjectHandle handle) return new JValue(handle.Value.ToString(CultureInfo.InvariantCulture));
            return value == null ? JValue.CreateNull() : new JValue(value);
        }

        public static object DefaultValue(BtsmtlGraphVariableDeclaration variable)
        {
            CompileValueType type = variable.ValueType;
            JToken value = variable.DefaultValue;
            return type.Kind switch
            {
                SemanticValueKind.Vector2 => new Vector2((float)value["x"], (float)value["y"]),
                SemanticValueKind.Vector3 => new Vector3((float)value["x"], (float)value["y"], (float)value["z"]),
                SemanticValueKind.Quaternion => new Quaternion((float)value["x"], (float)value["y"], (float)value["z"], (float)value["w"]),
                SemanticValueKind.Enum => Enum.ToObject(Type(type), (int)value),
                _ => BtsmtlGraphVariableDeclarationsJson.ReadDefault(type, value)
            };
        }

        public static GraphVariableContract CreateContract(this BtsmtlGraphVariableDeclarations source) =>
            new(source.OwnerId, source.Revision, source.Variables.Select(variable => new GraphVariableDescriptor(
                new BtsmtlGraphVariableReference(source.OwnerId, variable.VariableId), variable.Name, Type(variable.ValueType),
                GraphHostValue.FromObject(DefaultValue(variable)), variable.IsPublic)).ToArray());
    }
}
