#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(IntroSequenceBlock))]
public sealed class IntroSequenceBlockDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        const int lines = 3;
        var h = EditorGUIUtility.singleLineHeight;
        var sp = EditorGUIUtility.standardVerticalSpacing;
        return lines * h + (lines - 1) * sp;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var lineH = EditorGUIUtility.singleLineHeight;
        var sp = EditorGUIUtility.standardVerticalSpacing;
        var y = position.y;

        var actionProp = property.FindPropertyRelative("action");
        var waitTweenProp = property.FindPropertyRelative("waitForTweenCompletion");
        var float0Prop = property.FindPropertyRelative("float0");
        var buttonProp = property.FindPropertyRelative("waitForButton");

        var row0 = new Rect(position.x, y, position.width, lineH);
        row0 = EditorGUI.PrefixLabel(row0, GUIUtility.GetControlID(FocusType.Passive), label);
        EditorGUI.PropertyField(row0, actionProp, GUIContent.none);
        y += lineH + sp;

        EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), waitTweenProp);
        y += lineH + sp;

        var action = (IntroSequenceAction)actionProp.enumValueIndex;
        if (action == IntroSequenceAction.WaitForButtonClick)
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), buttonProp);
        else
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), float0Prop);

        EditorGUI.EndProperty();
    }
}
#endif
