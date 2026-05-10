#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class IntroSequenceFadeMaterialPlayModeCleanup
{
    static IntroSequenceFadeMaterialPlayModeCleanup()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode)
            return;

        foreach (var controller in Object.FindObjectsByType<IntroSequenceController>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
            controller.ResetFadeMaterialAlphaBlocksToZero();
    }
}
#endif
