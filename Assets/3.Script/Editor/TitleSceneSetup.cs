using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

// Tools/Setup/Title Scene
//  1) Assets/1.Scene/Title.unity가 없으면 새로 만듦 (카메라 · EventSystem · TitleScreen · SettingsMenu)
//  2) 이미 있으면 연결만 갱신 (배경 영상 목록 · 글꼴 · 설정 메뉴). 인스펙터에서 바꾼 연출 값은 유지
//  3) 빌드 설정 맨 앞에 Title, 그 뒤에 Tutorial · InGame
public static class TitleSceneSetup
{
    const string ScenePath = "Assets/1.Scene/Title.unity";
    const string VideoFolder = "Assets/12.Video/SkillPreview";
    const string TitleFontPath = "Assets/9.Font/public/static/Pretendard-SemiBold.otf";
    const string BodyFontPath = "Assets/9.Font/public/static/Pretendard-Regular.otf";
    static readonly string[] GameScenes = { "Assets/1.Scene/Tutorial.unity", "Assets/1.Scene/InGame.unity" }; // [변경] 본편 씬 이름 변경

    [MenuItem("Tools/Setup/Title Scene")]
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
        if (Object.FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        TitleScreen title = Object.FindAnyObjectByType<TitleScreen>(FindObjectsInactive.Include);
        if (title == null) title = new GameObject("Title").AddComponent<TitleScreen>();
        SettingsMenu settings = Object.FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include);
        if (settings == null) settings = new GameObject("SettingsMenu").AddComponent<SettingsMenu>();

        Font titleFont = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
        Font bodyFont = AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath);

        var s = new SerializedObject(settings);
        s.FindProperty("titleFont").objectReferenceValue = titleFont;
        s.FindProperty("bodyFont").objectReferenceValue = bodyFont;
        s.FindProperty("inGame").boolValue = false; // 일시정지 · ESC 열기 없음
        s.ApplyModifiedPropertiesWithoutUndo();

        VideoClip[] clips = AssetDatabase.FindAssets("t:VideoClip", new[] { VideoFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<VideoClip>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(c => c != null).OrderBy(c => c.name).ToArray();

        var t = new SerializedObject(title);
        t.FindProperty("titleFont").objectReferenceValue = titleFont;
        t.FindProperty("bodyFont").objectReferenceValue = bodyFont;
        t.FindProperty("settings").objectReferenceValue = settings;
        SerializedProperty list = t.FindProperty("backgroundClips");
        list.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        t.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        SetBuildOrder();
        Debug.Log($"[Title] {(created ? "생성" : "갱신")} · 배경 영상 {clips.Length}개 · 글꼴 {(titleFont != null ? "연결" : "없음")} → {ScenePath} (빌드 0번)");
    }

    // Title을 0번(실행 시 첫 씬)으로, 게임 씬은 빠져 있으면 뒤에 추가
    static void SetBuildOrder()
    {
        var scenes = EditorBuildSettings.scenes.Where(x => x.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        foreach (string path in GameScenes)
            if (!scenes.Any(x => x.path == path) && AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
