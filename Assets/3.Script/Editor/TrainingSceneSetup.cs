using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools/Setup/Training Scene — 타이틀 TRAINING으로 들어가는 자유 연습장 (다시 실행해도 됨)
//  1) 처음 한 번: SampleScene을 복사해 Training.unity로 (플레이어 · 카메라 · HUD · 메뉴 그대로)
//  2) 본편 구역 · 적을 지우고 수련장과 같은 겉모습의 마당 하나 생성 (출구 없이 닫힌 마당)
//  3) 허수아비 5 (앞쪽 줄) + 공격하는 수련 상대 3 (서 · 동 · 북, 각자 자기 자리 근처에서만 싸움)
//  4) 스킬트리 전부 해금(패시브 포함) + TrainingMode(뇌기 무한 · 체력 회복) + 녹화 모드(F10) + 빌드 설정
public static class TrainingSceneSetup
{
    const string ScenePath = "Assets/1.Scene/Training.unity";
    const string ThemePath = "Assets/11.Data/Zone/Training_Yard.asset";
    const float SparLeash = 5f;

    [MenuItem("Tools/Setup/Training Scene")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        GameObject dummy = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialSceneSetup.DummyPrefabPath);
        GameObject spar = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialSceneSetup.SparPrefabPath);
        ZoneTheme theme = LoadOrCreateTheme();
        if (dummy == null || spar == null || theme == null)
        {
            Debug.LogError("[Training] 허수아비 · 수련 상대 프리팹이나 테마가 없습니다. Tools/Setup/Tutorial Scene을 먼저 실행하세요.");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(TutorialSceneSetup.SourceScenePath, ScenePath))
        {
            Debug.LogError("[Training] SampleScene을 복사하지 못했습니다.");
            return;
        }
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        TutorialSceneSetup.RemoveMainContent(theme.zoneName);
        ZoneGenerator.Generate(theme, true, true, true, new int[0]); // 남북 모두 막음 → 닫힌 마당
        GameObject root = GameObject.Find(theme.zoneName);
        (Transform respawn, GameObject exit) = TutorialSceneSetup.StripArena(root);
        if (exit != null) exit.SetActive(true);
        Terrain terrain = root.GetComponentInChildren<Terrain>();

        // 시작은 마당 안 남쪽 (입구 밖이 아니라 바로 연습할 수 있게)
        Vector3 c = theme.origin;
        var start = new GameObject("TrainingStart").transform;
        start.position = c + new Vector3(0f, 0f, -6f);
        start.position = new Vector3(start.position.x, TutorialSceneSetup.GroundY(terrain, start.position), start.position.z);
        start.rotation = Quaternion.identity;
        TutorialSceneSetup.MovePlayer(start);
        Object.DestroyImmediate(start.gameObject);

        var group = new GameObject(TutorialSceneSetup.GroundName).transform;
        Vector3 face = c + new Vector3(0f, 0f, -6f);
        for (int i = 0; i < 5; i++)
        {
            float x = -6f + i * 3f;
            float z = 4f + (i == 1 || i == 3 ? 1.2f : i == 2 ? 2f : 0f);
            TutorialSceneSetup.Spawn(dummy, group, terrain, c + new Vector3(x, 0f, z), face, true, 0f, $"Dummy_{i + 1}");
        }
        TutorialSceneSetup.Spawn(spar, group, terrain, c + new Vector3(-10f, 0f, -3f), c, false, SparLeash, "Spar_West");
        TutorialSceneSetup.Spawn(spar, group, terrain, c + new Vector3(10f, 0f, -3f), c, false, SparLeash, "Spar_East");
        TutorialSceneSetup.Spawn(spar, group, terrain, c + new Vector3(0f, 0f, 12f), c, false, SparLeash, "Spar_North");

        SkillTreeMenu menu = Object.FindAnyObjectByType<SkillTreeMenu>(FindObjectsInactive.Include);
        if (menu != null)
        {
            var so = new SerializedObject(menu);
            so.FindProperty("unlockAllAtStart").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        TrainingMode mode = Object.FindAnyObjectByType<TrainingMode>(FindObjectsInactive.Include);
        if (mode == null) mode = new GameObject("TrainingMode").AddComponent<TrainingMode>();
        var m = new SerializedObject(mode);
        m.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(TutorialSceneSetup.BodyFontPath);
        m.ApplyModifiedPropertiesWithoutUndo();
        TutorialSceneSetup.EnsureRecordingMode();

        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Training] 완료 · 허수아비 5 · 수련 상대 3 · 스킬 전부 해금 {(menu != null ? "켬" : "(SkillTreeMenu 없음)")} → {ScenePath}");
    }

    // 수련장 테마를 복사 (같은 노을 마당, 지형 데이터는 따로)
    static ZoneTheme LoadOrCreateTheme()
    {
        var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>(ThemePath);
        if (theme != null) return theme;
        ZoneTheme source = TutorialSceneSetup.LoadOrCreateTheme();
        if (source == null) return null;
        theme = Object.Instantiate(source);
        theme.zoneName = "Training_Arena";
        theme.seed = source.seed + 7;
        theme.displayName = "연습장";
        AssetDatabase.CreateAsset(theme, ThemePath);
        AssetDatabase.SaveAssets();
        return theme;
    }
}
