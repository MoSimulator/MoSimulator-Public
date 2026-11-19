using UnityEditor;
using UnityEngine;

public static class EditorMemoryCleaner
{
    [MenuItem("Tools/Force Clear Editor Memory")]
    public static void Clean()
    {
        // Unloads assets that are no longer referenced in the current scene hierarchy
        EditorUtility.UnloadUnusedAssetsImmediate(true);
        
        // Forces the C# garbage collector to clear managed memory
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        
        Debug.Log("Editor memory explicitly flushed and garbage collected.");
    }
}