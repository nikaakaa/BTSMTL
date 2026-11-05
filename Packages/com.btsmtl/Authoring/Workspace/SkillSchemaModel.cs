#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace BTSMTL.Authoring.Workspace
{

public sealed class SkillSchemaTags
{
    readonly List<KeyValuePair<string, string>> m_Items;

    SkillSchemaTags(List<KeyValuePair<string, string>> items) => m_Items = items;

    public IReadOnlyList<KeyValuePair<string, string>> Items => m_Items;
    public string? this[string key] => m_Items.FirstOrDefault(item => item.Key == key).Value;
    public bool Has(string key) => m_Items.Any(item => item.Key == key);
    public bool True(string key) => this[key] == "true";

    public static SkillSchemaTags Parse(string? text)
    {
        var items = new List<KeyValuePair<string, string>>();
        foreach (string tag in (text ?? "").Split('#', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = tag.IndexOf('=');
            items.Add(separator < 0 ? new(tag, "") : new(tag[..separator], tag[(separator + 1)..]));
        }
        return new SkillSchemaTags(items);
    }
}

// var 类型：容器、元素类型名、可空与 #ref 类型标签。
public sealed class SkillSchemaType
{
    public SkillSchemaType(string container, string element, bool nullable, string? reference)
    { Container = container; Element = element; Nullable = nullable; Reference = reference; }
    public string Container { get; }
    public string Element { get; }
    public bool Nullable { get; }
    public string? Reference { get; }
}

public sealed class SkillSchemaVar
{
    internal SkillSchemaVar(string name, SkillSchemaType type, string group, string? alias,
        SkillSchemaTags tags, XElement element, SkillSchemaBean bean)
    { Name = name; Type = type; Group = group; Alias = alias; Tags = tags; Element = element; Bean = bean; }
    public string Name { get; }
    public SkillSchemaType Type { get; }
    public string Group { get; }
    public string? Alias { get; }
    public SkillSchemaTags Tags { get; }
    public XElement Element { get; }
    public SkillSchemaBean Bean { get; }
}

public sealed class SkillSchemaBean
{
    internal SkillSchemaBean(string name, string module, string? parentName, string comment,
        string? alias, bool isValueType, SkillSchemaTags tags, string file, XElement element)
    { Name = name; Module = module; ParentName = parentName; Comment = comment; Alias = alias;
      IsValueType = isValueType; Tags = tags; File = file; Element = element; }
    public string Name { get; }
    public string Module { get; }
    public string? ParentName { get; }
    public string Comment { get; }
    public string? Alias { get; }
    public bool IsValueType { get; }
    public SkillSchemaTags Tags { get; }
    public string File { get; }
    public XElement Element { get; }
    public List<SkillSchemaVar> Vars { get; } = new();
    public List<SkillSchemaBean> Children { get; } = new();
    public SkillSchemaBean? Parent { get; set; }
    public string FullName => Module + "." + Name;
    public string Path => Module["Btsmtl.Skill".Length..].TrimStart('.') is { Length: > 0 } nested ? nested + "." + Name : Name;

    public IEnumerable<SkillSchemaBean> AncestorsAndSelf()
    {
        for (SkillSchemaBean? bean = this; bean != null; bean = bean.Parent)
            yield return bean;
    }

    public IEnumerable<SkillSchemaBean> Descendants() => Children.SelectMany(child => child.Descendants().Prepend(child));
}

public sealed class SkillSchemaModel
{
    static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "displayName", "port", "compilationImpact", "kind", "category", "default", "finite", "operation", "optional",
        "minimum", "unit", "reference", "hidden", "readOnly", "id", "defaultField", "allowedValues", "timelineDomains",
        "nonEmpty", "inline", "asset", "variant", "maximum", "resource", "pickerOwner", "picker", "encoding",
        "output", "portSource", "enumSchema", "authoring", "numbered", "hostInputs", "graphRole", "documentMember"
    };
    static readonly HashSet<string> Booleans = new(StringComparer.Ordinal)
    {
        "finite", "optional", "hidden", "readOnly", "nonEmpty", "inline", "authoring", "numbered"
    };
    static readonly HashSet<string> Builtins = new(StringComparer.Ordinal) { "bool", "int", "long", "float", "double", "string" };

    readonly Dictionary<string, SkillSchemaBean> m_Beans = new(StringComparer.Ordinal);
    readonly HashSet<string> m_Enums = new(StringComparer.Ordinal);

    public List<SkillSchemaBean> Beans { get; } = new();

    public static SkillSchemaModel Load(IReadOnlyDictionary<string, XDocument> schemas, ICollection<SkillSchemaError> errors)
    {
        var model = new SkillSchemaModel();
        foreach (var schema in schemas)
            model.Read(schema.Key, schema.Value.Root!, schema.Value.Root!.Attribute("name")!.Value, errors);
        foreach (SkillSchemaBean bean in model.Beans.Where(bean => bean.ParentName != null))
        {
            bean.Parent = model.FindBean(bean.ParentName!, bean.Module);
            if (bean.Parent == null)
                errors.Add(Error(bean.File, bean.Element, ".parent", $"父类型 {bean.ParentName} 不存在。"));
            else
                bean.Parent.Children.Add(bean);
        }
        return model;
    }

    public SkillSchemaBean? FindBean(string name, string module)
    {
        for (string? scope = module; scope != null; scope = scope.Contains('.') ? scope[..scope.LastIndexOf('.')] : null)
            if (m_Beans.TryGetValue(scope + "." + name, out SkillSchemaBean? bean))
                return bean;
        return m_Beans.GetValueOrDefault(name);
    }

    public bool IsSchemaEnum(string name, string module)
    {
        for (string? scope = module; scope != null; scope = scope.Contains('.') ? scope[..scope.LastIndexOf('.')] : null)
            if (m_Enums.Contains(scope + "." + name))
                return true;
        return m_Enums.Contains(name);
    }

    public static bool IsBuiltin(string type) => Builtins.Contains(type);

    // 引用到 schema 外的类型名：只能是从运行合同导出的枚举。
    public IEnumerable<string> ExternalTypes() => Beans.SelectMany(bean => bean.Vars.Select(item => (bean, item)))
        .Where(pair => !IsBuiltin(pair.item.Type.Element) && FindBean(pair.item.Type.Element, pair.bean.Module) == null &&
            !IsSchemaEnum(pair.item.Type.Element, pair.bean.Module))
        .Select(pair => pair.item.Type.Element).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal);

    void Read(string file, XElement module, string name, ICollection<SkillSchemaError> errors)
    {
        foreach (XElement element in module.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "module":
                    Read(file, element, name + "." + element.Attribute("name")!.Value, errors);
                    break;
                case "enum":
                    m_Enums.Add(name + "." + element.Attribute("name")!.Value);
                    break;
                case "bean":
                    var bean = new SkillSchemaBean(element.Attribute("name")!.Value, name,
                        (string?)element.Attribute("parent"), (string?)element.Attribute("comment") ?? "",
                        (string?)element.Attribute("alias"), (string?)element.Attribute("valueType") is "1" or "true",
                        Validate(file, element, errors), file, element);
                    foreach (XElement item in element.Elements("var"))
                        bean.Vars.Add(new SkillSchemaVar(item.Attribute("name")!.Value,
                            ParseType(item.Attribute("type")!.Value), (string?)item.Attribute("group") ?? "data",
                            (string?)item.Attribute("alias"), Validate(file, item, errors), item, bean));
                    if (!m_Beans.TryAdd(bean.FullName, bean))
                        errors.Add(Error(file, element, "", $"bean {bean.FullName} 重复声明。"));
                    Beans.Add(bean);
                    break;
            }
        }
    }

    static SkillSchemaType ParseType(string text)
    {
        string container = "";
        string element = text;
        if (text.StartsWith('('))
        {
            int end = text.IndexOf(')');
            container = text[1..end].Split('#')[0];
            element = text[(end + 2)..];
        }
        else if (text.Contains(','))
        {
            container = text[..text.IndexOf(',')];
            element = text[(text.IndexOf(',') + 1)..];
        }
        string? reference = null;
        int tag = element.IndexOf('#');
        if (tag >= 0)
        {
            SkillSchemaTags tags = SkillSchemaTags.Parse(element[(tag + 1)..]);
            reference = tags["ref"];
            element = element[..tag];
        }
        bool nullable = element.EndsWith('?');
        return new SkillSchemaType(container, nullable ? element[..^1] : element, nullable, reference);
    }

    static SkillSchemaTags Validate(string file, XElement declaration, ICollection<SkillSchemaError> errors)
    {
        SkillSchemaTags tags = SkillSchemaTags.Parse((string?)declaration.Attribute("tags"));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags.Items)
        {
            string? reason = null;
            if (!keys.Add(tag.Key))
                reason = "同一附加说明键重复声明。";
            else if (!Allowed.Contains(tag.Key))
                reason = tag.Key.StartsWith("visibleWhen", StringComparison.Ordinal)
                    ? "字段联动必须通过多态子类型表达，不能声明可见性条件。"
                    : "白名单只接受跨语言数据语义；目标语言类型、成员、getter、布局及生成规则归代码目标。";
            else if (Booleans.Contains(tag.Key) && tag.Value is not ("true" or "false"))
                reason = "布尔附加说明必须为 true 或 false。";
            else if (tag.Key is "finite" or "nonEmpty" or "hidden" or "readOnly" or "inline" or "numbered" && tag.Value == "false" ||
                tag.Key == "authoring" && tag.Value == "true")
                reason = "与默认行为相同，删除重复声明。";
            else if (tag.Key == "optional" && declaration.Name.LocalName == "var" &&
                (tag.Value == "true") == ((string)declaration.Attribute("type")!).Split('#')[0].EndsWith('?'))
                reason = "作者可缺值规则与原生可空相同，删除重复声明。";
            if (reason != null)
                errors.Add(Error(file, declaration, ".tags." + tag.Key, reason));
        }
        return tags;
    }

    public static SkillSchemaError Error(string file, XElement declaration, string suffix, string message)
    {
        string path = "$" + string.Concat(declaration.AncestorsAndSelf().Reverse()
            .Where(element => element.Attribute("name") != null)
            .Select(element => "." + (string)element.Attribute("name")!));
        int line = ((IXmlLineInfo)declaration).LineNumber;
        return new SkillSchemaError(file, path + suffix, "skill-schema-target", message + " 声明行：" + line + "。");
    }
}

}
