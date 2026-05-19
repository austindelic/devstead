using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Devstead.Rendering;
using Oceana;

namespace Devstead.Editor.Rendering
{
    public static partial class SkyAndCloudsProjectSetup
    {
        private static bool SetObjectReference(SerializedObject serializedObject, string propertyName, Object value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue == value)
            {
                return false;
            }

            property.objectReferenceValue = value;
            return true;
        }

        private static bool SetEnum(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.enumValueIndex == value)
            {
                return false;
            }

            property.enumValueIndex = value;
            return true;
        }

        private static bool SetBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.boolValue == value)
            {
                return false;
            }

            property.boolValue = value;
            return true;
        }

        private static bool SetFloat(SerializedObject serializedObject, string propertyName, float value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || Mathf.Approximately(property.floatValue, value))
            {
                return false;
            }

            property.floatValue = value;
            return true;
        }

        private static bool SetInt(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.intValue == value)
            {
                return false;
            }

            property.intValue = value;
            return true;
        }

        private static bool SetVector3(SerializedObject serializedObject, string propertyName, Vector3 value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.vector3Value == value)
            {
                return false;
            }

            property.vector3Value = value;
            return true;
        }

        private static bool SetActive(VolumeComponent component, bool active)
        {
            if (component.active == active)
            {
                return false;
            }

            component.active = active;
            return true;
        }

        private static bool SetParameter<T>(VolumeParameter<T> parameter, T value, bool overrideState)
        {
            var changed = false;

            if (!EqualityComparer<T>.Default.Equals(parameter.value, value))
            {
                parameter.value = value;
                changed = true;
            }

            if (parameter.overrideState != overrideState)
            {
                parameter.overrideState = overrideState;
                changed = true;
            }

            return changed;
        }

        private static bool SetVolumeParameter(SerializedObject serializedObject, string propertyName, int value, bool overrideState)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return false;
            }

            var changed = false;
            changed |= SetSerializedBool(property.FindPropertyRelative("m_OverrideState"), overrideState);
            changed |= SetSerializedInt(property.FindPropertyRelative("m_Value"), value);
            return changed;
        }

        private static bool SetVolumeParameter(SerializedObject serializedObject, string propertyName, bool value, bool overrideState)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return false;
            }

            var changed = false;
            changed |= SetSerializedBool(property.FindPropertyRelative("m_OverrideState"), overrideState);
            changed |= SetSerializedBool(property.FindPropertyRelative("m_Value"), value);
            return changed;
        }

        private static bool SetVolumeParameter(SerializedObject serializedObject, string propertyName, float value, bool overrideState)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return false;
            }

            var changed = false;
            changed |= SetSerializedBool(property.FindPropertyRelative("m_OverrideState"), overrideState);
            changed |= SetSerializedFloat(property.FindPropertyRelative("m_Value"), value);
            return changed;
        }

        private static bool SetSerializedBool(SerializedProperty property, bool value)
        {
            if (property == null || property.boolValue == value)
            {
                return false;
            }

            property.boolValue = value;
            return true;
        }

        private static bool SetSerializedInt(SerializedProperty property, int value)
        {
            if (property == null || property.intValue == value)
            {
                return false;
            }

            property.intValue = value;
            return true;
        }

        private static bool SetSerializedFloat(SerializedProperty property, float value)
        {
            if (property == null || Mathf.Approximately(property.floatValue, value))
            {
                return false;
            }

            property.floatValue = value;
            return true;
        }
    }
}
