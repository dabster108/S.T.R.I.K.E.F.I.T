// MenuMusicEditor.cs
// Purpose: MenuMusic's Inspector: a warning above the Clip field while it is empty, with a button that opens the drop folder
//          (Assets/Audio/Music/Menu, README inside). No audio is invented or assigned by code.
// Attach to: nothing (custom Inspector for MenuMusic, Editor only)

using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MenuMusic))]
public class MenuMusicEditor : Editor
{
    const string Folder = "Assets/Audio/Music/Menu";

    public override void OnInspectorGUI()
    {
        var m = (MenuMusic)target;
        if (m.clip == null)
        {
            EditorGUILayout.HelpBox("No menu music clip assigned: the menu is silent. Drop a licensed, loopable ambient track into " + Folder +
                                    " and drag it into Clip below.", MessageType.Warning);
            if (GUILayout.Button("Open the menu music folder"))
            {
                var f = AssetDatabase.LoadAssetAtPath<Object>(Folder);
                if (f != null) { Selection.activeObject = f; EditorGUIUtility.PingObject(f); }
            }
            EditorGUILayout.Space();
        }
        DrawDefaultInspector();
    }
}
