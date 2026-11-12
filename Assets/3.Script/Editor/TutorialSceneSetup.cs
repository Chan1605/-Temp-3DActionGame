using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 수련장(튜토리얼) 씬 만들기 — 한 구역형 (다시 실행해도 됨)
//  1) 처음 한 번: SampleScene을 복사해 Tutorial.unity로 (플레이어·카메라·HUD·메뉴 구성을 그대로 씀)
//  2) 본편 구역·화톳불·배치된 적·예전 수련터 4곳을 지우고, 수련장 하나 생성 (전투 구역 기능은 뗌)
//     겉모습은 본편 산문(낮 · 소나무 · 풀밭)과 다르게: 노을 · 사시나무/자작나무 · 모래 마당 · 정자 · 반딧불
//  3) 상대 무리 3개(허수아비 2 · 수련 상대 · 허수아비 4)를 미리 두고, TutorialDirector가 단계마다 필요한 무리만 꺼냄
//  4) 북쪽 출구 결계는 닫아 두고 모든 단계를 끝내면 엶 → 길 끝 출구에서 본편(산문)으로
//  5) 녹화 모드(F10), 빌드 설정에 씬 추가
//  6) 본편 HUD에만 있는 표시(처형 F · 락온 · 데미지 숫자 · 레벨업 등)를 복사해 옴 — Tools/Setup/Sync Tutorial HUD로 따로도 실행 가능
public static class TutorialSceneSetup // [변경] 자유 연습장 셋업(TrainingSceneSetup)이 지형·배치 도우미를 같이 씀 (internal)
{
    internal const string SourceScenePath = "Assets/1.Scene/SampleScene.unity";
    const string ScenePath = "Assets/1.Scene/Tutorial.unity";
    const string SourceThemePath = "Assets/11.Data/Zone/Zone1_Sanmun.asset";
    const string ArenaThemePath = "Assets/11.Data/Zone/Tutorial_Yard.asset"; // 새 겉모습 테마 (예전 Tutorial_Arena.asset은 안 씀)
    const string PackRoot = "Assets/ThirdParty/Toon Fantasy Nature/";
    internal const string DummyPrefabPath = "Assets/2.Model/Prefabs/Swordsman.prefab";
    internal const string SparPrefabPath = "Assets/2.Model/Prefabs/Swordsman_Trainer.prefab";
    const string ShrinePrefabPath = "Assets/2.Model/Prefabs/Shrine.prefab";
    internal const string TitleFontPath = "Assets/9.Font/public/static/Pretendard-SemiBold.otf";
    internal const string BodyFontPath = "Assets/9.Font/public/static/Pretendard-Regular.otf";
    internal const string GroundName = "TrainingGround";
    const string ShrineGroupName = "Shrines";
    const string DirectorName = "Tutorial";
    const string ExitName = "TutorialExit";
    const string OldDojoName = "Tutorial_Dojo"; // 예전 한 칸짜리 수련장

    // 수련장 크기: 본편 구역과 비슷하게 (상대 무리 · 화톳불이 한 곳에 들어가도록), 북쪽으로 출구 길
    const float ArenaTerrain = 110f, ArenaCombat = 15f, ArenaWall = 19f, ArenaHill = 18f;
    static readonly string[] OldStationRoots = { "Tutorial_1", "Tutorial_2", "Tutorial_3", "Tutorial_4" };

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

        // 수련장 하나: 남쪽은 막고 북쪽은 출구 길을 냄
        ZoneTheme theme = LoadOrCreateTheme();
        if (theme == null) return;
        RemoveMainContent(theme.zoneName);
        ZoneGenerator.Generate(theme, true, true, false, new int[0]);
        GameObject root = GameObject.Find(theme.zoneName);
        (Transform respawn, GameObject exit) = StripArena(root);
        Terrain terrain = root.GetComponentInChildren<Terrain>();

        MovePlayer(respawn);
        var group = new GameObject(GroundName);
        Transform center = new GameObject("Center").transform;
        center.SetParent(group.transform, false);
        center.position = theme.origin;
        List<GameObject> lineups = BuildLineups(center, terrain, dummyPrefab, sparPrefab, out int enemies);

        bool shrine = PlaceShrine(theme.origin + new Vector3(-9f, 0f, -4f), theme.origin, terrain);
        PlaceDirector(center, exit, lineups);
        PlaceExit(center, exit);
        EnsureRecordingMode();
        AddToBuildSettings();
        int hudParts = SyncHudFromMainScene(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Tutorial] 완료 · 수련장 1곳 · 상대 무리 {lineups.Count}개({enemies}명) · 화톳불 {(shrine ? "배치" : "없음 (Skill Tree + Shrines 먼저)")} · HUD 복사 {hudParts}개 → {ScenePath} 저장됨");
    }

    // 지형은 그대로 두고 출구만 놓을 때
    [MenuItem("Tools/Setup/Tutorial Exit")]
    static void ExitMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TutorialDirector director = Object.FindAnyObjectByType<TutorialDirector>(FindObjectsInactive.Include);
        var so = director != null ? new SerializedObject(director) : null; // 수련장 하나의 가운데 · 출구 결계 기준
        var center = so != null ? so.FindProperty("center").objectReferenceValue as Transform : null;
        var barrier = so != null ? so.FindProperty("exitBarrier").objectReferenceValue as GameObject : null;
        if (center == null)
        {
            Debug.LogError("[Tutorial] 진행기(가운데 지점)가 없습니다. Tools/Setup/Tutorial Scene을 먼저 실행하세요.");
            return;
        }
        PlaceExit(center, barrier);
        AddToBuildSettings();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Tutorial] 출구 배치 (→ SampleScene) · {ScenePath} 저장됨");
    }

    // 지형은 그대로 두고 HUD만 맞출 때
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

    // 수련장 테마 하나 (구역 1 복사 + 수련장 겉모습). 이미 있으면 그대로 써서 인스펙터에서 고친 값 유지
    internal static ZoneTheme LoadOrCreateTheme()
    {
        var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>(ArenaThemePath);
        if (theme != null) return theme;

        var source = AssetDatabase.LoadAssetAtPath<ZoneTheme>(SourceThemePath);
        if (source == null)
        {
            Debug.LogError("[Tutorial] 구역 1 테마가 없습니다. Tools/Map/Generate All Zones를 먼저 실행하세요.");
            return null;
        }
        theme = Object.Instantiate(source);
        theme.zoneName = "Tutorial_Arena";
        theme.seed = source.seed + 101;
        theme.origin = Vector3.zero;
        theme.displayNumber = "수련";
        theme.displayName = "수련장";
        theme.terrainSize = ArenaTerrain;
        theme.combatRadius = ArenaCombat;
        theme.wallRadius = ArenaWall;
        theme.hillWidth = ArenaHill;
        theme.innerRockCount = Mathf.Min(theme.innerRockCount, 4);
        theme.treeMaxCount = Mathf.Min(theme.treeMaxCount, 140);
        theme.outerCoverCount = Mathf.Min(theme.outerCoverCount, 60);
        ApplyYardLook(theme);
        AssetDatabase.CreateAsset(theme, ArenaThemePath);
        AssetDatabase.SaveAssets();
        return theme;
    }

    // 핵심: 수련장 겉모습. 바위 벽은 구역 1 그대로, 나머지를 바꿔 본편 첫 구역(산문)과 확실히 구분
    //  노을 하늘 · 낮고 따뜻한 해 · 옅은 주황 안개 / 모래 마당 + 흙 바닥 / 사시나무 · 자작나무 / 정자 4채 / 반딧불 · 낙엽 · 꽃잎
    static void ApplyYardLook(ZoneTheme theme)
    {
        theme.hillHeight = 8f;
        theme.groundLayer = PackLoad<TerrainLayer>("Terrain/Layer_TFF_Terrain_Earth_3A_D.terrainlayer");
        theme.arenaLayer = PackLoad<TerrainLayer>("Terrain/Layer_TFF_Terrain_Sand_1A_D.terrainlayer");
        theme.roadLayer = PackLoad<TerrainLayer>("Terrain/Layer_TFF_Terrain_Dirt_Road_1A_D.terrainlayer");

        theme.trees = PackPrefabs("Vegetation/Trees", "TFF_Aspen_Tree_").Concat(PackPrefabs("Vegetation/Trees", "TFF_Birch_Tree_0")).ToArray();
        theme.treeHeight = new Vector2(8f, 13f);
        theme.arenaCover = PackPrefabs("Vegetation/Plants", "TFF_Flower_Patch_");
        theme.arenaCoverCount = 10;
        theme.outerCover = PackPrefabs("Vegetation/Plants", "TFF_Bush_0").Concat(PackPrefabs("Vegetation/Plants", "TFF_Flower_Patch_"))
            .Concat(PackPrefabs("Vegetation/Plants", "TFF_Reed_")).ToArray();
        theme.landmarks = PackPrefabs("Structures", "TFF_Wooden_Pavilion_");
        theme.landmarkSize = 9f;

        theme.skybox = PackLoad<Material>("Skyboxes/TFF_Skybox_Sunset_01A.mat");
        theme.fog = true;
        theme.fogColor = new Color(0.86f, 0.7f, 0.58f);
        theme.fogDensity = 0.009f;
        theme.sunColor = new Color(1f, 0.74f, 0.52f);
        theme.sunEuler = new Vector3(16f, 235f, 0f);
        theme.groundParticles = new[] { PackLoad<GameObject>("Particles/TFF_Fireflies_01A.prefab") };
        theme.overheadParticles = new[]
        {
            PackLoad<GameObject>("Particles/TFF_Falling_Leaves_01B.prefab"),
            PackLoad<GameObject>("Particles/TFF_Blowing_Flowers_01A.prefab"),
        };
        theme.groundParticles = theme.groundParticles.Where(g => g != null).ToArray();
        theme.overheadParticles = theme.overheadParticles.Where(g => g != null).ToArray();
    }

    static T PackLoad<T>(string relative) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(PackRoot + relative);
        if (asset == null) Debug.LogWarning($"[Tutorial] {PackRoot + relative}를 찾지 못했습니다.");
        return asset;
    }

    static GameObject[] PackPrefabs(string folder, string prefix) =>
        AssetDatabase.FindAssets("t:Prefab", new[] { PackRoot + "Prefabs/" + folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => System.IO.Path.GetFileName(p).StartsWith(prefix))
            .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p))
            .Where(p => p != null)
            .ToArray();

    // 본편 구역(ZoneArena가 붙은 루트) · 예전 수련장 · 화톳불 · 미리 둔 적 · 예전 배치를 지움. 수련장 루트는 남김 (Generate가 갱신)
    internal static void RemoveMainContent(string keepRoot)
    {
        foreach (ZoneArena arena in Object.FindObjectsByType<ZoneArena>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (arena != null && arena.gameObject.name != keepRoot) Object.DestroyImmediate(arena.gameObject);
        foreach (string old in OldStationRoots) DestroyByName(old); // 예전 동선형 수련터 4곳

        DestroyByName(OldDojoName);
        DestroyByName(GroundName);
        DestroyByName(ShrineGroupName);
        DestroyByName(ExitName);

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
    internal static (Transform respawn, GameObject exit) StripArena(GameObject root)
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

    internal static void MovePlayer(Transform respawn)
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

    // 핵심: 상대 무리 3개를 가운데 북쪽(플레이어가 남쪽에서 들어와 바라보는 쪽)에 미리 둠. 번호 = TutorialDirector 기본 단계의 lineup
    //  0 허수아비 2 (기본기 · 락온) · 1 수련 상대 (방어 · 처형) · 2 허수아비 4 (벽력일섬 · 각성). 처음엔 0만 켜 둠
    static List<GameObject> BuildLineups(Transform center, Terrain terrain, GameObject dummy, GameObject spar, out int enemies)
    {
        Vector3 c = center.position;
        Vector3 entrance = c - Vector3.forward * 20f;
        var groups = new List<GameObject>();
        enemies = 0;

        Transform basic = Group(center, "Lineup_0_Basic", groups);
        Spawn(dummy, basic, terrain, c + new Vector3(-2.2f, 0f, 3.5f), entrance, true, 0f, "Dummy_1");
        Spawn(dummy, basic, terrain, c + new Vector3(2.2f, 0f, 3.5f), entrance, true, 0f, "Dummy_2");
        enemies += 2;

        Transform sparGroup = Group(center, "Lineup_1_Spar", groups);
        Spawn(spar, sparGroup, terrain, c + new Vector3(0f, 0f, 5f), entrance, false, ArenaWall - 3f, "Spar");
        enemies += 1;

        Transform line = Group(center, "Lineup_2_Line", groups);
        for (int i = 0; i < 4; i++)
        {
            float x = -4.5f + i * 3f;
            float z = 4f + (i == 1 || i == 2 ? 1.5f : 0f);
            Spawn(dummy, line, terrain, c + new Vector3(x, 0f, z), entrance, true, 0f, $"Dummy_{i + 1}");
        }
        enemies += 4;

        for (int i = 1; i < groups.Count; i++) groups[i].SetActive(false);
        return groups;
    }

    static Transform Group(Transform parent, string name, List<GameObject> list)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.parent, false);
        list.Add(go);
        return go.transform;
    }

    internal static void Spawn(GameObject prefab, Transform parent, Terrain terrain, Vector3 pos, Vector3 lookAt, bool passive, float leash, string name)
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

    // 진행기는 처음 만들 때만 기본 단계를 넣음 (다시 실행해도 인스펙터에서 고친 단계 문구는 유지, 연결만 갱신)
    static void PlaceDirector(Transform center, GameObject exit, List<GameObject> lineups) // 수련장 하나 + 상대 무리
    {
        TutorialDirector director = Object.FindAnyObjectByType<TutorialDirector>(FindObjectsInactive.Include);
        if (director == null) director = new GameObject(DirectorName).AddComponent<TutorialDirector>();

        var so = new SerializedObject(director);
        so.FindProperty("center").objectReferenceValue = center;
        so.FindProperty("exitBarrier").objectReferenceValue = exit;
        SerializedProperty list = so.FindProperty("lineups");
        list.arraySize = lineups.Count;
        for (int i = 0; i < lineups.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = lineups[i];
        so.FindProperty("titleFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
        so.FindProperty("bodyFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 출구 결계 바깥쪽(중심→결계 방향으로 8m). 본편 씬이 빌드 설정에 있어야 불러올 수 있음
    static void PlaceExit(Transform center, GameObject exitBarrier)
    {
        DestroyByName(ExitName);
        Vector3 pos = center.position + Vector3.forward * (ArenaWall + 8f);
        if (exitBarrier != null)
        {
            Vector3 dir = exitBarrier.transform.position - center.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) pos = exitBarrier.transform.position + dir.normalized * 8f;
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

    internal static void EnsureRecordingMode()
    {
        if (Object.FindAnyObjectByType<RecordingMode>(FindObjectsInactive.Include) != null) return;
        new GameObject("RecordingMode").AddComponent<RecordingMode>();
    }

    static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        bool changed = false;
        foreach (string path in new[] { ScenePath, SourceScenePath }) // 출구가 불러올 본편 씬도
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

    internal static float GroundY(Terrain terrain, Vector3 pos) =>
        terrain != null ? terrain.SampleHeight(pos) + terrain.transform.position.y : pos.y;
}
