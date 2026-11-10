using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;

// 스킬트리 + 화톳불 셋업 (다시 실행해도 됨)
//  1) 노드 데이터 에셋 (없으면 기본값으로 생성) + 미리보기 영상 자동 연결: VideoFolder/<SkillId 이름>.mp4 등
//  2) 씬에 SkillTreeMenu (Pretendard 글꼴 · 데이터 · PlayerStats 연결)
//  3) 화톳불 프리팹 (돌 등롱 모양, 기본 도형) + 씬의 각 RespawnPoint 옆에 하나씩 (이미 근처에 있으면 건너뜀)
public static class SkillTreeSetup
{
    const string DataPath = "Assets/11.Data/Skill/SkillTree.asset";
    const string VideoFolder = "Assets/12.Video/SkillPreview";
    const string PrefabPath = "Assets/2.Model/Prefabs/Shrine.prefab";
    const string MaterialFolder = "Assets/6.Materials/Shrine";
    const string TitleFontPath = "Assets/9.Font/public/static/Pretendard-SemiBold.otf";
    const string BodyFontPath = "Assets/9.Font/public/static/Pretendard-Regular.otf";

    [MenuItem("Tools/Setup/Skill Tree + Shrines")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[SkillTree] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        SkillTreeData data = LoadOrCreateData();
        int videos = AssignVideos(data);
        SkillTreeMenu menu = PlaceMenu(data);
        GameObject prefab = LoadOrCreateShrinePrefab();
        int placed = PlaceShrines(prefab);

        EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
        Debug.Log($"[SkillTree] 완료 · 노드 {data.nodes.Count}개 (영상 {videos}개 연결) · 화톳불 {placed}개 새로 배치 → 씬을 저장하세요");
    }

    static SkillTreeData LoadOrCreateData()
    {
        var data = AssetDatabase.LoadAssetAtPath<SkillTreeData>(DataPath);
        if (data != null)
        {
            // [변경] 새로 추가된 기본 노드(수련 탭 등)를 기존 에셋에 덧붙임. 고친 이름·비용은 그대로
            int before = data.nodes.Count;
            data.nodes = SkillTreeData.WithMissingDefaults(data.nodes);
            if (data.nodes.Count != before)
            {
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
            }
            return data;
        }
        data = ScriptableObject.CreateInstance<SkillTreeData>();
        data.nodes = SkillTreeData.Defaults();
        AssetDatabase.CreateAsset(data, DataPath);
        AssetDatabase.SaveAssets();
        return data;
    }

    // 핵심: 영상 파일 이름 = SkillId 이름 (예: ChainStrike1.mp4). 영상을 새로 넣으면 이 메뉴만 다시 실행
    static int AssignVideos(SkillTreeData data)
    {
        if (!AssetDatabase.IsValidFolder(VideoFolder)) return data.nodes.Count(n => n.preview != null);
        var clips = AssetDatabase.FindAssets("t:VideoClip", new[] { VideoFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<VideoClip>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(c => c != null)
            .ToList();
        foreach (SkillTreeData.Node node in data.nodes)
        {
            VideoClip clip = clips.FirstOrDefault(c => c.name == node.id.ToString());
            if (clip != null) node.preview = clip;
        }
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        return data.nodes.Count(n => n.preview != null);
    }

    static SkillTreeMenu PlaceMenu(SkillTreeData data)
    {
        SkillTreeMenu menu = Object.FindAnyObjectByType<SkillTreeMenu>(FindObjectsInactive.Include);
        if (menu == null)
        {
            var go = new GameObject("SkillTreeMenu");
            Undo.RegisterCreatedObjectUndo(go, "Skill Tree");
            menu = go.AddComponent<SkillTreeMenu>();
        }
        var so = new SerializedObject(menu);
        so.FindProperty("data").objectReferenceValue = data;
        so.FindProperty("titleFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
        so.FindProperty("bodyFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath);
        so.FindProperty("stats").objectReferenceValue = Object.FindAnyObjectByType<PlayerStats>();
        so.ApplyModifiedProperties();
        return menu;
    }

    // ===== 화톳불 프리팹: 받침 · 기둥 · 불 상자 · 지붕 + 따뜻한 점광원 =====
    static GameObject LoadOrCreateShrinePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/6.Materials", "Shrine");
        Material stone = MakeMaterial("ShrineStone", new Color(0.26f, 0.25f, 0.24f), Color.black);
        Material fire = MakeMaterial("ShrineFire", new Color(1f, 0.6f, 0.25f), new Color(1f, 0.55f, 0.18f) * 5f);

        var root = new GameObject("Shrine");
        Part(root, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.1f, 0f), new Vector3(1.3f, 0.1f, 1.3f), stone, true);
        Part(root, PrimitiveType.Cube, "Pillar", new Vector3(0f, 0.75f, 0f), new Vector3(0.32f, 1.1f, 0.32f), stone, true);
        Part(root, PrimitiveType.Cube, "Fire", new Vector3(0f, 1.45f, 0f), new Vector3(0.42f, 0.32f, 0.42f), fire, false);
        Part(root, PrimitiveType.Cube, "Roof", new Vector3(0f, 1.7f, 0f), new Vector3(0.85f, 0.12f, 0.85f), stone, false);

        var lightGo = new GameObject("FireLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.45f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.68f, 0.38f);
        light.range = 7f;
        light.intensity = 3f;
        light.shadows = LightShadows.None;

        Shrine shrine = root.AddComponent<Shrine>();
        var so = new SerializedObject(shrine);
        so.FindProperty("fireLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void Part(GameObject root, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    static Material MakeMaterial(string name, Color baseColor, Color emission)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", baseColor);
        if (emission.maxColorComponent > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // 각 구역 RespawnPoint 앞 오른쪽에 하나씩 (구역을 다시 생성해도 지워지지 않게 씬 루트 "Shrines" 아래)
    static int PlaceShrines(GameObject prefab)
    {
        Transform[] respawns = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(t => t.name == "RespawnPoint").ToArray();
        Shrine[] existing = Object.FindObjectsByType<Shrine>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        GameObject group = GameObject.Find("Shrines");
        if (group == null)
        {
            group = new GameObject("Shrines");
            Undo.RegisterCreatedObjectUndo(group, "Shrines");
        }

        int placed = 0;
        foreach (Transform r in respawns)
        {
            Vector3 pos = r.position + r.forward * 2.5f + r.right * 2f;
            if (existing.Any(s => (s.transform.position - pos).sqrMagnitude < 36f)) continue;

            if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-r.right, Vector3.up));
            Transform zone = r.parent != null && r.parent.parent != null ? r.parent.parent : r.parent;
            go.name = zone != null ? $"Shrine ({zone.name})" : "Shrine";
            Undo.RegisterCreatedObjectUndo(go, "Shrine");
            placed++;
        }
        return placed;
    }
}
