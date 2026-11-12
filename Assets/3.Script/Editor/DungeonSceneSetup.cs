using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 던전 "뇌운 협곡" 만들기 (다시 실행해도 됨)
//  Tools/Setup/Dungeon Scene
//   1) 처음 한 번: SampleScene을 복사해 Dungeon.unity로 (플레이어 · 카메라 · HUD · 메뉴 그대로)
//   2) 본편 구역을 지우고 밤 협곡 구역 4개 생성: 가운데 허브(남 · 북 · 동 · 서 문) + 서 · 동 · 북 갈래 방(허브 쪽 문 하나)
//   3) 갈래 방 웨이브: 서 = 혼전(방패병 · 돌격병 섞임) · 동 = 정예 결투 · 북 = 버티기(4웨이브). 이미 있으면 유지
//   4) 허브: 동 · 서 · 북 결계를 하나로 묶어 전투 중에만 닫음, 남쪽 결계는 수문장을 이기면 열림 → 길 끝 출구에서 본편 보스 구역 입구로
//   5) 허브 남서쪽 화톳불 · DungeonDirector · 빌드 설정
//  Tools/Setup/Dungeon Entrance (Main)
//   본편(SampleScene) 폐사찰 북쪽 길에 던전 입구(한 판에 한 번)를 둠
public static class DungeonSceneSetup
{
    const string ScenePath = "Assets/1.Scene/Dungeon.unity";
    const string MainScenePath = "Assets/1.Scene/SampleScene.unity";
    const string ThemeFolder = "Assets/11.Data/Zone";
    const string BaseThemePath = "Assets/11.Data/Zone/Zone2_Forest.asset";
    const string TempleThemePath = "Assets/11.Data/Zone/Zone3_Temple.asset";
    const string PackRoot = "Assets/ThirdParty/Toon Fantasy Nature/";
    const string EnemyPath = "Assets/2.Model/Prefabs/Swordsman.prefab";
    const string ElitePath = "Assets/2.Model/Prefabs/Swordsman_Elite.prefab";
    const string GuardianPath = "Assets/2.Model/Prefabs/Swordsman_Guardian.prefab";
    const string ChargerPath = "Assets/2.Model/Prefabs/Swordsman_Charger.prefab";
    const string ShrinePath = "Assets/2.Model/Prefabs/Shrine.prefab";
    const string BossSpawnPath = "Zone_4_Boss/Generated/RespawnPoint"; // 본편 보스 구역 입구 밖
    const string EntranceName = "DungeonEntrance";
    const string DirectorName = "Dungeon";
    const string ObjectiveSettingsPath = "Assets/11.Data/Navigation/Nav_Objective.asset"; // [변경] HUD Navigation 셋업이 만든 목표 표식 설정

    const float Size = 110f; // 네 구역 모두 같은 크기여야 가장자리 이음새가 맞음

    // 구역: 파일 이름 · 구역 이름 · 배너 · 위치 · 문(남 북 동 서)
    struct Room
    {
        public string file, zone, title; public Vector3 origin; public bool s, n, e, w;
        public Room(string file, string zone, string title, Vector3 origin, bool s, bool n, bool e, bool w)
        { this.file = file; this.zone = zone; this.title = title; this.origin = origin; this.s = s; this.n = n; this.e = e; this.w = w; }
    }

    static readonly Room[] Rooms =
    {
        new Room("Dungeon_West", "Dungeon_West", "서쪽 봉인 · 혼전", new Vector3(-Size, 0f, 0f), false, false, true, false),
        new Room("Dungeon_East", "Dungeon_East", "동쪽 봉인 · 정예", new Vector3(Size, 0f, 0f), false, false, false, true),
        new Room("Dungeon_North", "Dungeon_North", "북쪽 봉인 · 버티기", new Vector3(0f, 0f, Size), true, false, false, false),
        new Room("Dungeon_Hub", "Dungeon_Hub", "뇌운 협곡", Vector3.zero, true, true, true, true), // 마지막 = 씬 기본 분위기
    };

    [MenuItem("Tools/Setup/Dungeon Scene")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        GameObject enemy = Load<GameObject>(EnemyPath), elite = Load<GameObject>(ElitePath);
        GameObject guardian = Load<GameObject>(GuardianPath), charger = Load<GameObject>(ChargerPath);
        if (enemy == null || elite == null || guardian == null || charger == null)
        {
            Debug.LogError("[Dungeon] 검병 · 정예 · 방패병 · 돌격병 프리팹이 필요합니다.");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(MainScenePath, ScenePath))
        {
            Debug.LogError("[Dungeon] SampleScene을 복사하지 못했습니다.");
            return;
        }
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var themes = new List<ZoneTheme>();
        foreach (Room room in Rooms)
        {
            ZoneTheme theme = LoadOrCreateTheme(room);
            if (theme == null) return;
            themes.Add(theme);
        }

        // 본편 구역 · 적 · 화톳불 · 본편 던전 입구 정리 (던전 구역 루트는 남겨 두고 Generate가 갱신 → 웨이브 조절값 유지)
        var keep = themes.Select(t => t.zoneName).ToList();
        foreach (ZoneArena a in Object.FindObjectsByType<ZoneArena>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (a != null && !keep.Contains(a.gameObject.name)) Object.DestroyImmediate(a.gameObject);
        foreach (EnemySwordsman e in Object.FindObjectsByType<EnemySwordsman>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (e != null) Object.DestroyImmediate(OutermostRoot(e.gameObject));
        foreach (string name in new[] { DirectorName, "DungeonShrines", "Shrines", EntranceName, TutorialSceneSetup.GroundName })
            DestroyRoot(name);

        var arenas = new List<ZoneArena>();
        for (int i = 0; i < themes.Count; i++)
        {
            bool hub = i == themes.Count - 1;
            ZoneGenerator.Generate(themes[i], hub, hub, false, null); // 허브만 남쪽 길 끝을 막음(출구 쪽)
            arenas.Add(GameObject.Find(themes[i].zoneName).GetComponent<ZoneArena>());
        }

        ZoneArena west = arenas[0], east = arenas[1], north = arenas[2], hubArena = arenas[3];
        FillWaves(west, new[]
        {
            W(enemy, 2, null, 0, guardian, charger),
            W(enemy, 2, elite, 1, guardian, charger),
        });
        FillWaves(east, new[]
        {
            W(enemy, 1, elite, 1),
            W(enemy, 1, elite, 2),
        });
        FillWaves(north, new[]
        {
            W(enemy, 3, null, 0),
            W(enemy, 3, null, 0, charger),
            W(enemy, 3, null, 0, guardian),
            W(enemy, 3, elite, 1),
        });
        FillWaves(hubArena, new[] { W(enemy, 1, elite, 1) }); // 정예 1 = 수문장 (DungeonDirector가 배율 · 이름)
        Transform hubRoot = hubArena.transform;
        GameObject seal = SetupHubBarriers(hubArena);

        Terrain hubTerrain = hubRoot.GetComponentInChildren<Terrain>();
        Vector3 c = themes[3].origin;
        MovePlayer(c + new Vector3(0f, 0f, -9f), hubTerrain);
        PlaceShrine(c + new Vector3(-8f, 0f, -7f), c, hubTerrain);
        GameObject exit = PlaceExit(hubRoot, themes[3], hubTerrain);
        PlaceDirector(new List<ZoneArena> { west, east, north }, hubArena, exit, elite);
        TutorialSceneSetup.EnsureRecordingMode();
        AddToBuildSettings(ScenePath);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Dungeon] 완료 · 허브 + 갈래 3 · 결계 묶음 {(seal != null ? seal.transform.childCount : 0)}개 → {ScenePath}");
    }

    // 본편 폐사찰 북쪽 길(보스 구역 가기 전)에 던전 입구. 던전을 깨고 돌아오면 꺼짐
    [MenuItem("Tools/Setup/Dungeon Entrance (Main)")]
    static void PlaceEntrance()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var temple = Load<ZoneTheme>(TempleThemePath);
        if (temple == null) return;
        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        DestroyRoot(EntranceName);

        Vector3 pos = temple.origin + Vector3.forward * (temple.wallRadius + 9f);
        pos.y = GroundY(pos);
        var go = new GameObject(EntranceName);
        go.transform.position = pos;
        var so = new SerializedObject(go.AddComponent<SceneExit>());
        so.FindProperty("sceneName").stringValue = "Dungeon";
        so.FindProperty("skipIfFlag").stringValue = DungeonDirector.ClearedFlag;
        so.ApplyModifiedPropertiesWithoutUndo();
        AddGlow(go.transform, new Color(0.6f, 0.55f, 1f));

        if (GameObject.Find(BossSpawnPath) == null)
            Debug.LogWarning($"[Dungeon] 본편에 {BossSpawnPath}가 없습니다. 던전에서 돌아올 위치를 찾지 못하면 씬 시작 위치로 돌아옵니다.");
        AddToBuildSettings(ScenePath);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Dungeon] 본편 던전 입구 배치 ({pos}) → {MainScenePath}");
    }

    // ===== 테마 =====
    // 밤 협곡: 밤 하늘 · 짙은 푸른 안개 · 차가운 달빛, 높고 가파른 언덕 · 소나무, 보랏빛 결계. 이미 있으면 그대로(인스펙터 값 유지)
    static ZoneTheme LoadOrCreateTheme(Room room)
    {
        string path = $"{ThemeFolder}/{room.file}.asset";
        var theme = Load<ZoneTheme>(path);
        if (theme == null)
        {
            var source = Load<ZoneTheme>(BaseThemePath);
            if (source == null)
            {
                Debug.LogError("[Dungeon] 구역 2 테마가 없습니다. Tools/Map/Generate All Zones를 먼저 실행하세요.");
                return null;
            }
            theme = Object.Instantiate(source);
            theme.seed = source.seed + 300 + room.zone.Length * 7;
            theme.terrainSize = Size;
            theme.combatRadius = 15f;
            theme.wallRadius = 19f;
            theme.hillHeight = 15f;
            theme.hillWidth = 14f;
            theme.groundLayer = Load<TerrainLayer>(PackRoot + "Terrain/Layer_TFF_Terrain_Earth_2A_D.terrainlayer");
            theme.arenaLayer = Load<TerrainLayer>(PackRoot + "Terrain/Layer_TFF_Terrain_Dirt_1A_D.terrainlayer");
            theme.roadLayer = Load<TerrainLayer>(PackRoot + "Terrain/Layer_TFF_Terrain_Dirt_Road_1A_D.terrainlayer");
            theme.skybox = Load<Material>(PackRoot + "Skyboxes/TFF_Skybox_Night_01A.mat");
            theme.fog = true;
            theme.fogColor = new Color(0.1f, 0.13f, 0.22f);
            theme.fogDensity = 0.016f;
            theme.sunColor = new Color(0.45f, 0.55f, 0.85f);
            theme.sunEuler = new Vector3(35f, 60f, 0f);
            theme.barrierColor = new Color(0.6f, 0.55f, 1f, 0.35f);
            var fireflies = Load<GameObject>(PackRoot + "Particles/TFF_Fireflies_01A.prefab");
            var fog = Load<GameObject>(PackRoot + "Particles/TFF_Fog_01A.prefab");
            theme.groundParticles = new[] { fog, fireflies }.Where(p => p != null).ToArray();
            theme.overheadParticles = new GameObject[0];
            AssetDatabase.CreateAsset(theme, path);
        }
        // 위치 · 문 · 이름은 매번 맞춤 (배치가 바뀌면 이음새가 어긋나므로)
        theme.zoneName = room.zone;
        theme.origin = room.origin;
        theme.displayNumber = "던전";
        theme.displayName = room.title;
        theme.gateSouth = room.s;
        theme.gateNorth = room.n;
        theme.gateEast = room.e;
        theme.gateWest = room.w;
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        return theme;
    }

    // ===== 구역 설정 =====
    struct WaveDef
    {
        public GameObject enemy, elite; public int count, eliteCount; public GameObject[] extras;
    }

    static WaveDef W(GameObject enemy, int count, GameObject elite, int eliteCount, params GameObject[] extras) =>
        new WaveDef { enemy = enemy, count = count, elite = elite, eliteCount = eliteCount, extras = extras };

    // 웨이브는 인스펙터에서 조절하는 값이라 비어 있을 때만 넣음
    static void FillWaves(ZoneArena arena, WaveDef[] waves)
    {
        var so = new SerializedObject(arena);
        SerializedProperty list = so.FindProperty("waves");
        if (list.arraySize > 0) return;
        list.arraySize = waves.Length;
        for (int i = 0; i < waves.Length; i++)
        {
            SerializedProperty w = list.GetArrayElementAtIndex(i);
            w.FindPropertyRelative("enemyPrefab").objectReferenceValue = waves[i].enemy;
            w.FindPropertyRelative("count").intValue = waves[i].count;
            w.FindPropertyRelative("elitePrefab").objectReferenceValue = waves[i].elite;
            w.FindPropertyRelative("eliteCount").intValue = waves[i].eliteCount;
            SerializedProperty extras = w.FindPropertyRelative("extras");
            extras.arraySize = waves[i].extras.Length;
            for (int k = 0; k < waves[i].extras.Length; k++)
            {
                SerializedProperty g = extras.GetArrayElementAtIndex(k);
                g.FindPropertyRelative("prefab").objectReferenceValue = waves[i].extras[k];
                g.FindPropertyRelative("count").intValue = 1;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 핵심: 허브의 동 · 서 · 북 결계를 한 오브젝트(HubSeal) 아래로 모아 ZoneArena 입구 결계로 씀 → 수문장 전투 중에만 닫힘
    //       남쪽 결계는 출구 결계 → 처음엔 닫혀 있다가 수문장을 이기면 열림. 쓰러지면 허브 안에서 바로 재도전
    static GameObject SetupHubBarriers(ZoneArena hub)
    {
        Transform generated = hub.transform.Find("Generated");
        if (generated == null) return null;
        var seal = new GameObject("HubSeal");
        seal.transform.SetParent(generated, false);
        foreach (string gate in new[] { "Gate_Exit", "Gate_East", "Gate_West" })
        {
            Transform barrier = generated.Find(gate + "/Barrier");
            if (barrier != null) barrier.SetParent(seal.transform, true);
        }
        seal.SetActive(false);
        Transform south = generated.Find("Gate_Entrance/Barrier");
        if (south != null) south.gameObject.SetActive(true);

        var so = new SerializedObject(hub);
        so.FindProperty("entranceBarrier").objectReferenceValue = seal;
        so.FindProperty("exitBarrier").objectReferenceValue = south != null ? south.gameObject : null;
        so.FindProperty("quickRetry").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        return seal;
    }

    // 허브 남쪽 결계 바깥 길 끝 → 본편 보스 구역 입구로. 처음엔 꺼 둠(DungeonDirector가 켬)
    static GameObject PlaceExit(Transform hubRoot, ZoneTheme t, Terrain terrain)
    {
        Transform old = hubRoot.Find("DungeonExit");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Vector3 pos = t.origin + Vector3.back * (t.wallRadius + 9f);
        pos.y = GroundY(pos);
        var go = new GameObject("DungeonExit");
        go.transform.SetParent(hubRoot, true);
        go.transform.position = pos;
        var so = new SerializedObject(go.AddComponent<SceneExit>());
        so.FindProperty("sceneName").stringValue = System.IO.Path.GetFileNameWithoutExtension(MainScenePath);
        so.FindProperty("spawnAt").stringValue = BossSpawnPath;
        so.ApplyModifiedPropertiesWithoutUndo();
        AddGlow(go.transform, new Color(0.55f, 0.8f, 1f));
        return go;
    }

    static void PlaceDirector(List<ZoneArena> branches, ZoneArena hub, GameObject exit, GameObject elite)
    {
        var director = new GameObject(DirectorName).AddComponent<DungeonDirector>();
        var so = new SerializedObject(director);
        SerializedProperty list = so.FindProperty("branches");
        list.arraySize = branches.Count;
        for (int i = 0; i < branches.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = branches[i];
        so.FindProperty("hub").objectReferenceValue = hub;
        so.FindProperty("exitMarker").objectReferenceValue = exit;
        so.FindProperty("miniBossPrefab").objectReferenceValue = elite;
        so.FindProperty("font").objectReferenceValue = Load<Font>(TutorialSceneSetup.TitleFontPath);
        so.FindProperty("markerSettings").objectReferenceValue = Load<ScriptableObject>(ObjectiveSettingsPath); // [변경]
        so.ApplyModifiedPropertiesWithoutUndo();
        if (Load<ScriptableObject>(ObjectiveSettingsPath) == null)
            Debug.LogWarning($"[Dungeon] {ObjectiveSettingsPath}가 없어 나침반 목표 표식은 빠집니다 (오른쪽 위 목표 · 거리는 표시됨). Tools/Setup/HUD Navigation을 먼저 실행하세요.");
    }

    static void PlaceShrine(Vector3 pos, Vector3 lookAt, Terrain terrain)
    {
        var prefab = Load<GameObject>(ShrinePath);
        if (prefab == null) return;
        var group = new GameObject("DungeonShrines");
        pos.y = TutorialSceneSetup.GroundY(terrain, pos);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
        Vector3 face = lookAt - pos;
        face.y = 0f;
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face, Vector3.up));
        go.name = "Shrine (Dungeon)";
    }

    static void MovePlayer(Vector3 pos, Terrain terrain)
    {
        pos.y = TutorialSceneSetup.GroundY(terrain, pos) + 0.1f;
        var start = new GameObject("DungeonStart").transform;
        start.SetPositionAndRotation(pos, Quaternion.identity);
        TutorialSceneSetup.MovePlayer(start);
        Object.DestroyImmediate(start.gameObject);
    }

    // ===== 공용 =====
    static void AddGlow(Transform parent, Color color)
    {
        var light = new GameObject("Glow").AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.localPosition = Vector3.up * 2.5f;
        light.type = LightType.Point;
        light.color = color;
        light.range = 9f;
        light.intensity = 4f;
        light.shadows = LightShadows.None;
    }

    static float GroundY(Vector3 pos)
    {
        return Physics.Raycast(pos + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 120f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point.y : pos.y;
    }

    static GameObject OutermostRoot(GameObject go)
    {
        GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        return root != null ? root : go;
    }

    static void DestroyRoot(string name)
    {
        GameObject go = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name);
        if (go != null) Object.DestroyImmediate(go);
    }

    static void AddToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
}
