#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(IntroSequenceBlock))]
public sealed class IntroSequenceBlockDrawer : PropertyDrawer
{
    private static float HelpBoxHeight(string text, float width)
    {
        return EditorStyles.helpBox.CalcHeight(new GUIContent(text), width);
    }

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
            case IntroSequenceAction.FadeMaterialAlpha:
                return 5;
            case IntroSequenceAction.FadeTMPTextAlphaToZero:
                return 4;
            default:
                return 3;
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var action = (IntroSequenceAction)property.FindPropertyRelative("action").enumValueIndex;
        if (action == IntroSequenceAction.DeactivateGameObjects || action == IntroSequenceAction.ActivateGameObjects)
        {
            var lineH = EditorGUIUtility.singleLineHeight;
            var sp = EditorGUIUtility.standardVerticalSpacing;
            var arrName = action == IntroSequenceAction.DeactivateGameObjects ? "objectsToDeactivate" : "objectsToActivate";
            var arr = property.FindPropertyRelative(arrName);
            return lineH + sp + lineH + sp + EditorGUI.GetPropertyHeight(arr, GUIContent.none, true);
        }

        if (action == IntroSequenceAction.DisableMoveFourEnableGraphAndVideo)
        {
            var lineH = EditorGUIUtility.singleLineHeight;
            var sp = EditorGUIUtility.standardVerticalSpacing;
            var w = EditorGUIUtility.currentViewWidth - 40f;
            var helpH = HelpBoxHeight(
                "Объекты Move Four и Intro Graph And Video задаются на IntroSequenceController.",
                w);
            return lineH + sp + lineH + sp + helpH;
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
        var objectsToActivateProp = property.FindPropertyRelative("objectsToActivate");
        var fadeMaterialProp = property.FindPropertyRelative("fadeMaterial");
        var fadeMaterialEndAlphaProp = property.FindPropertyRelative("fadeMaterialEndAlpha");
        var fadeTmpTextProp = property.FindPropertyRelative("fadeTmpText");

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
        else if (action == IntroSequenceAction.FadeMaterialAlpha)
        {
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), float0Prop);
            y += lineH + sp;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), fadeMaterialProp);
            y += lineH + sp;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), fadeMaterialEndAlphaProp);
        }
        else if (action == IntroSequenceAction.FadeTMPTextAlphaToZero)
        {
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), float0Prop);
            y += lineH + sp;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), fadeTmpTextProp);
        }
        else if (action == IntroSequenceAction.DeactivateGameObjects)
        {
            var arrH = EditorGUI.GetPropertyHeight(objectsToDeactivateProp, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, arrH), objectsToDeactivateProp, new GUIContent("Objects To Deactivate"), true);
        }
        else if (action == IntroSequenceAction.ActivateGameObjects)
        {
            var arrH = EditorGUI.GetPropertyHeight(objectsToActivateProp, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, arrH), objectsToActivateProp, new GUIContent("Objects To Activate"), true);
        }
        else if (action == IntroSequenceAction.DisableMoveFourEnableGraphAndVideo)
        {
            var help = "Объекты Move Four и Intro Graph And Video задаются на IntroSequenceController.";
            var helpH = HelpBoxHeight(help, position.width);
            EditorGUI.HelpBox(new Rect(position.x, y, position.width, helpH), help, MessageType.Info);
        }
        else
        {
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), float0Prop);
        }

        EditorGUI.EndProperty();
    }
}
#endif
