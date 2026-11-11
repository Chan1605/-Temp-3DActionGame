using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 수련장(튜토리얼) 씬 만들기 — 동선형 (다시 실행해도 됨)
//  1) 처음 한 번: SampleScene을 복사해 Tutorial.unity로 (플레이어·카메라·HUD·메뉴 구성을 그대로 씀)
//  2) 본편 구역·화톳불·배치된 적을 지우고, 작은 수련터 4개를 길로 이어 생성 (구역 1 테마 복사, 전투 구역 기능은 뗌)
//     기본기(허수아비) → 방어(수련 상대) → 벽력일섬(허수아비) → 각성·처형(수련 상대 + 허수아비 + 화톳불)
//  3) 수련터마다 출구 결계는 닫아 두고 TutorialDirector가 단계를 깨면 엶
//  4) 녹화 모드(F10), 빌드 설정에 씬 추가
//  5) 마지막 수련터 북쪽 길 끝에 본편(산문)으로 넘어가는 출구 // [변경]
//  6) 본편 HUD에만 있는 표시(처형 F · 락온 · 데미지 숫자 · 레벨업 등)를 복사해 옴 — Tools/Setup/Sync Tutorial HUD로 따로도 실행 가능 // [변경]
public static class TutorialSceneSetup
{
    const string SourceScenePath = "Assets/1.Scene/SampleScene.unity";
    const string ScenePath = "Assets/1.Scene/Tutorial.unity";
    const string SourceThemePath = "Assets/11.Data/Zone/Zone1_Sanmun.asset";
    const string ThemeFolder = "Assets/11.Data/Zone";
    const string DummyPrefabPath = "Assets/2.Model/Prefabs/Swordsman.prefab";
    const string SparPrefabPath = "Assets/2.Model/Prefabs/Swordsman_Trainer.prefab";
    const string ShrinePrefabPath = "Assets/2.Model/Prefabs/Shrine.prefab";
    const string TitleFontPath = "Assets/9.Font/public/static/Pretendard-SemiBold.otf";
    const string BodyFontPath = "Assets/9.Font/public/static/Pretendard-Regular.otf";
    const string GroundName = "TrainingGround";
    const string ShrineGroupName = "Shrines";
    const string DirectorName = "Tutorial";
    const string ExitName = "TutorialExit"; // [변경]
    const string OldDojoName = "Tutorial_Dojo"; // 예전 한 칸짜리 수련장

    // 수련터 크기: 본편 구역보다 작게 → 사이 길 약 54m
    const float StationTerrain = 80f, StationCombat = 10f, StationWall = 13f, StationHill = 14f;

    static readonly string[] StationNames = { "기본기", "방어", "벽력일섬", "각성 · 처형" };

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

        var themes = new List<ZoneTheme>();
        for (int i = 0; i < StationNames.Length; i++)
        {
            ZoneTheme theme = LoadOrCreateTheme(i);
            if (theme == null) return;
            themes.Add(theme);
        }

        RemoveMainContent(themes.Select(t => t.zoneName).ToList());

        var stations = new List<(ZoneTheme theme, Terrain terrain, Transform respawn, GameObject exit)>();
        for (int i = 0; i < themes.Count; i++)
        {
            ZoneGenerator.Generate(themes[i], i == 0, i == 0, i == themes.Count - 1, new int[0]);
            GameObject root = GameObject.Find(themes[i].zoneName);
            (Transform respawn, GameObject exit) = StripArena(root);
            stations.Add((themes[i], root.GetComponentInChildren<Terrain>(), respawn, exit));
        }

        MovePlayer(stations[0].respawn);
        var group = new GameObject(GroundName);
        var centers = new List<Transform>();
        int enemies = 0;
        for (int i = 0; i < stations.Count; i++)
        {
            Transform center = new GameObject($"Station_{i + 1}_{StationNames[i]}").transform;
            center.SetParent(group.transform, false);
            center.position = stations[i].theme.origin;
            centers.Add(center);
            enemies += Populate(i, center, stations[i].terrain, dummyPrefab, sparPrefab);
        }

        bool shrine = PlaceShrine(stations[3].theme.origin + new Vector3(-5f, 0f, 9f), stations[3].theme.origin, stations[3].terrain);
        PlaceDirector(centers, stations.Select(s => s.exit).ToList());
        PlaceExit(centers[3], stations[3].exit); // [변경]
        EnsureRecordingMode();
        AddToBuildSettings();
        int hudParts = SyncHudFromMainScene(scene); // [변경]

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Tutorial] 완료 · 수련터 {stations.Count}곳 · 수련 상대 {enemies}명 · 화톳불 {(shrine ? "배치" : "없음 (Skill Tree + Shrines 먼저)")} · HUD 복사 {hudParts}개 → {ScenePath} 저장됨");
    }

    // [변경] 지형은 그대로 두고 출구만 놓을 때 (진행기의 마지막 수련터 기준)
    [MenuItem("Tools/Setup/Tutorial Exit")]
    static void ExitMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TutorialDirector director = Object.FindAnyObjectByType<TutorialDirector>(FindObjectsInactive.Include);
        SerializedProperty list = director != null ? new SerializedObject(director).FindProperty("stations") : null;
        if (list == null || list.arraySize == 0)
        {
            Debug.LogError("[Tutorial] 진행기(수련터 목록)가 없습니다. Tools/Setup/Tutorial Scene을 먼저 실행하세요.");
            return;
        }
        SerializedProperty last = list.GetArrayElementAtIndex(list.arraySize - 1);
        var center = last.FindPropertyRelative("center").objectReferenceValue as Transform;
        var barrier = last.FindPropertyRelative("exitBarrier").objectReferenceValue as GameObject;
        if (center == null) return;
        PlaceExit(center, barrier);
        AddToBuildSettings();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Tutorial] 출구 배치 (→ SampleScene) · {ScenePath} 저장됨");
    }

    // [변경] 지형은 그대로 두고 HUD만 맞출 때
    [MenuItem("Tools/Setup/Sync Tutorial HUD")]
    static void SyncHudMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int copied = SyncHudFromMainScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Tutorial] 본편 HUD에서 {copied}개 복사 → {ScenePath} 저장됨");
    }

    // 핵심: 본편 씬 HUD의 자식 중 수련장 HUD에 없는 것만 같은 순서 자리에 복사.
    // 복사본이 본편 씬 오브젝트를 가리키면(씬을 넘는 참조는 저장 안 됨) 수련장 씬의 같은 종류 컴포넌트로 바꿔 연결
    static int SyncHudFromMainScene(Scene target)
    {
        Transform targetHud = FindRoot(target, "HUD");
        if (targetHud == null) return 0;

        Scene source = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
        int copied = 0;
        try
        {
            Transform sourceHud = FindRoot(source, "HUD");
            if (sourceHud == null) return 0;
            for (int i = 0; i < sourceHud.childCount; i++)
            {
                Transform child = sourceHud.GetChild(i);
                if (targetHud.Find(child.name) != null) continue;
                GameObject copy = Object.Instantiate(child.gameObject, targetHud, false);
                copy.name = child.name;
                copy.transform.SetSiblingIndex(Mathf.Min(i, targetHud.childCount - 1));
                RemapSceneReferences(copy, source, target);
                copied++;
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(source, true);
        }
        return copied;
    }

    static void RemapSceneReferences(GameObject copy, Scene source, Scene target)
    {
        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue;
            var so = new SerializedObject(behaviour);
            SerializedProperty p = so.GetIterator();
            bool changed = false;
            while (p.Next(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null) continue;
                Object o = p.objectReferenceValue;
                Scene scene = o is Component c ? c.gameObject.scene : o is GameObject g ? g.scene : default(Scene);
                if (!scene.IsValid() || scene != source) continue;

                if (o is Component component) p.objectReferenceValue = FindInScene(target, component.GetType());
                else
                {
                    Transform found = FindDeep(target, ((GameObject)o).name);
                    p.objectReferenceValue = found != null ? found.gameObject : null;
                }
                changed = true;
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static Transform FindRoot(Scene scene, string name)
    {
        GameObject go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
        return go != null ? go.transform : null;
    }

    static Component FindInScene(Scene scene, System.Type type)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Component c = root.GetComponentInChildren(type, true);
            if (c != null) return c;
        }
        return null;
    }

    static Transform FindDeep(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
        return null;
    }

    static ZoneTheme LoadOrCreateTheme(int i)
    {
        string path = $"{ThemeFolder}/Tutorial_Station{i + 1}.asset";
        var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>(path);
        if (theme != null) return theme;

        var source = AssetDatabase.LoadAssetAtPath<ZoneTheme>(SourceThemePath);
        if (source == null)
        {
            Debug.LogError("[Tutorial] 구역 1 테마가 없습니다. Tools/Map/Generate All Zones를 먼저 실행하세요.");
            return null;
        }
        theme = Object.Instantiate(source);
        theme.zoneName = $"Tutorial_{i + 1}";
        theme.seed = source.seed + 101 + i;
        theme.origin = Vector3.forward * (StationTerrain * i);
        theme.displayNumber = "수련";
        theme.displayName = StationNames[i];
        theme.terrainSize = StationTerrain;
        theme.combatRadius = StationCombat;
        theme.wallRadius = StationWall;
        theme.hillWidth = StationHill;
        theme.innerRockCount = Mathf.Min(theme.innerRockCount, 4);
        theme.treeMaxCount = Mathf.Min(theme.treeMaxCount, 110);
        theme.arenaCoverCount = Mathf.Min(theme.arenaCoverCount, 16);
        theme.outerCoverCount = Mathf.Min(theme.outerCoverCount, 50);
        AssetDatabase.CreateAsset(theme, path);
        AssetDatabase.SaveAssets();
        return theme;
    }

    // 본편 구역(ZoneArena가 붙은 루트) · 예전 한 칸 수련장 · 화톳불 · 미리 둔 적 · 예전 배치를 지움. 수련터 루트는 남김 (Generate가 갱신)
    static void RemoveMainContent(List<string> keepRoots)
    {
        foreach (ZoneArena arena in Object.FindObjectsByType<ZoneArena>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (arena != null && !keepRoots.Contains(arena.gameObject.name)) Object.DestroyImmediate(arena.gameObject);

        DestroyByName(OldDojoName);
        DestroyByName(GroundName);
        DestroyByName(ShrineGroupName);
        DestroyByName(ExitName); // [변경]

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

    // 웨이브·사망 처리는 쓰지 않음 → ZoneArena를 떼고, 입구 결계는 열고 출구 결계는 닫아 둠 (TutorialDirector가 엶)
    static (Transform respawn, GameObject exit) StripArena(GameObject root)
    {
        Transform respawn = null;
        GameObject exit = null;
        ZoneArena arena = root.GetComponent<ZoneArena>();
        if (arena != null)
        {
            var so = new SerializedObject(arena);
            respawn = so.FindProperty("respawnPoint").objectReferenceValue as Transform;
            var entrance = so.FindProperty("entranceBarrier").objectReferenceValue as GameObject;
            exit = so.FindProperty("exitBarrier").objectReferenceValue as GameObject;
            if (entrance != null) entrance.SetActive(false);
            if (exit != null) exit.SetActive(true);
            Object.DestroyImmediate(arena);
        }
        if (respawn == null) respawn = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RespawnPoint");
        return (respawn, exit);
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

    // 핵심: 수련터마다 배울 것에 맞춰 상대 배치. 입구(남쪽)에서 들어오는 플레이어를 바라보게
    static int Populate(int station, Transform center, Terrain terrain, GameObject dummy, GameObject spar)
    {
        Vector3 c = center.position;
        Vector3 entrance = c - Vector3.forward * 20f;
        switch (station)
        {
            case 0: // 기본기: 허수아비 2
                Spawn(dummy, center, terrain, c + new Vector3(-2.2f, 0f, 3.5f), entrance, true, 0f, "Dummy_1");
                Spawn(dummy, center, terrain, c + new Vector3(2.2f, 0f, 3.5f), entrance, true, 0f, "Dummy_2");
                return 2;
            case 1: // 방어: 수련 상대 1 (수련터 안 어디서든 싸우도록 넓게 묶음)
                Spawn(spar, center, terrain, c + new Vector3(0f, 0f, 4f), entrance, false, StationWall - 3f, "Spar");
                return 1;
            case 2: // 벽력일섬: 허수아비 4 (발도 자세 표식 연습)
                for (int i = 0; i < 4; i++)
                {
                    float x = -4.5f + i * 3f;
                    float z = 4f + (i == 1 || i == 2 ? 1.5f : 0f);
                    Spawn(dummy, center, terrain, c + new Vector3(x, 0f, z), entrance, true, 0f, $"Dummy_{i + 1}");
                }
                return 4;
            default: // 각성 · 처형: 허수아비 3 (각성 낙뢰) + 수련 상대 1 (락온 · 패링 → 처형)
                for (int i = 0; i < 3; i++)
                    Spawn(dummy, center, terrain, c + new Vector3(-6f + i * 1.8f, 0f, 3.5f + (i == 1 ? 1.2f : 0f)), entrance, true, 0f, $"Dummy_{i + 1}");
                Spawn(spar, center, terrain, c + new Vector3(5f, 0f, 3.5f), entrance, false, 6f, "Spar");
                return 4;
        }
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

    static bool PlaceShrine(Vector3 pos, Vector3 lookAt, Terrain terrain)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShrinePrefabPath);
        if (prefab == null) return false;

        var group = new GameObject(ShrineGroupName);
        pos.y = GroundY(terrain, pos);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
        Vector3 face = lookAt - pos;
        face.y = 0f;
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face, Vector3.up));
        go.name = "Shrine (Tutorial)";

        if (Object.FindAnyObjectByType<SkillTreeMenu>(FindObjectsInactive.Include) == null)
            Debug.LogWarning("[Tutorial] 씬에 SkillTreeMenu가 없습니다. Tools/Setup/Skill Tree + Shrines를 이 씬에서 한 번 실행하세요.");
        return true;
    }

    // 진행기는 처음 만들 때만 기본 단계를 넣음 (다시 실행해도 인스펙터에서 고친 단계 문구는 유지, 수련터 연결만 갱신)
    static void PlaceDirector(List<Transform> centers, List<GameObject> exits)
    {
        TutorialDirector director = Object.FindAnyObjectByType<TutorialDirector>(FindObjectsInactive.Include);
        if (director == null) director = new GameObject(DirectorName).AddComponent<TutorialDirector>();

        var so = new SerializedObject(director);
        SerializedProperty list = so.FindProperty("stations");
        list.arraySize = centers.Count;
        for (int i = 0; i < centers.Count; i++)
        {
            SerializedProperty s = list.GetArrayElementAtIndex(i);
            s.FindPropertyRelative("name").stringValue = StationNames[i];
            s.FindPropertyRelative("center").objectReferenceValue = centers[i];
            s.FindPropertyRelative("radius").floatValue = StationWall;
            s.FindPropertyRelative("exitBarrier").objectReferenceValue = exits[i];
        }
        so.FindProperty("titleFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
        so.FindProperty("bodyFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // [변경] 마지막 수련터의 출구 결계 바깥쪽(중심→결계 방향으로 6m). 본편 씬이 빌드 설정에 있어야 불러올 수 있음
    static void PlaceExit(Transform center, GameObject exitBarrier)
    {
        DestroyByName(ExitName);
        Vector3 pos = center.position + Vector3.forward * (StationWall + 8f);
        if (exitBarrier != null)
        {
            Vector3 dir = exitBarrier.transform.position - center.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) pos = exitBarrier.transform.position + dir.normalized * 6f;
        }
        pos.y = GroundY(TerrainAt(pos), pos);
        var go = new GameObject(ExitName);
        go.transform.position = pos;
        var exit = go.AddComponent<SceneExit>();
        var so = new SerializedObject(exit);
        so.FindProperty("sceneName").stringValue = System.IO.Path.GetFileNameWithoutExtension(SourceScenePath);
        so.ApplyModifiedPropertiesWithoutUndo();

        var light = new GameObject("Glow").AddComponent<Light>(); // 길 끝을 밝혀 출구가 보이게
        light.transform.SetParent(go.transform, false);
        light.transform.localPosition = Vector3.up * 2.5f;
        light.type = LightType.Point;
        light.color = new Color(0.55f, 0.8f, 1f);
        light.range = 9f;
        light.intensity = 4f;
        light.shadows = LightShadows.None;
    }

    static void EnsureRecordingMode()
    {
        if (Object.FindAnyObjectByType<RecordingMode>(FindObjectsInactive.Include) != null) return;
        new GameObject("RecordingMode").AddComponent<RecordingMode>();
    }

    static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        bool changed = false;
        foreach (string path in new[] { ScenePath, SourceScenePath }) // [변경] 출구가 불러올 본편 씬도
        {
            if (scenes.Any(s => s.path == path)) continue;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            changed = true;
        }
        if (changed) EditorBuildSettings.scenes = scenes.ToArray();
    }

    static Terrain TerrainAt(Vector3 pos)
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            Vector3 local = pos - t.transform.position;
            Vector3 size = t.terrainData.size;
            if (local.x >= 0f && local.z >= 0f && local.x <= size.x && local.z <= size.z) return t;
        }
        return null;
    }

    static float GroundY(Terrain terrain, Vector3 pos) =>
        terrain != null ? terrain.SampleHeight(pos) + terrain.transform.position.y : pos.y;
}
