using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// 규칙 기반 구역 생성: ZoneTheme 값으로 지형·바위 벽·나무·문·분위기를 만든다.
// 생성물은 <구역>/Generated 아래에만 두고 다시 생성할 때 통째로 교체 → 직접 배치한 오브젝트는 <구역> 아래 다른 자식에 두면 유지됨
// 구역들은 한 씬에서 북쪽(+Z)으로 지형 크기만큼 이어 붙임. 입구(남)·출구(북) 길이 그대로 구역 사이 연결 길이 됨
public static class ZoneGenerator
{
    const string DataFolder = "Assets/11.Data/Zone";
    const string Zone1ThemePath = DataFolder + "/Zone1_Sanmun.asset";
    const string Zone2ThemePath = DataFolder + "/Zone2_Forest.asset";
    const string PackRoot = "Assets/ThirdParty/Toon Fantasy Nature/";
    const string MaterialFolder = "Assets/6.Materials";
    const string GeneratedName = "Generated";
    const string EnemyPrefabPath = "Assets/2.Model/Prefabs/Swordsman.prefab"; // 웨이브 기본 적
    // 이웃 구역과 맞닿는 지형 가장자리 높이. 모든 구역이 같은 값을 써야 이음새가 안 생김
    const float SeamHeight = 8f;
    const float SeamBand = 8f;

    // 구역 순서 = 배치 순서 (구역 3·보스는 테마만 추가하면 됨)
    // waves: 웨이브별 검병 수 (기획서 20번: 구역 1은 2웨이브, 2·3은 3웨이브). ZoneArena의 웨이브가 비어 있을 때만 채움
    // number·title: HUD 구역 배너 문구 (테마에 비어 있을 때만 채움)
    static readonly (string path, System.Func<ZoneTheme> create, int[] waves, string number, string title)[] Zones =
    {
        (Zone1ThemePath, CreateZone1Theme, new[] { 2, 3 }, "구역 1", "산문"),
        (Zone2ThemePath, CreateZone2Theme, new[] { 2, 3, 3 }, "구역 2", "안개 낀 숲길"),
    };

    static System.Random rng;
    static Vector3 origin; // 지금 생성 중인 구역의 중심
    static readonly Dictionary<GameObject, Bounds> boundsCache = new Dictionary<GameObject, Bounds>();

    // 구역 메뉴: 전체 / 개별. 어느 쪽이든 먼저 전체 위치를 다시 계산함
    [MenuItem("Tools/Map/Generate All Zones")]
    public static void GenerateAll() // [변경] Polish Pass에서도 호출
    {
        ZoneTheme[] themes = LoadAllThemes();
        if (themes == null) return;
        for (int i = 0; i < themes.Length; i++) Generate(themes[i], i == 0, i == 0, i == themes.Length - 1, Zones[i].waves);
    }

    [MenuItem("Tools/Map/Generate Zone 1 (Sanmun)")]
    static void GenerateZone1() => GenerateSingle(0);

    [MenuItem("Tools/Map/Generate Zone 2 (Forest)")]
    static void GenerateZone2() => GenerateSingle(1);

    [MenuItem("Tools/Map/Clear All Generated")]
    static void ClearAll()
    {
        foreach (var (path, _, _, _, _) in Zones)
        {
            var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>(path);
            GameObject root = theme != null ? GameObject.Find(theme.zoneName) : null;
            Transform generated = root != null ? root.transform.Find(GeneratedName) : null;
            if (generated != null) Undo.DestroyObjectImmediate(generated.gameObject);
        }
    }

    // 씬의 기본 분위기(하늘·안개·해)는 구역 1 것으로 둠. 나머지 구역은 플레이 중 ZoneArena가 다가갈 때 전환
    static void GenerateSingle(int index)
    {
        ZoneTheme[] themes = LoadAllThemes();
        if (themes != null) Generate(themes[index], index == 0, index == 0, index == themes.Length - 1, Zones[index].waves);
    }

    static ZoneTheme[] LoadAllThemes()
    {
        var themes = new ZoneTheme[Zones.Length];
        for (int i = 0; i < Zones.Length; i++)
        {
            themes[i] = AssetDatabase.LoadAssetAtPath<ZoneTheme>(Zones[i].path);
            if (themes[i] == null) themes[i] = Zones[i].create();
            if (themes[i] == null) return null;


            if (string.IsNullOrEmpty(themes[i].displayName))
            {
                themes[i].displayNumber = Zones[i].number;
                themes[i].displayName = Zones[i].title;
                EditorUtility.SetDirty(themes[i]);
            }
        }

        // 앞 구역 지형 끝에 다음 구역 지형이 바로 붙도록 중심을 계산
        for (int i = 1; i < themes.Length; i++)
        {
            ZoneTheme prev = themes[i - 1];
            Vector3 next = prev.origin + Vector3.forward * (prev.terrainSize * 0.5f + themes[i].terrainSize * 0.5f);
            if (themes[i].origin == next) continue;
            themes[i].origin = next;
            EditorUtility.SetDirty(themes[i]);
        }
        AssetDatabase.SaveAssets();
        return themes;
    }

    // capSouth/capNorth: 이웃 구역이 없는 쪽 길 끝만 막음 (구역 사이 길은 열어 둠)
    public static void Generate(ZoneTheme theme, bool applyAtmosphere = true, bool capSouth = true, bool capNorth = true, int[] defaultWaves = null)
    {
        rng = new System.Random(theme.seed);
        boundsCache.Clear();
        origin = theme.origin;

        GameObject root = GameObject.Find(theme.zoneName);
        if (root == null)
        {
            root = new GameObject(theme.zoneName);
            Undo.RegisterCreatedObjectUndo(root, "Create Zone");
        }
        Undo.RecordObject(root.transform, "Zone Origin");
        root.transform.position = origin;

        Transform old = root.transform.Find(GeneratedName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var generated = new GameObject(GeneratedName);
        Undo.RegisterCreatedObjectUndo(generated, "Generate Zone");
        generated.transform.SetParent(root.transform, false);

        // 예전 테스트용 평면 바닥은 지형과 겹치므로 끔 (지우지 않음)
        GameObject oldGround = GameObject.Find("Ground");
        if (oldGround != null && oldGround.transform.root != root.transform)
        {
            Undo.RecordObject(oldGround, "Disable Ground");
            oldGround.SetActive(false);
        }

        Terrain terrain = BuildTerrain(theme, generated.transform);
        BuildInvisibleWall(theme, generated.transform, capSouth, capNorth);
        (GameObject entrance, GameObject exit) = BuildGates(theme, generated.transform, terrain);
        Transform respawn = BuildRespawnPoint(theme, generated.transform, terrain);

        int wallRocks = PlaceWallRocks(theme, Group(generated, "WallRocks"), terrain);
        int innerRocks = PlaceInnerRocks(theme, Group(generated, "InnerRocks"), terrain);
        int trees = PlaceTrees(theme, Group(generated, "Trees"), terrain);
        int cover = PlaceCover(theme, Group(generated, "Cover"), terrain);
        int stones = PlacePathStones(theme, Group(generated, "PathStones"), terrain);
        PlaceParticles(theme, Group(generated, "Particles"), terrain);
        if (applyAtmosphere) ApplyAtmosphere(theme);

        ZoneArena arena = root.GetComponent<ZoneArena>();
        if (arena == null) arena = Undo.AddComponent<ZoneArena>(root);
        var so = new SerializedObject(arena);
        so.FindProperty("radius").floatValue = theme.combatRadius;
        so.FindProperty("entranceBarrier").objectReferenceValue = entrance;
        so.FindProperty("exitBarrier").objectReferenceValue = exit;
        so.FindProperty("theme").objectReferenceValue = theme;
        so.FindProperty("atmosphereRadius").floatValue = theme.wallRadius + 10f;
        so.FindProperty("respawnPoint").objectReferenceValue = respawn;
        FillDefaultWaves(so.FindProperty("waves"), defaultWaves);
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log($"[ZoneGenerator] {theme.zoneName} 완료 · 벽 바위 {wallRocks}, 안쪽 바위 {innerRocks}, 나무 {trees}, 풀·덤불 {cover}, 돌길 {stones} (seed {theme.seed})");
    }

    // ===== 지형 =====
    // 아레나와 문 앞 길은 평평하게, 벽 바깥은 언덕으로 솟아 "산 속 공터" 느낌
    static float Height(ZoneTheme t, float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float flat = t.wallRadius + 2f;
        float rise = Mathf.Clamp01((r - flat) / t.hillWidth);
        rise = rise * rise * (3f - 2f * rise);

        float noise = Mathf.PerlinNoise(x * 0.045f + t.seed * 13.1f, z * 0.045f + t.seed * 7.3f);
        float h = rise * (t.hillHeight + noise * t.hillNoise);

        // 가장자리 SeamBand 구간은 공통 높이로 모아서 이웃 구역 지형과 높이를 맞춤
        float toEdge = t.terrainSize * 0.5f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        h = Mathf.Lerp(SeamHeight, h, Mathf.Clamp01(toEdge / SeamBand));

        float corridor = 1f - Mathf.Clamp01((Mathf.Abs(x) - t.gateHalfWidth) / 6f);
        return h * (1f - corridor);
    }

    static Terrain BuildTerrain(ZoneTheme t, Transform parent)
    {
        if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/11.Data", "Zone");

        string dataPath = $"{DataFolder}/{t.zoneName}_Terrain.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, dataPath);
        }

        const int res = 129;
        float maxHeight = Mathf.Max(t.hillHeight + t.hillNoise, SeamHeight) + 2f;
        data.heightmapResolution = res;
        data.size = new Vector3(t.terrainSize, maxHeight, t.terrainSize);

        float half = t.terrainSize * 0.5f;
        var heights = new float[res, res];
        for (int iz = 0; iz < res; iz++)
        for (int ix = 0; ix < res; ix++)
        {
            float x = ix / (float)(res - 1) * t.terrainSize - half;
            float z = iz / (float)(res - 1) * t.terrainSize - half;
            heights[iz, ix] = Height(t, x, z) / maxHeight;
        }
        data.SetHeights(0, 0, heights);

        PaintLayers(t, data);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        GameObject go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(parent, false);
        go.transform.position = origin + new Vector3(-half, 0f, -half);
        return go.GetComponent<Terrain>();
    }

    // 레이어 0 풀, 1 아레나 흙 마당, 2 문 앞 길
    static void PaintLayers(ZoneTheme t, TerrainData data)
    {
        var layers = new[] { t.groundLayer, t.arenaLayer, t.roadLayer };
        if (layers.Any(l => l == null))
        {
            Debug.LogWarning("[ZoneGenerator] Terrain Layer가 비어 있어 칠하기를 건너뜁니다.");
            return;
        }
        data.terrainLayers = layers;

        const int res = 128;
        data.alphamapResolution = res;
        float half = t.terrainSize * 0.5f;
        var maps = new float[res, res, 3];
        for (int iz = 0; iz < res; iz++)
        for (int ix = 0; ix < res; ix++)
        {
            float x = (ix + 0.5f) / res * t.terrainSize - half;
            float z = (iz + 0.5f) / res * t.terrainSize - half;
            float r = Mathf.Sqrt(x * x + z * z);

            float arena = 1f - Mathf.Clamp01((r - (t.combatRadius - 1f)) / 3f);
            float road = (1f - Mathf.Clamp01((Mathf.Abs(x) - (t.gateHalfWidth - 1f)) / 2f)) * (1f - arena);
            float grass = Mathf.Max(0f, 1f - arena - road);
            float sum = arena + road + grass;

            maps[iz, ix, 0] = grass / sum;
            maps[iz, ix, 1] = arena / sum;
            maps[iz, ix, 2] = road / sum;
        }
        data.SetAlphamaps(0, 0, maps);
    }

    // ===== 경계 =====
    // 보이지 않는 원형 벽: 바위 사이 틈으로 빠져나가지 않게. 문 자리는 비우고 결계가 막음
    static void BuildInvisibleWall(ZoneTheme t, Transform parent, bool capSouth, bool capNorth)
    {
        var wall = new GameObject("InvisibleWall");
        wall.transform.SetParent(parent, false);

        const int segments = 64;
        float r = t.wallRadius - 0.5f;
        float length = 2f * Mathf.PI * r / segments * 1.1f;
        for (int i = 0; i < segments; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2f / segments;
            Vector3 pos = new Vector3(Mathf.Cos(a) * r, 3f, Mathf.Sin(a) * r);
            if (Mathf.Abs(pos.x) < t.gateHalfWidth) continue;

            var seg = new GameObject($"Wall_{i:00}");
            seg.transform.SetParent(wall.transform, false);
            seg.transform.localPosition = pos;
            seg.transform.localRotation = Quaternion.LookRotation(-new Vector3(pos.x, 0f, pos.z).normalized, Vector3.up);
            seg.AddComponent<BoxCollider>().size = new Vector3(length, 6f, 1f);
        }

        // 문 밖 길 끝은 지형 가장자리라 떨어지지 않게 막음
        float end = t.terrainSize * 0.5f - 3f;
        foreach (float side in new[] { -1f, 1f })
        {
            if (side < 0f ? !capSouth : !capNorth) continue;
            var cap = new GameObject(side < 0f ? "Wall_SouthEnd" : "Wall_NorthEnd");
            cap.transform.SetParent(wall.transform, false);
            cap.transform.localPosition = new Vector3(0f, 3f, side * end);
            cap.AddComponent<BoxCollider>().size = new Vector3(t.gateHalfWidth * 2f + 12f, 6f, 1f);
        }
    }

    // 남쪽 입구, 북쪽 출구에 붉은 문 + 결계(콜라이더). 결계 열고 닫기는 ZoneArena 담당
    static (GameObject entrance, GameObject exit) BuildGates(ZoneTheme t, Transform parent, Terrain terrain)
    {
        Material gateMat = GetOrCreateMaterial("Zone_Gate", false, t.gateColor);
        Material barrierMat = GetOrCreateMaterial("Zone_Barrier", true, t.barrierColor);

        GameObject entrance = BuildGate("Gate_Entrance", -1f, t, parent, terrain, gateMat, barrierMat);
        GameObject exit = BuildGate("Gate_Exit", 1f, t, parent, terrain, gateMat, barrierMat);
        return (entrance, exit);
    }

    static GameObject BuildGate(string name, float side, ZoneTheme t, Transform parent, Terrain terrain, Material gateMat, Material barrierMat)
    {
        var gate = new GameObject(name);
        gate.transform.SetParent(parent, false);
        Vector3 basePos = new Vector3(0f, 0f, side * t.wallRadius);
        basePos.y = GroundY(terrain, origin + basePos);
        gate.transform.localPosition = basePos;

        float w = t.gateHalfWidth;
        Part(gate.transform, "Pillar_L", new Vector3(-(w + 0.4f), 2.75f, 0f), new Vector3(0.5f, 5.5f, 0.5f), gateMat, true);
        Part(gate.transform, "Pillar_R", new Vector3(w + 0.4f, 2.75f, 0f), new Vector3(0.5f, 5.5f, 0.5f), gateMat, true);
        Part(gate.transform, "Beam_Top", new Vector3(0f, 5.7f, 0f), new Vector3(w * 2f + 2.4f, 0.45f, 0.7f), gateMat, false);
        Part(gate.transform, "Beam_Low", new Vector3(0f, 4.9f, 0f), new Vector3(w * 2f + 0.9f, 0.3f, 0.45f), gateMat, false);

        GameObject barrier = Part(gate.transform, "Barrier", new Vector3(0f, 2.5f, 0f), new Vector3(w * 2f, 5f, 0.3f), barrierMat, true);
        barrier.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        return barrier;
    }

    // 부활 위치: 입구 문 바깥 길 위, 아레나(북쪽)를 바라봄
    static Transform BuildRespawnPoint(ZoneTheme t, Transform parent, Terrain terrain)
    {
        var point = new GameObject("RespawnPoint");
        point.transform.SetParent(parent, false);
        Vector3 local = new Vector3(0f, 0f, -(t.wallRadius + 3f));
        local.y = GroundY(terrain, origin + local) + 0.1f;
        point.transform.localPosition = local;
        point.transform.localRotation = Quaternion.identity;
        return point.transform;
    }

    // 웨이브는 인스펙터에서 조절하는 값이라 비어 있을 때만 기본값을 넣음 (다시 생성해도 덮어쓰지 않음)
    static void FillDefaultWaves(SerializedProperty list, int[] counts)
    {
        if (list == null || list.arraySize > 0 || counts == null) return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (prefab == null) Debug.LogWarning($"[ZoneGenerator] {EnemyPrefabPath}가 없어 웨이브 적 프리팹을 비워 둡니다. Swordsman 셋업을 먼저 실행하세요.");

        list.arraySize = counts.Length;
        for (int i = 0; i < counts.Length; i++)
        {
            SerializedProperty wave = list.GetArrayElementAtIndex(i);
            wave.FindPropertyRelative("enemyPrefab").objectReferenceValue = prefab;
            wave.FindPropertyRelative("count").intValue = counts[i];
        }
    }

    static GameObject Part(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // ===== 배치 =====
    // 원을 따라 각도를 조금씩 넘기며 바위를 놓음. 다음 바위까지 간격은 이번 바위 크기에 비례
    static int PlaceWallRocks(ZoneTheme t, Transform parent, Terrain terrain)
    {
        if (!HasAny(t.wallRocks, "Wall Rocks")) return 0;

        int count = 0;
        float angle = 0f;
        while (angle < Mathf.PI * 2f)
        {
            float size = Range(t.wallRockSize);
            float r = t.wallRadius + size * 0.2f;
            Vector3 pos = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
            angle += size * t.wallRockSpacing / t.wallRadius;

            if (Mathf.Abs(pos.x) < t.gateHalfWidth + 1.2f) continue;
            Spawn(Pick(t.wallRocks), parent, terrain, pos, size, false, size * 0.15f, true);
            count++;
        }
        return count;
    }

    static int PlaceInnerRocks(ZoneTheme t, Transform parent, Terrain terrain)
    {
        if (!HasAny(t.innerRocks, "Inner Rocks")) return 0;

        int count = 0;
        for (int attempt = 0; attempt < t.innerRockCount * 10 && count < t.innerRockCount; attempt++)
        {
            Vector3 pos = RandomInRing(t.combatRadius + 0.5f, t.wallRadius - 1f);
            if (Mathf.Abs(pos.x) < t.gateHalfWidth + 1f) continue;
            Spawn(Pick(t.innerRocks), parent, terrain, pos, Range(t.innerRockSize), false, 0.1f, true);
            count++;
        }
        return count;
    }

    // 벽 바깥 띠에만 심고, 서로 최소 거리를 둠 (단순 거부 샘플링)
    static int PlaceTrees(ZoneTheme t, Transform parent, Terrain terrain)
    {
        if (!HasAny(t.trees, "Trees")) return 0;

        var placed = new List<Vector3>();
        float half = t.terrainSize * 0.5f - 4f;
        float minSqr = t.treeMinDistance * t.treeMinDistance;

        for (int attempt = 0; attempt < t.treeMaxCount * 20 && placed.Count < t.treeMaxCount; attempt++)
        {
            Vector3 pos = new Vector3(RangeF(-half, half), 0f, RangeF(-half, half));
            float r = new Vector2(pos.x, pos.z).magnitude;
            if (r < t.wallRadius + 2.5f) continue;
            if (Mathf.Abs(pos.x) < t.gateHalfWidth + 2.5f) continue;
            if (placed.Any(p => (p - pos).sqrMagnitude < minSqr)) continue;

            placed.Add(pos);
            Spawn(Pick(t.trees), parent, terrain, pos, Range(t.treeHeight), true, 0.2f, true);
        }
        return placed.Count;
    }

    // 풀·덤불은 콜라이더를 떼서 이동·회피·일섬을 막지 않게
    static int PlaceCover(ZoneTheme t, Transform parent, Terrain terrain)
    {
        int count = 0;
        if (t.arenaCover != null && t.arenaCover.Length > 0)
        {
            for (int i = 0; i < t.arenaCoverCount; i++)
            {
                Spawn(Pick(t.arenaCover), parent, terrain, RandomInRing(1.5f, t.combatRadius - 0.5f), 0f, false, 0f, false);
                count++;
            }
        }

        if (t.outerCover != null && t.outerCover.Length > 0)
        {
            for (int attempt = 0; attempt < t.outerCoverCount * 5 && count < t.arenaCoverCount + t.outerCoverCount; attempt++)
            {
                Vector3 pos = RandomInRing(t.wallRadius + 0.5f, t.wallRadius + 10f);
                if (Mathf.Abs(pos.x) < t.gateHalfWidth + 0.5f) continue;
                Spawn(Pick(t.outerCover), parent, terrain, pos, 0f, false, 0f, false);
                count++;
            }
        }
        return count;
    }

    static int PlacePathStones(ZoneTheme t, Transform parent, Terrain terrain)
    {
        if (!HasAny(t.pathStones, "Path Stones")) return 0;

        int count = 0;
        float end = t.terrainSize * 0.5f - 6f;
        foreach (float side in new[] { -1f, 1f })
        {
            for (float d = t.wallRadius + 1.5f; d < end; d += t.pathStoneSpacing)
            {
                Vector3 pos = new Vector3(RangeF(-0.6f, 0.6f), 0f, side * d);
                Spawn(Pick(t.pathStones), parent, terrain, pos, t.pathStoneSize, false, 0.05f, false);
                count++;
            }
        }
        return count;
    }

    static void PlaceParticles(ZoneTheme t, Transform parent, Terrain terrain)
    {
        if (t.groundParticles != null && t.groundParticles.Length > 0)
        {
            for (int i = 0; i < t.groundParticleCount; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / t.groundParticleCount;
                Vector3 pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (t.wallRadius + 6f);
                Spawn(Pick(t.groundParticles), parent, terrain, pos, 0f, false, 0f, true);
            }
        }

        if (t.overheadParticles != null)
        {
            foreach (GameObject prefab in t.overheadParticles.Where(p => p != null))
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.position = origin + new Vector3(0f, 9f, 0f);
            }
        }
    }

    // size > 0이면 크기를 맞춤 (byHeight: 높이 기준, 아니면 가장 긴 변). sink: 땅에 묻는 깊이
    static GameObject Spawn(GameObject prefab, Transform parent, Terrain terrain, Vector3 pos, float size, bool byHeight, float sink, bool keepColliders)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

        float scale = 1f;
        if (size > 0f)
        {
            Bounds b = PrefabBounds(prefab);
            float source = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (source > 0.001f) scale = size / source;
        }

        pos += origin; // 배치 계산은 구역 중심 기준, 실제 위치는 월드
        pos.y = GroundY(terrain, pos) - sink;
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, RangeF(0f, 360f), 0f);
        go.transform.localScale = Vector3.one * scale;

        if (!keepColliders)
            foreach (Collider c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        return go;
    }

    // 프리팹마다 원본 크기를 한 번만 잼 (LOD가 여러 개라도 전체 렌더러 범위)
    static Bounds PrefabBounds(GameObject prefab)
    {
        if (boundsCache.TryGetValue(prefab, out Bounds cached)) return cached;

        var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        temp.transform.localScale = Vector3.one;

        Renderer[] renderers = temp.GetComponentsInChildren<Renderer>();
        Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

        Object.DestroyImmediate(temp);
        boundsCache[prefab] = b;
        return b;
    }

    static float GroundY(Terrain terrain, Vector3 pos) => terrain != null ? terrain.SampleHeight(pos) + terrain.transform.position.y : 0f;

    // ===== 분위기 =====
    static void ApplyAtmosphere(ZoneTheme t)
    {
        if (t.skybox != null)
        {
            RenderSettings.skybox = t.skybox;
            RenderSettings.ambientMode = AmbientMode.Skybox;
        }

        RenderSettings.fog = t.fog;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = t.fogColor;
        RenderSettings.fogDensity = t.fogDensity;

        Light sun = RenderSettings.sun;
        if (sun == null) sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun != null)
        {
            Undo.RecordObject(sun, "Zone Sun");
            Undo.RecordObject(sun.transform, "Zone Sun");
            sun.color = t.sunColor;
            sun.transform.rotation = Quaternion.Euler(t.sunEuler);
        }

        DynamicGI.UpdateEnvironment();
    }

    // ===== 공용 =====
    static Transform Group(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go.transform;
    }

    static Material GetOrCreateMaterial(string name, bool transparent, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader shader = Shader.Find(transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        mat = new Material(shader) { name = name };
        mat.SetColor("_BaseColor", color);
        if (transparent)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            mat.SetFloat("_Smoothness", 0.2f);
        }

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static bool HasAny(GameObject[] list, string label)
    {
        if (list != null && list.Any(p => p != null)) return true;
        Debug.LogWarning($"[ZoneGenerator] {label} 목록이 비어 있어 건너뜁니다.");
        return false;
    }

    static GameObject Pick(GameObject[] list)
    {
        GameObject[] valid = list.Where(p => p != null).ToArray();
        return valid[rng.Next(valid.Length)];
    }

    static float RangeF(float min, float max) => min + (float)rng.NextDouble() * (max - min);
    static float Range(Vector2 range) => RangeF(range.x, range.y);

    static Vector3 RandomInRing(float inner, float outer)
    {
        float a = RangeF(0f, Mathf.PI * 2f);
        float r = Mathf.Sqrt(RangeF(inner * inner, outer * outer)); // 면적 기준 균등 분포
        return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
    }

    // ===== 구역별 기본값 (에셋이 없을 때 한 번만 만듦) =====
    // 공통 준비를 분리
    static ZoneTheme NewTheme(string zoneName)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(PackRoot.TrimEnd('/')) == null)
        {
            Debug.LogError($"[ZoneGenerator] {PackRoot} 폴더가 없습니다. Toon Fantasy Nature(URP)를 ThirdParty에 임포트하세요.");
            return null;
        }
        if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/11.Data", "Zone");

        var theme = ScriptableObject.CreateInstance<ZoneTheme>();
        theme.zoneName = zoneName;
        return theme;
    }

    static ZoneTheme SaveTheme(ZoneTheme theme, string path)
    {
        AssetDatabase.CreateAsset(theme, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ZoneGenerator] 테마 생성: {path}");
        return theme;
    }

    static ZoneTheme CreateZone1Theme()
    {
        ZoneTheme theme = NewTheme("Zone_1_Sanmun");
        if (theme == null) return null;

        theme.groundLayer = Load<TerrainLayer>("Terrain/Layer_TFF_Terrain_Grass_1A_D.terrainlayer");
        theme.arenaLayer = Load<TerrainLayer>("Terrain/Layer_TFF_Terrain_Earth_1A_D.terrainlayer");
        theme.roadLayer = Load<TerrainLayer>("Terrain/Layer_TFF_Terrain_Dirt_Road_1A_D.terrainlayer");

        theme.wallRocks = Prefabs("Rocks", "TFF_Rock_Large_");
        theme.innerRocks = Prefabs("Rocks", "TFF_Rock_Medium_").Concat(Prefabs("Rocks", "TFF_Rock_Small_")).ToArray();
        theme.trees = Prefabs("Vegetation/Trees", "TFF_Pine_Tree_");
        theme.arenaCover = Prefabs("Vegetation/Plants", "TFF_Grass_Patch_");
        theme.outerCover = Prefabs("Vegetation/Plants", "TFF_Bush_0").Concat(Prefabs("Vegetation/Plants", "TFF_Fern_")).ToArray();
        theme.pathStones = Prefabs("Rocks", "TFF_Stone_Slab_");

        theme.skybox = Load<Material>("Skyboxes/TFF_Skybox_Day_01A.mat");
        theme.groundParticles = new[] { Load<GameObject>("Particles/TFF_Fog_01A.prefab") };
        theme.overheadParticles = new[] { Load<GameObject>("Particles/TFF_Falling_Leaves_01A.prefab") };

        return SaveTheme(theme, Zone1ThemePath);
    }

    // 구역 2: 안개 낀 소나무 숲길. 나무를 빽빽하게, 안개를 진하게, 해는 낮고 차갑게
    static ZoneTheme CreateZone2Theme()
    {
        ZoneTheme theme = NewTheme("Zone_2_Forest");
        if (theme == null) return null;

        theme.seed = 2;
        theme.terrainSize = 110f;
        theme.hillHeight = 12f;
        theme.hillNoise = 5f;
        theme.hillWidth = 18f;

        theme.groundLayer = Load<TerrainLayer>("Terrain/Layer_TFF_Terrain_Grass_2A_D.terrainlayer");
        theme.arenaLayer = Load<TerrainLayer>("Terrain/Layer_TFF_Terrain_Earth_2A_D.terrainlayer");
        theme.roadLayer = Load<TerrainLayer>("Terrain/Layer_TFF_Terrain_Dirt_Road_1A_D.terrainlayer");

        theme.wallRocks = Prefabs("Rocks", "TFF_Rock_Large_");
        theme.wallRockSize = new Vector2(3.5f, 5.5f);
        theme.innerRocks = Prefabs("Rocks", "TFF_Rock_Medium_").Concat(Prefabs("Vegetation/Trees", "TFF_Tree_Trunk_")).ToArray();
        theme.innerRockCount = 10;
        theme.trees = Prefabs("Vegetation/Trees", "TFF_Pine_Tree_");
        theme.treeHeight = new Vector2(12f, 19f);
        theme.treeMinDistance = 3.2f;
        theme.treeMaxCount = 320;
        theme.arenaCover = Prefabs("Vegetation/Plants", "TFF_Grass_Patch_").Concat(Prefabs("Vegetation/Plants", "TFF_Fern_")).ToArray();
        theme.arenaCoverCount = 50;
        theme.outerCover = Prefabs("Vegetation/Plants", "TFF_Fern_")
            .Concat(Prefabs("Vegetation/Trees", "TFF_Branch_Fallen_"))
            .Concat(Prefabs("Vegetation/Trees", "TFF_Tree_Log_")).ToArray();
        theme.outerCoverCount = 110;
        theme.pathStones = Prefabs("Rocks", "TFF_Stone_Slab_");

        theme.skybox = Load<Material>("Skyboxes/TFF_Skybox_Day_01A.mat");
        theme.fogColor = new Color(0.6f, 0.68f, 0.7f);
        theme.fogDensity = 0.02f;
        theme.sunColor = new Color(0.85f, 0.9f, 1f);
        theme.sunEuler = new Vector3(28f, 20f, 0f);
        theme.groundParticles = new[] { Load<GameObject>("Particles/TFF_Fog_01A.prefab") };
        theme.groundParticleCount = 10;
        theme.overheadParticles = new[]
        {
            Load<GameObject>("Particles/TFF_Falling_Leaves_01A.prefab"),
            Load<GameObject>("Particles/TFF_Sun_Shaft_01A.prefab")
        };

        return SaveTheme(theme, Zone2ThemePath);
    }

    static T Load<T>(string relative) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(PackRoot + relative);
        if (asset == null) Debug.LogWarning($"[ZoneGenerator] {PackRoot + relative}를 찾지 못했습니다.");
        return asset;
    }

    static GameObject[] Prefabs(string folder, string prefix)
    {
        return AssetDatabase.FindAssets("t:Prefab", new[] { PackRoot + "Prefabs/" + folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => System.IO.Path.GetFileName(p).StartsWith(prefix))
            .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p))
            .Where(p => p != null)
            .ToArray();
    }
}
