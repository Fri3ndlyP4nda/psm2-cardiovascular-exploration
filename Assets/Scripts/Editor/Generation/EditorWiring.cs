using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Helper for assigning values to [SerializeField] private fields from
    /// editor code.
    ///
    /// The runtime scripts deliberately keep their fields private (so nothing
    /// outside a class can reach in at runtime), which means the generator
    /// cannot just assign them. SerializedObject is the supported way to do it,
    /// and it also gives a clear error when a field is renamed - a silent
    /// mis-wire would be much harder to debug than a console warning.
    ///
    /// Usage:
    ///     using (var w = new EditorWiring(myComponent))
    ///     {
    ///         w.Set("startButton", button);
    ///         w.SetInt("levelId", (int)LevelId.Level2_Brain);
    ///     }
    /// </summary>
    public sealed class EditorWiring : System.IDisposable
    {
        private readonly SerializedObject _so;
        private readonly Object _target;

        public EditorWiring(Object target)
        {
            _target = target;
            _so = new SerializedObject(target);
        }

        private SerializedProperty Find(string field)
        {
            SerializedProperty p = _so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"[EditorWiring] '{_target.GetType().Name}' has no serialized field '{field}'. " +
                                 "The scene generator and the script are out of sync.");
            }
            return p;
        }

        /// <summary>Assigns an object reference (component, GameObject or asset).</summary>
        public EditorWiring Set(string field, Object value)
        {
            SerializedProperty p = Find(field);
            if (p != null) p.objectReferenceValue = value;
            return this;
        }

        public EditorWiring SetInt(string field, int value)
        {
            SerializedProperty p = Find(field);
            if (p != null) p.intValue = value;
            return this;
        }

        public EditorWiring SetFloat(string field, float value)
        {
            SerializedProperty p = Find(field);
            if (p != null) p.floatValue = value;
            return this;
        }

        public EditorWiring SetBool(string field, bool value)
        {
            SerializedProperty p = Find(field);
            if (p != null) p.boolValue = value;
            return this;
        }

        public EditorWiring SetString(string field, string value)
        {
            SerializedProperty p = Find(field);
            if (p != null) p.stringValue = value;
            return this;
        }

        public EditorWiring SetColor(string field, Color value)
        {
            SerializedProperty p = Find(field);
            if (p != null) p.colorValue = value;
            return this;
        }

        /// <summary>Fills an array/list of object references.</summary>
        public EditorWiring SetArray(string field, IReadOnlyList<Object> values)
        {
            SerializedProperty p = Find(field);
            if (p == null) return this;

            p.arraySize = values?.Count ?? 0;
            for (int i = 0; i < p.arraySize; i++)
            {
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            return this;
        }

        /// <summary>Fills an array/list of strings.</summary>
        public EditorWiring SetStringArray(string field, IReadOnlyList<string> values)
        {
            SerializedProperty p = Find(field);
            if (p == null) return this;

            p.arraySize = values?.Count ?? 0;
            for (int i = 0; i < p.arraySize; i++)
            {
                p.GetArrayElementAtIndex(i).stringValue = values[i];
            }
            return this;
        }

        public void Dispose()
        {
            _so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
