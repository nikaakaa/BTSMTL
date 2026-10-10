using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(ObjectFieldAttribute), true)]
public class ObjectFieldDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var attribute = fieldInfo.GetCustomAttribute<ObjectFieldAttribute>();

        ObjectField propertyField = new ObjectField();
        if (string.IsNullOrEmpty(attribute.BindPath))
        {
            propertyField.BindProperty(property);
        }
        else
        {

        }


        var method = property.serializedObject.targetObject.GetType().GetMethod(attribute.OnValueChangedCallback, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (method != null)
        {
            propertyField.RegisterValueChangedCallback((i) =>
            {
                method.Invoke(property.serializedObject.targetObject, new object[] { i.newValue });
            });
        }
        return propertyField;
    }
}
