#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(BackgroundController))]
public sealed class BackgroundControllerEditor : Editor
{
    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var c = (BackgroundController)target;
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Отладка вида фона — действия", EditorStyles.boldLabel);

        if (!c.DebugPreviewEnabled)
        {
            EditorGUILayout.HelpBox(
                "Включи Debug Preview: фон покажет превью (Debug Texture / Debug Color) в редакторе и в Play Mode. " +
                "Подбери вид, затем «Записать в текущую ноду» (Play Mode) или «Записать в Debug Target Node».",
                MessageType.Info);
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Взять из дефолта"))
                c.EditorDebugTakeFromDefault();
            if (GUILayout.Button("Взять из материала"))
                c.EditorDebugTakeFromMaterial();
        }

        // --- Текущая нода (Play Mode) ---
        EditorGUILayout.Space(4);
        var current = Application.isPlaying && GameManager.Instance != null
            ? GameManager.Instance.CurrentSelectedMapNode
            : null;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("«Записать в текущую ноду» работает в Play Mode: берётся нода, выбранная на карте.", MessageType.None);
        }
        else
        {
            EditorGUILayout.LabelField("Текущая нода", current != null ? current.name : "— ничего не выбрано —");
            using (new EditorGUI.DisabledScope(current == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Взять из текущей ноды"))
                        c.EditorDebugTakeFromNodeObject(current);
                    if (GUILayout.Button("Записать в текущую ноду"))
                        BackgroundPlayModeWrites.WriteNow(current, c.EditorDebugTexture, c.EditorDebugColor);
                }
            }

            int pending = BackgroundPlayModeWrites.PendingCount;
            if (pending > 0)
                EditorGUILayout.HelpBox(
                    $"Записей в ноды: {pending}. Перенесутся в сцену после выхода из Play Mode (сцену потом сохранить).",
                    MessageType.Info);
        }

        // --- Нода по ссылке (вне Play Mode) ---
        EditorGUILayout.Space(4);
        using (new EditorGUI.DisabledScope(c.EditorDebugTargetNode == null))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Взять из Debug Target Node"))
                    c.EditorDebugTakeFromNode();
                if (GUILayout.Button("Записать в Debug Target Node"))
                {
                    // В Play Mode — через отложенную запись, иначе откатится при выходе из игры.
                    if (Application.isPlaying)
                        BackgroundPlayModeWrites.WriteNow(c.EditorDebugTargetNode, c.EditorDebugTexture, c.EditorDebugColor);
                    else
                        c.EditorDebugWriteToNode();
                }
            }
        }

        EditorGUILayout.Space(4);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Записать превью в дефолт"))
            {
                if (EditorUtility.DisplayDialog(
                        "Фон по умолчанию",
                        "Заменить дефолтные картинку и цвет значениями превью?",
                        "Заменить",
                        "Отмена"))
                    c.EditorDebugWriteToDefault();
            }
        }
    }
}

/// <summary>
/// Запись фона в ноду из Play Mode: сразу в игре + запоминание; после выхода из Play Mode — перенос в ноду сцены.
/// </summary>
[InitializeOnLoad]
public static class BackgroundPlayModeWrites
{
    private const string SessionKey = "Environmental.BackgroundPlayModeWrites";

    [Serializable]
    private class Entry
    {
        public string nodeId;
        public string nodePath;
        public string nodeName;
        public string textureId;
        public Color color;
    }

    [Serializable]
    private class EntryList
    {
        public List<Entry> items = new List<Entry>();
    }

    static BackgroundPlayModeWrites()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static int PendingCount => Load().items.Count;

    public static void WriteNow(Node node, Texture texture, Color color)
    {
        if (node == null)
            return;

        // Сразу — в игре (видно при следующем заходе на ноду).
        node.EditorSetBackground(true, texture, color);

        var list = Load();
        var entry = new Entry
        {
            nodeId = GlobalObjectId.GetGlobalObjectIdSlow(node).ToString(),
            nodePath = GetPath(node.transform),
            nodeName = node.name,
            textureId = texture != null ? GlobalObjectId.GetGlobalObjectIdSlow(texture).ToString() : string.Empty,
            color = color,
        };

        // Повторная запись в ту же ноду заменяет прошлую.
        list.items.RemoveAll(e => e.nodePath == entry.nodePath);
        list.items.Add(entry);
        Save(list);

        Debug.Log($"[Фон] Записано в «{node.name}» (перенесётся в сцену после выхода из Play Mode).", node);
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode)
            return;

        var list = Load();
        if (list.items.Count == 0)
            return;

        var applied = 0;
        foreach (var e in list.items)
        {
            var node = ResolveNode(e);
            if (node == null)
            {
                Debug.LogWarning($"[Фон] Не нашёл ноду «{e.nodeName}» ({e.nodePath}) для записи фона — пропуск.");
                continue;
            }

            Texture tex = null;
            if (!string.IsNullOrEmpty(e.textureId) && GlobalObjectId.TryParse(e.textureId, out var texGid))
                tex = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(texGid) as Texture;

            Undo.RecordObject(node, "Фон из Play Mode");
            node.EditorSetBackground(true, tex, e.color);
            PrefabUtility.RecordPrefabInstancePropertyModifications(node);
            EditorUtility.SetDirty(node);
            EditorSceneManager.MarkSceneDirty(node.gameObject.scene);
            applied++;
            Debug.Log($"[Фон] Перенесено в ноду «{node.name}». Не забудь сохранить сцену.", node);
        }

        Save(new EntryList());
        if (applied > 0)
            Debug.Log($"[Фон] Записей фона перенесено в сцену: {applied}.");
    }

    private static Node ResolveNode(Entry e)
    {
        // Основной путь — по иерархии (в Play Mode у объектов сцены свои экземпляры, путь совпадает с редактором).
        var all = UnityEngine.Object.FindObjectsOfType<Node>(true);
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] != null && GetPath(all[i].transform) == e.nodePath)
                return all[i];
        }

        // Запасной — GlobalObjectId.
        if (!string.IsNullOrEmpty(e.nodeId) && GlobalObjectId.TryParse(e.nodeId, out var gid))
        {
            if (GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) is Node byId && byId != null)
                return byId;
        }

        return null;
    }

    private static string GetPath(Transform t)
    {
        var scenePath = t.gameObject.scene.path;
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return scenePath + ":" + path;
    }

    private static EntryList Load()
    {
        var json = SessionState.GetString(SessionKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return new EntryList();
        try
        {
            return JsonUtility.FromJson<EntryList>(json) ?? new EntryList();
        }
        catch
        {
            return new EntryList();
        }
    }

    private static void Save(EntryList list) =>
        SessionState.SetString(SessionKey, JsonUtility.ToJson(list));
}
#endif
