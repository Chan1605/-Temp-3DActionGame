using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools/Setup/Loading Scene
//  Assets/1.Scene/Loading.unity를 만들거나 갱신 (카메라 + LoadingScreen, 글꼴 연결) → 빌드 설정에 추가
//  빌드에 있으면 SceneTransition이 타이틀 → 수련장 → 본편 사이에 자동으로 거침
public static class LoadingSceneSetup
{
    const string ScenePath = "Assets/1.Scene/Loading.unity";
    const string TitleFontPath = "Assets/9.Font/public/static/Pretendard-SemiBold.otf";
    const string BodyFontPath = "Assets/9.Font/public/static/Pretendard-Regular.otf";

    [MenuItem("Tools/Setup/Loading Scene")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        bool created = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null;
        Scene scene = created
            ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        if (created)
        {
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0; // 화면은 전부 UI
        }
        Camera main = Object.FindAnyObjectByType<Camera>(); // 소리 듣는 곳 (없으면 "no audio listeners" 경고)
        if (main != null && main.GetComponent<AudioListener>() == null) main.gameObject.AddComponent<AudioListener>();

        LoadingScreen screen = Object.FindAnyObjectByType<LoadingScreen>(FindObjectsInactive.Include);
        if (screen == null) screen = new GameObject("LoadingScreen").AddComponent<LoadingScreen>();
        var so = new SerializedObject(screen);
        so.FindProperty("titleFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
        so.FindProperty("bodyFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
        Debug.Log($"[Loading] {(created ? "생성" : "갱신")} → {ScenePath} (빌드 설정 포함)");
    }
}
