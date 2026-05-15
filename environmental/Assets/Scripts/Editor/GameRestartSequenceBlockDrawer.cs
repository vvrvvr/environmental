#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(GameRestartSequenceBlock))]
public sealed class GameRestartSequenceBlockDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var lineH = EditorGUIUtility.singleLineHeight;
        var sp = EditorGUIUtility.standardVerticalSpacing;
        var action = (GameRestartSequenceAction)property.FindPropertyRelative("action").enumValueIndex;
        if (action == GameRestartSequenceAction.None)
            return 2 * lineH + sp;
        if (action == GameRestartSequenceAction.MinimapGraphRewind)
        {
            var help = "Float0 не используется. Укажите Minimap Graph Rewind на контроллере.";
            var w = Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 40f);
            var helpH = EditorStyles.helpBox.CalcHeight(new GUIContent(help), w);
            return 2 * lineH + 2 * sp + helpH;
        }

        return 3 * lineH + 2 * sp;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var lineH = EditorGUIUtility.singleLineHeight;
        var sp = EditorGUIUtility.standardVerticalSpacing;
        var y = position.y;

        var actionProp = property.FindPropertyRelative("action");
        var waitProp = property.FindPropertyRelative("waitForCompletion");
        var float0Prop = property.FindPropertyRelative("float0");
        var sceneProp = property.FindPropertyRelative("reloadSceneName");

        var row0 = new Rect(position.x, y, position.width, lineH);
        row0 = EditorGUI.PrefixLabel(row0, GUIUtility.GetControlID(FocusType.Passive), label);
        EditorGUI.PropertyField(row0, actionProp, GUIContent.none);
        y += lineH + sp;

        EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), waitProp);
        y += lineH + sp;

        var action = (GameRestartSequenceAction)actionProp.enumValueIndex;
        if (action == GameRestartSequenceAction.None)
        {
            EditorGUI.EndProperty();
            return;
        }

        if (action == GameRestartSequenceAction.WaitSeconds)
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), float0Prop);
        else if (action == GameRestartSequenceAction.ReloadScene)
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), sceneProp, new GUIContent("Reload Scene Name"));
        else if (action == GameRestartSequenceAction.MinimapGraphRewind)
        {
            var help = "Float0 не используется. Укажите Minimap Graph Rewind на контроллере.";
            var helpH = EditorStyles.helpBox.CalcHeight(new GUIContent(help), position.width);
            EditorGUI.HelpBox(new Rect(position.x, y, position.width, helpH), help, MessageType.Info);
        }

        EditorGUI.EndProperty();
    }
}
#endif
