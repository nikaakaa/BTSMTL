using System;
using ThirdPersonSimulation;
using FlowCanvas;
using FlowCanvas.Nodes;
using ParadoxNotion.Design;
using UnityEngine;

namespace BTSMTL.Graphs
{
    [Name("Extract Planar XZ")]
    [Category("Values/Vector3")]
    public sealed class GraphVector3PlanarNode : PureFunctionNode<Vector2, Vector3>
    {
        public override Vector2 Invoke(Vector3 value) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Extract Y Component")]
    [Category("Values/Vector3")]
    public sealed class GraphVector3YNode : PureFunctionNode<float, Vector3>
    {
        public override float Invoke(Vector3 value) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Vector2 Magnitude")]
    [Category("Values/Vector2")]
    public sealed class GraphVector2MagnitudeNode : PureFunctionNode<float, Vector2>
    {
        public override float Invoke(Vector2 value) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Vector2 Normalize By Squared Magnitude")]
    [Category("Values/Vector2")]
    public sealed class GraphVector2NormalizeNode : PureFunctionNode<Vector2, Vector2, float>
    {
        public override Vector2 Invoke(Vector2 value, float minimumSquaredMagnitude) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Vector2 Subtract")]
    [Category("Values/Vector2")]
    public sealed class GraphVector2SubtractNode : PureFunctionNode<Vector2, Vector2, Vector2>
    {
        public override Vector2 Invoke(Vector2 a, Vector2 b) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Vector2 Signed Angle")]
    [Category("Values/Vector2")]
    public sealed class GraphVector2SignedAngleNode : PureFunctionNode<float, Vector2, Vector2>
    {
        public override float Invoke(Vector2 from, Vector2 to) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Vector2 Dot")]
    [Category("Values/Vector2")]
    public sealed class GraphVector2DotNode : PureFunctionNode<float, Vector2, Vector2>
    {
        public override float Invoke(Vector2 a, Vector2 b) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Vector2 Cross")]
    [Category("Values/Vector2")]
    public sealed class GraphVector2CrossNode : PureFunctionNode<float, Vector2, Vector2>
    {
        public override float Invoke(Vector2 a, Vector2 b) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Quaternion Forward")]
    [Category("Values/Quaternion")]
    public sealed class GraphQuaternionForwardNode : PureFunctionNode<Vector3, Quaternion>
    {
        public override Vector3 Invoke(Quaternion value) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }
    [Name("Text Equal")]
    [Category("Values/Text")]
    public sealed class GraphTextEqualNode : PureFunctionNode<bool, string, string>
    {
        public override bool Invoke(string a, string b) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Float Absolute")]
    [Category("Values/Float")]
    public sealed class GraphFloatAbsNode : PureFunctionNode<float, float>
    {
        public override float Invoke(float value) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Float Clamp")]
    [Category("Values/Float")]
    public sealed class GraphFloatClampNode : PureFunctionNode<float, float, float, float>
    {
        public override float Invoke(float value, float minimum, float maximum) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Float Move Towards")]
    [Category("Values/Float")]
    public sealed class GraphFloatMoveTowardsNode : PureFunctionNode<float, float, float, float>
    {
        public override float Invoke(float current, float target, float maxDelta) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

    [Name("Select Float")]
    [Category("Values/Float")]
    public sealed class GraphFloatSelectNode : PureFunctionNodeBase
    {
        protected override void OnRegisterPorts(FlowNode node)
        {
            node.AddValueInput<bool>("condition");
            node.AddValueInput<float>("whenTrue");
            node.AddValueInput<float>("whenFalse");
            node.AddValueOutput<float>("Value", () => throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。"));
        }
    }

    [Name("Quaternion Angle Axis")]
    [Category("Values/Quaternion")]
    public sealed class GraphQuaternionAngleAxisNode : PureFunctionNode<Quaternion, float, Vector3>
    {
        public override Quaternion Invoke(float angle, Vector3 axis) =>
            throw new InvalidOperationException("BTSMTL 基础值节点由共用图求值器执行。");
    }

}
