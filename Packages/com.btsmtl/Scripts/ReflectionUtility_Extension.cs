using System;
using System.Collections.Generic;
using System.Reflection;

namespace BTSMTL
{
    public static partial class ReflectionUtility
    {
		static Dictionary<Type, Dictionary<Type, Attribute[]>> s_CachedTypeAttributesMap = new Dictionary<Type, Dictionary<Type, Attribute[]>>();
		static Dictionary<Type, Dictionary<FieldInfo, Dictionary<Type, Attribute[]>>> s_CachedTypeFieldAttributesMap = new Dictionary<Type, Dictionary<FieldInfo, Dictionary<Type, Attribute[]>>>();

		/// <summary>
		/// ��ȡһ�����������
		/// </summary>
		/// <typeparam name="T">����</typeparam>
		/// <param name="target">����</param>
		/// <returns></returns>
		public static T GetAttribute<T>(this object target) where T : Attribute
		{
			T[] attributes = GetAttributes<T>(target);
			return (attributes.Length > 0) ? attributes[0] : null;
		}

		/// <summary>
		/// ��ȡһ���������
		/// </summary>
		/// <typeparam name="T">����</typeparam>
		/// <param name="targetType">��</param>
		/// <returns></returns>
		public static T GetAttribute<T>(this Type targetType) where T : Attribute
		{
			T[] attributes = GetAttributes<T>(targetType);
			return (attributes.Length > 0) ? attributes[0] : null;
		}

		/// <summary>
		/// ��ȡһ���������������
		/// </summary>
		/// <typeparam name="T">����</typeparam>
		/// <param name="target">����</param>
		/// <returns></returns>
		public static T[] GetAttributes<T>(this object target) where T : Attribute
		{
			return GetAttributes<T>(target.GetType());
		}

		/// <summary>
		/// ��ȡһ�������������
		/// </summary>
		/// <typeparam name="T">����</typeparam>
		/// <param name="targetType">��</param>
		/// <returns></returns>
		public static T[] GetAttributes<T>(this Type targetType) where T : Attribute
		{
			if (!s_CachedTypeAttributesMap.ContainsKey(targetType))
				s_CachedTypeAttributesMap.Add(targetType, new Dictionary<Type, Attribute[]>());

			Type attributeType = typeof(T);
			if (!s_CachedTypeAttributesMap[targetType].ContainsKey(attributeType))
				s_CachedTypeAttributesMap[targetType].Add(attributeType, (T[])targetType.GetCustomAttributes(attributeType, true));

			return (T[])s_CachedTypeAttributesMap[targetType][attributeType];
		}

		/// <summary>
		/// ��ȡһ���ֶε�����
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="target"></param>
		/// <param name="fieldName"></param>
		/// <returns></returns>
		public static T GetFieldAttribute<T>(this object target, string fieldName) where T : Attribute
		{
			T[] attributes = GetFieldAttributes<T>(target, fieldName);
			
			if (attributes == null || attributes.Length == 0)
				return null;
			else
				return attributes[0];
		}

		/// <summary>
		/// ��ȡһ���ֶε���������
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="target"></param>
		/// <param name="fieldName"></param>
		/// <returns></returns>
		public static T[] GetFieldAttributes<T>(this object target, string fieldName) where T : Attribute
		{
			Type targetType = target.GetType();
			if (!s_CachedTypeFieldAttributesMap.ContainsKey(targetType))
				s_CachedTypeFieldAttributesMap.Add(targetType, new Dictionary<FieldInfo, Dictionary<Type, Attribute[]>>());

			FieldInfo fieldInfo = target.GetField(fieldName);
			if (fieldInfo == null)
				return null;

			if (!s_CachedTypeFieldAttributesMap[targetType].ContainsKey(fieldInfo))
				s_CachedTypeFieldAttributesMap[targetType].Add(fieldInfo, new Dictionary<Type, Attribute[]>());

			Type attributeType = typeof(T);
			if (!s_CachedTypeFieldAttributesMap[targetType][fieldInfo].ContainsKey(attributeType))
				s_CachedTypeFieldAttributesMap[targetType][fieldInfo].Add(attributeType, (T[])fieldInfo.GetCustomAttributes(attributeType, true));

			return (T[])s_CachedTypeFieldAttributesMap[targetType][fieldInfo][attributeType];
		}
	}
}