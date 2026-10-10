using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public static class ManagedReferenceCloneUtility
    {
        public static T Clone<T>(T source)
        {
            return (T)CloneValue(source, new Dictionary<object, object>(ReferenceComparer.Instance));
        }

        static object CloneValue(object source, Dictionary<object, object> visited)
        {
            if (source == null)
                return null;

            Type type = source.GetType();
            if (type.IsValueType || type.IsEnum || type == typeof(string))
                return source;
            if (source is UnityEngine.Object)
                return source;
            if (source is AnimationCurve curve)
            {
                AnimationCurve cloneCurve = new AnimationCurve(curve.keys)
                {
                    preWrapMode = curve.preWrapMode,
                    postWrapMode = curve.postWrapMode
                };
                return cloneCurve;
            }
            if (visited.TryGetValue(source, out object existing))
                return existing;
            if (type.IsArray)
            {
                Array sourceArray = (Array)source;
                Array cloneArray = Array.CreateInstance(type.GetElementType(), sourceArray.Length);
                visited.Add(source, cloneArray);
                for (int i = 0; i < sourceArray.Length; i++)
                    cloneArray.SetValue(CloneValue(sourceArray.GetValue(i), visited), i);
                return cloneArray;
            }
            if (source is IList sourceList)
            {
                IList cloneList = (IList)Activator.CreateInstance(type);
                visited.Add(source, cloneList);
                for (int i = 0; i < sourceList.Count; i++)
                    cloneList.Add(CloneValue(sourceList[i], visited));
                return cloneList;
            }

            object clone;
            try
            {
                clone = Activator.CreateInstance(type, true);
            }
            catch (MissingMethodException)
            {
                clone = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
            }
            visited.Add(source, clone);
            foreach (FieldInfo field in SerializableFields(type))
                field.SetValue(clone, CloneValue(field.GetValue(source), visited));
            return clone;
        }

        static IEnumerable<FieldInfo> SerializableFields(Type type)
        {
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo[] fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic || field.IsInitOnly || field.IsNotSerialized || typeof(Delegate).IsAssignableFrom(field.FieldType))
                        continue;
                    if (field.IsPublic || field.IsDefined(typeof(SerializeField), true) || field.IsDefined(typeof(SerializeReference), true))
                        yield return field;
                }
            }
        }

        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
