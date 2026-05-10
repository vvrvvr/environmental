#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(IntroSequenceBlock))]
public sealed class IntroSequenceBlockDrawer : PropertyDrawer
{
    private static int GetLineCount(SerializedProperty property)
    {
        var actionProp = property.FindPropertyRelative("action");
        var action = (IntroSequenceAction)actionProp.enumValueIndex;
        switch (action)
        {
            case IntroSequenceAction.WaitForButtonClick:
                return 3;
            case IntroSequenceAction.FadeUIImage:
                return property.FindPropertyRelative("fadeUiFadeIn").boolValue ? 6 : 5;
            default:
                return 3;
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var action = (IntroSequenceAction)property.FindPropertyRelative("action").enumValueIndex;
        if (action == IntroSequenceAction.DeactivateGameObjects)
        {
            var lineH = EditorGUIUtility.singleLineHeight;
            var sp = EditorGUIUtility.standardVerticalSpacing;
            var arr = property.FindPropertyRelative("objectsToDeactivate");
            return lineH + sp + lineH + sp + EditorGUI.GetPropertyHeight(arr, GUIContent.none, true);
        }

        var lines = GetLineCount(property);
        var h = EditorGUIUtility.singleLineHeight;
        var sp2 = EditorGUIUtility.standardVerticalSpacing;
        return lines * h + (lines - 1) * sp2;
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
        var fadeImageProp = property.FindPropertyRelative("fadeUiImage");
        var fadeInProp = property.FindPropertyRelative("fadeUiFadeIn");
        var fadeEndAlphaProp = property.FindPropertyRelative("fadeUiEndAlpha");
        var objectsToDeactivateProp = property.FindPropertyRelative("objectsToDeactivate");

        var row0 = new Rect(position.x, y, position.width, lineH);
        row0 = EditorGUI.PrefixLabel(row0, GUIUtility.GetControlID(FocusType.Passive), label);
        EditorGUI.PropertyField(row0, actionProp, GUIContent.none);
        y += lineH + sp;

        EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), waitTweenProp);
        y += lineH + sp;

        var action = (IntroSequenceAction)actionProp.enumValueIndex;
        if (action == IntroSequenceAction.WaitForButtonClick)
        {
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), buttonProp);
        }
        else if (action == IntroSequenceAction.FadeUIImage)
        {
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), float0Prop);
            y += lineH + sp;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), fadeImageProp);
            y += lineH + sp;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), fadeInProp);
            y += lineH + sp;
            if (fadeInProp.boolValue)
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), fadeEndAlphaProp);
        }
        else if (action == IntroSequenceAction.DeactivateGameObjects)
        {
            var arrH = EditorGUI.GetPropertyHeight(objectsToDeactivateProp, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, arrH), objectsToDeactivateProp, new GUIContent("Objects To Deactivate"), true);
        }
        else
        {
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), float0Prop);
        }

        EditorGUI.EndProperty();
    }
}
#endif
