using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BTSMTL.Graphs
{
    // 子图的宿主准入声明：子图内允许哪些节点由将要调用它的宿主图决定，编译时再由实际调用方复核。
    [Serializable]
    public abstract class BtsmtlMacroHost
    {
        public abstract string DisplayName { get; }
        // 准入声明的稳定内容，进入调用方编译身份。
        public abstract string Identity { get; }
        public abstract bool CanAuthorNodeType(Type nodeType);
        // 私有子图在所属图中创建时取所属图的准入；各子图持有独立副本。
        public abstract BtsmtlMacroHost Clone();
    }

    // 能在自身中创建私有子图的图提供其准入声明。
    public interface IBtsmtlMacroHostProvider
    {
        BtsmtlMacroHost CreatePrivateMacroHost();
    }

    // 通用图宿主：与 BtsmtlGraphRoot 共用节点准入，领域集合必须是调用方图领域的子集。
    [Serializable]
    public sealed class BtsmtlGraphMacroHost : BtsmtlMacroHost
    {
        [SerializeField] string[] m_Domains = Array.Empty<string>();

        BtsmtlGraphMacroHost() { }

        public BtsmtlGraphMacroHost(IEnumerable<string> domains)
        {
            string[] values = domains?.ToArray() ?? throw new ArgumentNullException(nameof(domains));
            if (values.Any(string.IsNullOrWhiteSpace) ||
                values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new ArgumentException("子图宿主领域必须是不重复的稳定身份。", nameof(domains));
            m_Domains = values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyList<string> Domains => m_Domains;
        public override string DisplayName => m_Domains.Length == 0
            ? "通用图" : "通用图（" + string.Join("、", m_Domains) + "）";
        public override string Identity => "graph:" + string.Join(",", m_Domains);
        public override bool CanAuthorNodeType(Type nodeType) => BtsmtlGraphRoot.AllowsNodeType(nodeType, m_Domains);
        public override BtsmtlMacroHost Clone() => new BtsmtlGraphMacroHost(m_Domains);
    }
}
