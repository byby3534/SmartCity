#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UI;
using UnityEngine.UI;

[CustomEditor(typeof(RoundedRectImage), true)]
[CanEditMultipleObjects]
public class RoundedRectImageEditor : ImageEditor
{
    SerializedProperty cornerRadius;
    SerializedProperty cornerSegments;

    protected override void OnEnable()
    {
        base.OnEnable();
        cornerRadius = serializedObject.FindProperty("cornerRadius");
        cornerSegments = serializedObject.FindProperty("cornerSegments");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        base.OnInspectorGUI();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rounded Corners", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(cornerRadius);
        EditorGUILayout.PropertyField(cornerSegments);
        serializedObject.ApplyModifiedProperties();
    }
}
#endif
