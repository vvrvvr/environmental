#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(GameRestartSequenceBlock))]
public sealed class GameRestartSequenceBlockDrawer : PropertyDrawer
{
    private const float NoteIndent = 14f;
    private const float SectionGap = 6f;
    private const float NoteMinLines = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var lineH = EditorGUIUtility.singleLineHeight;
        var sp = EditorGUIUtility.standardVerticalSpacing;
        var w = GetContentWidth(property);
        var noteH = GetNoteHeight(property.FindPropertyRelative("blockNote"), w);
        var action = (GameRestartSequenceAction)property.FindPropertyRelative("action").enumValueIndex;

        var bodyLines = action == GameRestartSequenceAction.None ? 1f : 2f;
        var extraHelp = 0f;

        switch (action)
        {
            case GameRestartSequenceAction.MinimapGraphRewind:
                extraHelp = HelpBoxHeight("Float0 не используется. Укажите Minimap Graph Rewind на контроллере.", w);
                bodyLines = 2f;
                break;
            case GameRestartSequenceAction.SetGraphImpulseJellyTiltEnabled:
                bodyLines = 3f;
                extraHelp = HelpBoxHeight(
                    "Wait For Completion и Float0 не используются. Пустая цель — с GameRestartSequenceController или FindObjectOfType.",
                    w);
                break;
            case GameRestartSequenceAction.RotateGameObject180AroundY:
                bodyLines = 4f;
                break;
            case GameRestartSequenceAction.ScaleGameObjectToOrFromZero:
                bodyLines = 5f;
                break;
            case GameRestartSequenceAction.WaitSeconds:
            case GameRestartSequenceAction.ReloadScene:
                bodyLines = 3f;
                break;
        }

        return noteH + SectionGap + bodyLines * lineH + (bodyLines - 1) * sp + extraHelp +
               (extraHelp > 0f ? sp : 0f);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var lineH = EditorGUIUtility.singleLineHeight;
        var sp = EditorGUIUtility.standardVerticalSpacing;
        var y = position.y;
        var contentW = GetContentWidth(property);

        var noteProp = property.FindPropertyRelative("blockNote");
        var actionProp = property.FindPropertyRelative("action");
        var waitProp = property.FindPropertyRelative("waitForCompletion");
        var float0Prop = property.FindPropertyRelative("float0");
        var sceneProp = property.FindPropertyRelative("reloadSceneName");
        var enableProp = property.FindPropertyRelative("enableComponent");
        var tiltProp = property.FindPropertyRelative("graphImpulseJellyTilt");
        var rotateTargetProp = property.FindPropertyRelative("rotateTarget");
        var tweenEaseProp = property.FindPropertyRelative("tweenEase");
        var scaleTargetProp = property.FindPropertyRelative("scaleTarget");
        var scaleFromZeroProp = property.FindPropertyRelative("scaleFromZero");

        y = DrawNoteSection(position.x, y, contentW, noteProp);
        y += SectionGap;

        var rowAction = new Rect(position.x, y, position.width, lineH);
        rowAction = EditorGUI.PrefixLabel(rowAction, GUIUtility.GetControlID(FocusType.Passive), label);
        EditorGUI.PropertyField(rowAction, actionProp, GUIContent.none);
        y += lineH + sp;

        var action = (GameRestartSequenceAction)actionProp.enumValueIndex;
        if (action == GameRestartSequenceAction.None)
        {
            EditorGUI.EndProperty();
            return;
        }

        var ix = position.x + NoteIndent;
        var iw = position.width - NoteIndent;

        if (action == GameRestartSequenceAction.SetGraphImpulseJellyTiltEnabled)
        {
            EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), enableProp, new GUIContent("Enable Component"));
            y += lineH + sp;
            EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), tiltProp, new GUIContent("Graph Impulse Jelly Tilt"));
            y += lineH + sp;
            var helpH = HelpBoxHeight(
                "Wait For Completion и Float0 не используются. Пустая цель — с GameRestartSequenceController или FindObjectOfType.",
                iw);
            EditorGUI.HelpBox(new Rect(ix, y, iw, helpH),
                "Wait For Completion и Float0 не используются. Пустая цель — с GameRestartSequenceController или FindObjectOfType.",
                MessageType.Info);
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), waitProp);
        y += lineH + sp;

        switch (action)
        {
            case GameRestartSequenceAction.WaitSeconds:
                EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), float0Prop);
                break;

            case GameRestartSequenceAction.RotateGameObject180AroundY:
                EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), rotateTargetProp, new GUIContent("Rotate Target"));
                y += lineH + sp;
                EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), float0Prop, new GUIContent("Duration (sec)"));
                y += lineH + sp;
                EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), tweenEaseProp, new GUIContent("Tween Ease"));
                break;

            case GameRestartSequenceAction.ScaleGameObjectToOrFromZero:
                EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), scaleTargetProp, new GUIContent("Scale Target"));
                y += lineH + sp;
                EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), scaleFromZeroProp, new GUIContent("Scale From Zero"));
                y += lineH + sp;
                EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), float0Prop, new GUIContent("Duration (sec)"));
                y += lineH + sp;
                EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), tweenEaseProp, new GUIContent("Tween Ease"));
                break;

            case GameRestartSequenceAction.ReloadScene:
                EditorGUI.PropertyField(new Rect(ix, y, iw, lineH), sceneProp, new GUIContent("Reload Scene Name"));
                break;

            case GameRestartSequenceAction.MinimapGraphRewind:
                var helpH = HelpBoxHeight("Float0 не используется. Укажите Minimap Graph Rewind на контроллере.", iw);
                EditorGUI.HelpBox(new Rect(ix, y, iw, helpH),
                    "Float0 не используется. Укажите Minimap Graph Rewind на контроллере.",
                    MessageType.Info);
                break;
        }

        EditorGUI.EndProperty();
    }

    private static float GetContentWidth(SerializedProperty property)
    {
        return Mathf.Max(80f, EditorGUIUtility.currentViewWidth - 48f);
    }

    private static float HelpBoxHeight(string text, float width)
    {
        return EditorStyles.helpBox.CalcHeight(new GUIContent(text), width);
    }

    private static float GetNoteHeight(SerializedProperty noteProp, float width)
    {
        var text = noteProp != null ? noteProp.stringValue : string.Empty;
        var minH = EditorGUIUtility.singleLineHeight * NoteMinLines + 6f;
        if (string.IsNullOrEmpty(text))
            return minH;

        var style = EditorStyles.textArea;
        return Mathf.Max(minH, style.CalcHeight(new GUIContent(text), width - NoteIndent - 4f) + 8f);
    }

    private static float DrawNoteSection(float x, float y, float contentW, SerializedProperty noteProp)
    {
        var noteH = GetNoteHeight(noteProp, contentW);
        var boxRect = new Rect(x + NoteIndent, y, contentW - NoteIndent, noteH);
        var bg = string.IsNullOrWhiteSpace(noteProp.stringValue)
            ? new Color(0.55f, 0.55f, 0.55f, 0.12f)
            : new Color(0.35f, 0.55f, 0.75f, 0.22f);
        EditorGUI.DrawRect(boxRect, bg);

        EditorGUI.LabelField(new Rect(x, y, NoteIndent - 2f, EditorGUIUtility.singleLineHeight), "▸", EditorStyles.miniLabel);

        noteProp.stringValue = EditorGUI.TextArea(
            boxRect,
            noteProp.stringValue ?? string.Empty,
            EditorStyles.textArea);

        return y + noteH;
    }
}
#endif
