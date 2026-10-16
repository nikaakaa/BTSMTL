using ThirdPersonSimulation;
using System;
using FlowCanvas;
using FlowCanvas.Nodes;
using ParadoxNotion.Design;
using UnityEngine;

namespace BTSMTL.EventGraphs
{
    [EventGraphCapability(EventGraphCapabilityKind.HostInput)]
    public abstract class EventGraphHostInputNode<T> : FlowScriptNode, EventGraphHostInputNodeMarker
    {
        [SerializeField] string m_InputId = string.Empty;

        public string InputId => m_InputId ?? string.Empty;

        public Type ValueType => typeof(T);

        public override string name =>
            string.IsNullOrEmpty(InputId)
                ? base.name
                : ((HostEventGraph)graph).GetHostInputDisplayName(InputId);

        protected override void RegisterPorts()
        {
            AddValueOutput<T>("Value", ReadValue);
        }

        public void ConfigureInput(string inputId)
        {
            m_InputId = inputId;
        }


        T ReadValue() => throw new InvalidOperationException("宿主输入只在 BTSMTL 图执行期间读取。");
    }

    [Name("Read Host Float")]
    [Category("Host/Input")]
    [Description("Reads a declared host float input.")]
    public sealed class EventGraphFloatInputNode : EventGraphHostInputNode<float>
    {
    }

    [Name("Read Host Int")]
    [Category("Host/Input")]
    [Description("Reads a declared host integer input.")]
    public sealed class EventGraphIntInputNode : EventGraphHostInputNode<int>
    {
    }

    [Name("Read Host Bool")]
    [Category("Host/Input")]
    [Description("Reads a declared host boolean input.")]
    public sealed class EventGraphBoolInputNode : EventGraphHostInputNode<bool>
    {
    }

    [Name("Read Host Vector2")]
    [Category("Host/Input")]
    [Description("Reads a declared host Vector2 input.")]
    public sealed class EventGraphVector2InputNode : EventGraphHostInputNode<Vector2>
    {
    }

    [Name("Read Host Vector3")]
    [Category("Host/Input")]
    [Description("Reads a declared host Vector3 input.")]
    public sealed class EventGraphVector3InputNode : EventGraphHostInputNode<Vector3>
    {
    }

    [Name("Read Host Quaternion")]
    [Category("Host/Input")]
    public sealed class EventGraphQuaternionInputNode : EventGraphHostInputNode<Quaternion>
    {
    }

    [Name("Read Delta Seconds")]
    [Category("Host/Input")]
    [Description("Reads the delta seconds supplied by the host.")]
    [EventGraphCapability(EventGraphCapabilityKind.HostInput)]
    public sealed class EventGraphDeltaNode : FlowScriptNode, EventGraphHostInputNodeMarker
    {

        protected override void RegisterPorts()
        {
            AddValueOutput<float>("Delta", () =>
                throw new InvalidOperationException("宿主 delta 只在 BTSMTL 图执行期间读取。"));
        }

        public string InputId => EventGraphHostInputIds.DeltaSeconds;

        public Type ValueType => typeof(float);

        public void ConfigureInput(string inputId)
        {
            if (!string.Equals(inputId, InputId, StringComparison.Ordinal))
                throw new InvalidOperationException("Event graph delta input has a fixed identity.");
        }

    }

    public static class EventGraphHostInputIds
    {
        public const string DeltaSeconds = "host.delta-seconds";
    }

    [Name("Read Host Text")]
    [Category("Host/Input")]
    public sealed class EventGraphTextInputNode : EventGraphHostInputNode<string> { }

}
