#if UNITY_5_3_OR_NEWER
using System;
using System.IO;
using System.Linq;

namespace BTSMTL.Authoring.Graph
{
    public static class UnityGraphAuthoringResourceTypes
    {
        public static Type[] Resolve(string objectType, GraphAuthoringReferenceSource source)
        {
            if (string.IsNullOrWhiteSpace(objectType))
                throw new InvalidDataException("正式资源字段没有声明允许的对象类型。");
            return objectType.Split('|').Select(name =>
            {
                Type[] matches = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name, false))
                    .Where(type => type != null).Distinct().ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException($"正式资源字段类型'{name}'没有唯一实际类型（{matches.Length}个），请检查该字段声明。");
                Type result = matches[0];
                if (source == GraphAuthoringReferenceSource.AdapterResource && !typeof(UnityEngine.Object).IsAssignableFrom(result))
                    throw new InvalidDataException($"Unity资源字段类型'{name}'必须是实际Unity对象类型。");
                return result;
            }).ToArray();
        }
    }
}
#endif
