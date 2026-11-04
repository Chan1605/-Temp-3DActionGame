using System.Linq;
using SickscoreGames.HUDNavigationSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// HUD Navigation System 연결
//  - 나침반 바(상단 중앙): 방위 + 구역 돌파 후 출구 목표와 거리
//  - 레이더(좌상단): 주변 적 위치
//  - 화면 밖 표시: 시야 밖 적은 화면 가장자리에 화살표 (기획서 17번 "화면 밖 공격은 방향 표시")
//  - 미니맵은 맵 텍스처가 없어 끔
// 에셋 위치가 바뀌어도(ThirdParty로 옮겨도) 이름으로 찾음
public static class HUDNavigationSetup
{
    const string DataFolder = "Assets/11.Data/Navigation";
    const string EnemySettingsPath = DataFolder + "/Nav_Enemy.asset";
    const string ObjectiveSettingsPath = DataFolder + "/Nav_Objective.asset";
    const string EnemyPrefabPath = "Assets/2.Model/Prefabs/Swordsman.prefab";
    const string SystemName = "HUD Navigation System";
    const string CanvasName = "HUD Navigation Canvas";
    const string ObjectiveName = "Objective_Exit";

    // [변경] 나침반 눈금 후보 (투명 배경이 있는 첫 번째를 사용)
    static readonly string[] CompassTextures = { "Compass_Minimal", "Compass_Simple", "Compass_RPG", "Compass_Shooter", "Compass_Flight" };

    static readonly Color EnemyColor = new Color(1f, 0.32f, 0.25f);
    static readonly Color ObjectiveColor = new Color(0.95f, 0.8f, 0.45f);

    [MenuItem("Tools/Setup/HUD Navigation")]
    static void Run()
    {
        GameObject canvasPrefab = FindPrefab(CanvasName);
        var radar = FindPrefab("RadarPrefab_Default")?.GetComponent<HNSRadarPrefab>();
        var compass = FindPrefab("CompassBarPrefab_Default")?.GetComponent<HNSCompassBarPrefab>();
        var indicator = FindPrefab("IndicatorPrefab_Default")?.GetComponent<HNSIndicatorPrefab>();
        if (canvasPrefab == null || radar == null || compass == null || indicator == null)
        {
            Debug.LogError("[HUDNavSetup] HUD Navigation System 프리팹을 찾지 못했습니다. 에셋 임포트를 확인하세요.");
            return;
        }

        var player = Object.FindAnyObjectByType<PlayerLocomotion>();
        Camera cam = Camera.main;
        if (player == null || cam == null)
        {
            Debug.LogError("[HUDNavSetup] 씬에 Player 또는 MainCamera가 없습니다.");
            return;
        }

        HUDNavigationElementSettings enemySettings = GetOrCreateSettings(EnemySettingsPath, s =>
        {
            s.Prefabs = new HNSPrefabs { RadarPrefab = radar, IndicatorPrefab = indicator };
            s.hideInCompassBar = true;           // 나침반은 목표 전용 (적까지 올리면 지저분)
            s.showIndicator = false;             // 화면 안의 적 머리 위 표식은 끔 (모션을 가리지 않게)
            s.showOffscreenIndicator = true;     // 화면 밖 적만 가장자리 화살표
            s.ignoreIndicatorRadius = false;     // 가까운 적만
            s.useIndicatorDistanceText = false;
            s.useRadarHeightSystem = false;
        });

        HUDNavigationElementSettings objectiveSettings = GetOrCreateSettings(ObjectiveSettingsPath, s =>
        {
            s.Prefabs = new HNSPrefabs { RadarPrefab = radar, CompassBarPrefab = compass, IndicatorPrefab = indicator };
            s.ignoreCompassBarRadius = true;
            s.ignoreIndicatorRadius = true;
            s.ignoreRadarRadius = true;
            s.useCompassBarDistanceText = true;
            s.useIndicatorDistanceText = true;
            s.showOffscreenIndicatorDistance = true;
            s.useRadarHeightSystem = false;
        });

        RemoveExisting(SystemName);
        RemoveExisting(CanvasName);

        BuildCanvas(canvasPrefab);
        BuildSystem(cam, player.transform);

        GetOrAdd<HNSPlayerController>(player.gameObject);
        GetOrAdd<HNSPlayerCamera>(cam.gameObject);

        SetupEnemyPrefab(enemySettings);
        int zones = SetupZoneObjectives(objectiveSettings);

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Debug.Log($"[HUDNavSetup] 완료 · 나침반·레이더·화면 밖 표시 연결, 구역 목표 {zones}개");
    }

    // 에셋 기본 캔버스는 1280x720 기준. 우리 HUD(1920x1080)와 같은 비율로 커지도록 Match만 0.5로 맞추고 레이더를 좌상단으로 옮김
    static void BuildCanvas(GameObject prefab)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = CanvasName;
        Undo.RegisterCreatedObjectUndo(go, "HUD Navigation Canvas");

        var scaler = go.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.matchWidthOrHeight = 0.5f;
        }
        var canvas = go.GetComponent<Canvas>();
        if (canvas != null) canvas.sortingOrder = 5; // 우리 HUD(10) 아래 → 사망 화면이 나침반까지 덮음

        var hns = go.GetComponent<HUDNavigationCanvas>();
        if (hns != null && hns.Radar.Panel != null)
        {
            RectTransform panel = hns.Radar.Panel;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(37f, -32f); // 1080p 기준 약 56, 48 (우리 HUD 여백과 같게)
            panel.sizeDelta = new Vector2(120f, 120f);
        }
        if (hns != null && hns.CompassBar.Panel != null)
        {
            hns.CompassBar.Panel.anchoredPosition = new Vector2(0f, -10f);

            // [변경] 각도 숫자 상자는 스프라이트 없는 흰 사각형이라 끔 (액션게임에는 각도 표시가 필요 없음)
            Transform degrees = hns.CompassBar.Panel.Find("Compass Bar Mask/Degrees Box");
            if (degrees != null) degrees.gameObject.SetActive(false);
        }
        if (hns != null && hns.CompassBar.Compass != null) FixCompassTexture(hns.CompassBar.Compass); // [변경]
    }

    // [변경] 나침반이 흰 네모로 보이는 원인 두 가지를 처리
    //  1) 텍스처가 비어 있음 → 후보 중 하나를 연결
    //  2) PSD가 투명도 없이 임포트됨 → 투명도 사용 + 반복(Repeat) 설정. 원본에 알파가 없으면 다음 후보로
    static void FixCompassTexture(RawImage compass)
    {
        var candidates = new System.Collections.Generic.List<Texture>();
        if (compass.texture != null) candidates.Add(compass.texture);
        foreach (string name in CompassTextures)
        {
            Texture t = FindTexture(name);
            if (t != null && !candidates.Contains(t)) candidates.Add(t);
        }

        foreach (Texture tex in candidates)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || !importer.DoesSourceTextureHaveAlpha())
            {
                Debug.Log($"[HUDNavSetup] 나침반 후보 제외(알파 없음): {path}");
                continue;
            }

            bool changed = false;
            if (importer.alphaSource != TextureImporterAlphaSource.FromInput) { importer.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
            if (importer.wrapMode != TextureWrapMode.Repeat) { importer.wrapMode = TextureWrapMode.Repeat; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (changed) importer.SaveAndReimport();

            compass.texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            compass.color = Color.white;
            EditorUtility.SetDirty(compass);
            Debug.Log($"[HUDNavSetup] 나침반 텍스처: {path}{(changed ? " (임포트 설정 수정)" : "")}");
            return;
        }

        Debug.LogWarning("[HUDNavSetup] 투명 배경이 있는 나침반 텍스처를 찾지 못했습니다. HUD Navigation System에서 Use Compass Bar를 꺼주세요.");
    }

    static Texture FindTexture(string name)
    {
        return AssetDatabase.FindAssets($"{name} t:Texture")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == name)
            .Select(p => AssetDatabase.LoadAssetAtPath<Texture>(p))
            .FirstOrDefault(t => t != null);
    }

    static void BuildSystem(Camera cam, Transform player)
    {
        var go = new GameObject(SystemName);
        Undo.RegisterCreatedObjectUndo(go, "HUD Navigation System");
        var system = go.AddComponent<HUDNavigationSystem>();

        system.PlayerCamera = cam;
        system.PlayerController = player;
        system.RotationReference = _RotationReference.Camera;
        system.KeepAliveOnLoad = false; // 한 씬 구조라 필요 없음

        system.useRadar = true;
        system.radarMode = RadarModes.RotateRadar;
        system.radarRadius = 25f;
        system.radarMaxRadius = 35f;

        system.useCompassBar = true;
        system.compassBarRadius = 150f;

        system.useIndicators = true;
        system.indicatorRadius = 25f;
        system.indicatorHideDistance = 4f;
        system.useOffscreenIndicators = true;

        system.useMinimap = false;
    }

    // 웨이브로 나오는 적도 표식이 붙도록 프리팹 자체에 추가
    static void SetupEnemyPrefab(HUDNavigationElementSettings settings)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath) == null)
        {
            Debug.LogWarning($"[HUDNavSetup] {EnemyPrefabPath}가 없어 적 표식을 건너뜁니다.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
        var element = GetOrAdd<HUDNavigationElement>(root);
        element.Settings = settings;
        SetColor(GetOrAdd<NavTarget>(root), EnemyColor);
        PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    // 구역마다 출구 문 바로 바깥에 목표 표식. Generated 밖(구역 루트 아래)에 두어 맵을 다시 생성해도 유지됨
    static int SetupZoneObjectives(HUDNavigationElementSettings settings)
    {
        int count = 0;
        foreach (ZoneArena arena in Object.FindObjectsByType<ZoneArena>(FindObjectsSortMode.None))
        {
            Transform gate = arena.transform.Find("Generated/Gate_Exit");
            if (gate == null) continue;

            Transform old = arena.transform.Find(ObjectiveName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var marker = new GameObject(ObjectiveName);
            Undo.RegisterCreatedObjectUndo(marker, "Zone Objective");
            marker.transform.SetParent(arena.transform, false);
            marker.transform.position = gate.position + Vector3.up * 2.5f + Vector3.forward * 2f;
            marker.AddComponent<HUDNavigationElement>().Settings = settings;
            SetColor(marker.AddComponent<NavTarget>(), ObjectiveColor);
            marker.SetActive(false);

            var objective = GetOrAdd<ZoneObjective>(arena.gameObject);
            var so = new SerializedObject(objective);
            so.FindProperty("arena").objectReferenceValue = arena;
            so.FindProperty("marker").objectReferenceValue = marker;
            so.ApplyModifiedProperties();
            count++;
        }
        return count;
    }

    static HUDNavigationElementSettings GetOrCreateSettings(string path, System.Action<HUDNavigationElementSettings> init)
    {
        var settings = AssetDatabase.LoadAssetAtPath<HUDNavigationElementSettings>(path);
        if (settings != null) return settings; // 이미 있으면 인스펙터에서 바꾼 값을 유지

        if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/11.Data", "Navigation");
        settings = ScriptableObject.CreateInstance<HUDNavigationElementSettings>();
        init(settings);
        AssetDatabase.CreateAsset(settings, path);
        AssetDatabase.SaveAssets();
        return settings;
    }

    static GameObject FindPrefab(string name)
    {
        return AssetDatabase.FindAssets($"{name} t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == name)
            .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p))
            .FirstOrDefault(p => p != null);
    }

    static void SetColor(NavTarget target, Color color)
    {
        var so = new SerializedObject(target);
        so.FindProperty("color").colorValue = color;
        so.ApplyModifiedProperties();
    }

    static void RemoveExisting(string name)
    {
        GameObject old = GameObject.Find(name);
        if (old != null) Undo.DestroyObjectImmediate(old);
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        return comp != null ? comp : go.AddComponent<T>();
    }
}
