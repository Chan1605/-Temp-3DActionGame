using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 수련장(튜토리얼) 씬 만들기 (다시 실행해도 됨)
//  1) 처음 한 번: SampleScene을 복사해 Tutorial.unity로 (플레이어·카메라·HUD·메뉴 구성을 그대로 씀)
//  2) 본편 구역·화톳불·배치된 적을 지우고, 구역 1 테마를 복사한 수련장 지형을 생성 (전투 구역 기능은 뗌)
//  3) 왼쪽: 허수아비 6 (표식·연격·띄우기·차지) / 오른쪽: 공격하는 수련 상대 1 + 허수아비 2 (간파·저스트 회피)
//  4) 입구에 화톳불, 녹화 모드(F10), 빌드 설정에 씬 추가
public static class TutorialSceneSetup
{
    const string SourceScenePath = "Assets/1.Scene/SampleScene.unity";
    const string ScenePath = "Assets/1.Scene/Tutorial.unity";
    const string SourceThemePath = "Assets/11.Data/Zone/Zone1_Sanmun.asset";
    const string ThemePath = "Assets/11.Data/Zone/Tutorial_Dojo.asset";
    const string DummyPrefabPath = "Assets/2.Model/Prefabs/Swordsman.prefab";
    const string SparPrefabPath = "Assets/2.Model/Prefabs/Swordsman_Trainer.prefab";
    const string ShrinePrefabPath = "Assets/2.Model/Prefabs/Shrine.prefab";
    const string GroundName = "TrainingGround";
    const string ShrineGroupName = "Shrines";

    static readonly Vector3 DummyCenter = new Vector3(-5f, 0f, 1f);
    const float DummyRingRadius = 2.6f;
    static readonly Vector3 SparCenter = new Vector3(7f, 0f, 3f);
    const float SparLeash = 5.5f;

    [MenuItem("Tools/Setup/Tutorial Scene")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Tutorial] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        GameObject dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPrefabPath);
        GameObject sparPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SparPrefabPath);
        if (dummyPrefab == null || sparPrefab == null)
        {
            Debug.LogError("[Tutorial] Swordsman / Swordsman_Trainer 프리팹이 없습니다.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
        {
            Debug.LogError($"[Tutorial] {SourceScenePath}를 복사하지 못했습니다.");
            return;
        }
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        ZoneTheme theme = LoadOrCreateTheme();
        if (theme == null) return;

        RemoveMainContent(theme.zoneName);
        ZoneGenerator.Generate(theme, true, true, true, new int[0]);
        GameObject root = GameObject.Find(theme.zoneName);
        Transform respawn = StripArena(root);
        Terrain terrain = root.GetComponentInChildren<Terrain>();

        MovePlayer(respawn);
        int enemies = BuildTrainingGround(theme.origin, respawn, terrain, dummyPrefab, sparPrefab);
        bool shrine = PlaceShrine(respawn, terrain);
        EnsureRecordingMode();
        AddToBuildSettings();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Tutorial] 완료 · 수련 상대 {enemies}명 · 화톳불 {(shrine ? "배치" : "없음 (Skill Tree + Shrines 먼저)")} · F10 녹화 모드 → {ScenePath} 저장됨");
    }

    static ZoneTheme LoadOrCreateTheme()
    {
        var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>(ThemePath);
        if (theme != null) return theme;

        var source = AssetDatabase.LoadAssetAtPath<ZoneTheme>(SourceThemePath);
        if (source == null)
        {
            Debug.LogError("[Tutorial] 구역 1 테마가 없습니다. Tools/Map/Generate All Zones를 먼저 실행하세요.");
            return null;
        }
        theme = Object.Instantiate(source);
        theme.zoneName = "Tutorial_Dojo";
        theme.seed = source.seed + 101;
        theme.origin = Vector3.zero;
        theme.displayNumber = "수련";
        theme.displayName = "수련장";
        AssetDatabase.CreateAsset(theme, ThemePath);
        AssetDatabase.SaveAssets();
        return theme;
    }

    // 본편 구역(ZoneArena가 붙은 루트) · 화톳불 · 미리 둔 적 · 예전 수련 배치를 지움. 수련장 지형 루트는 남김 (Generate가 갱신)
    static void RemoveMainContent(string keepRoot)
    {
        foreach (ZoneArena arena in Object.FindObjectsByType<ZoneArena>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (arena != null && arena.gameObject.name != keepRoot) Object.DestroyImmediate(arena.gameObject);

        DestroyByName(GroundName);
        DestroyByName(ShrineGroupName);

        foreach (EnemySwordsman e in Object.FindObjectsByType<EnemySwordsman>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (e != null) Object.DestroyImmediate(OutermostRoot(e.gameObject));
        foreach (TrainingDummy d in Object.FindObjectsByType<TrainingDummy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (d != null) Object.DestroyImmediate(OutermostRoot(d.gameObject));
    }

    static void DestroyByName(string name)
    {
        GameObject go = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name);
        if (go != null) Object.DestroyImmediate(go);
    }

    static GameObject OutermostRoot(GameObject go)
    {
        GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        return root != null ? root : go;
    }

    // 웨이브·결계·사망 처리는 쓰지 않음 → ZoneArena를 떼고, 입구 결계는 열고 출구 결계는 닫아 둠 (튜토리얼 끝에 열 예정)
    // 분위기(하늘·안개·해)는 Generate가 씬에 이미 적용함. 사망 시엔 PlayerHealth가 제자리 회복
    static Transform StripArena(GameObject root)
    {
        Transform respawn = null;
        ZoneArena arena = root.GetComponent<ZoneArena>();
        if (arena != null)
        {
            var so = new SerializedObject(arena);
            respawn = so.FindProperty("respawnPoint").objectReferenceValue as Transform;
            var entrance = so.FindProperty("entranceBarrier").objectReferenceValue as GameObject;
            var exit = so.FindProperty("exitBarrier").objectReferenceValue as GameObject;
            if (entrance != null) entrance.SetActive(false);
            if (exit != null) exit.SetActive(true);
            Object.DestroyImmediate(arena);
        }
        if (respawn == null) respawn = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RespawnPoint");
        return respawn;
    }

    static void MovePlayer(Transform respawn)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null || respawn == null) return;
        player.transform.SetPositionAndRotation(respawn.position, respawn.rotation);

        // 시작할 때 카메라가 먼 곳에서 날아오지 않게 플레이어 뒤로 옮겨 둠
        ThirdPersonCamera cam = Object.FindAnyObjectByType<ThirdPersonCamera>();
        if (cam == null) return;
        Vector3 pivot = respawn.position + Vector3.up * 1.5f;
        cam.transform.position = pivot - respawn.forward * 4f + Vector3.up * 1f;
        cam.transform.rotation = Quaternion.LookRotation(pivot - cam.transform.position, Vector3.up);
    }

    // 핵심: 두 무리를 12m쯤 떨어뜨려, 허수아비 쪽에서 기술을 찍는 동안 수련 상대(묶인 범위 5.5m)가 끼어들지 않게
    static int BuildTrainingGround(Vector3 center, Transform respawn, Terrain terrain, GameObject dummyPrefab, GameObject sparPrefab)
    {
        var group = new GameObject(GroundName);
        Vector3 entrance = respawn != null ? respawn.position : center - Vector3.forward * 20f;
        int count = 0;

        Vector3 dummyCenter = center + DummyCenter;
        for (int i = 0; i < 6; i++)
        {
            float angle = (30f + i * 60f) * Mathf.Deg2Rad;
            Vector3 pos = dummyCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * DummyRingRadius;
            Spawn(dummyPrefab, group.transform, terrain, pos, entrance, true, 0f, $"Dummy_{i + 1}");
            count++;
        }

        Vector3 sparCenter = center + SparCenter;
        Spawn(sparPrefab, group.transform, terrain, sparCenter, entrance, false, SparLeash, "Spar");
        Spawn(dummyPrefab, group.transform, terrain, sparCenter + new Vector3(-1.8f, 0f, 1.6f), entrance, true, 0f, "Spar_Dummy_L");
        Spawn(dummyPrefab, group.transform, terrain, sparCenter + new Vector3(1.8f, 0f, 1.6f), entrance, true, 0f, "Spar_Dummy_R");
        return count + 3;
    }

    static void Spawn(GameObject prefab, Transform parent, Terrain terrain, Vector3 pos, Vector3 lookAt, bool passive, float leash, string name)
    {
        pos.y = GroundY(terrain, pos);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        Vector3 face = lookAt - pos;
        face.y = 0f;
        go.transform.SetPositionAndRotation(pos, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face, Vector3.up) : Quaternion.identity);

        EnemySwordsman enemy = go.GetComponentInChildren<EnemySwordsman>();
        if (enemy == null) return;
        var so = new SerializedObject(enemy);
        so.FindProperty("trainer").boolValue = true;
        so.FindProperty("passive").boolValue = passive;
        so.FindProperty("leashRadius").floatValue = leash;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static bool PlaceShrine(Transform respawn, Terrain terrain)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShrinePrefabPath);
        if (prefab == null || respawn == null) return false;

        var group = new GameObject(ShrineGroupName);
        Vector3 pos = respawn.position + respawn.forward * 2.5f + respawn.right * 2f;
        pos.y = GroundY(terrain, pos);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-respawn.right, Vector3.up));
        go.name = "Shrine (Tutorial)";

        if (Object.FindAnyObjectByType<SkillTreeMenu>(FindObjectsInactive.Include) == null)
            Debug.LogWarning("[Tutorial] 씬에 SkillTreeMenu가 없습니다. Tools/Setup/Skill Tree + Shrines를 이 씬에서 한 번 실행하세요.");
        return true;
    }

    static void EnsureRecordingMode()
    {
        if (Object.FindAnyObjectByType<RecordingMode>(FindObjectsInactive.Include) != null) return;
        new GameObject("RecordingMode").AddComponent<RecordingMode>();
    }

    static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static float GroundY(Terrain terrain, Vector3 pos) =>
        terrain != null ? terrain.SampleHeight(pos) + terrain.transform.position.y : pos.y;
}
