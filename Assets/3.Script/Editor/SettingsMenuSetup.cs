using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 열린 씬에 ESC 설정 패널(SettingsMenu)을 놓고 Pretendard 글꼴을 연결 (다시 실행해도 됨)
public static class SettingsMenuSetup
{
    const string TitleFontPath = "Assets/9.Font/public/static/Pretendard-SemiBold.otf";
    const string BodyFontPath = "Assets/9.Font/public/static/Pretendard-Regular.otf";

    [MenuItem("Tools/Setup/Settings Menu (ESC)")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[SettingsMenu] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        SettingsMenu menu = Object.FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include);
        if (menu == null)
        {
            var go = new GameObject("SettingsMenu");
            Undo.RegisterCreatedObjectUndo(go, "Settings Menu");
            menu = go.AddComponent<SettingsMenu>();
        }

        var so = new SerializedObject(menu);
        so.FindProperty("titleFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
        so.FindProperty("bodyFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath);
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
        Selection.activeGameObject = menu.gameObject;
        Debug.Log("[SettingsMenu] 완료 · 씬을 저장하세요. 플레이 중 ESC로 열림");
    }
}
