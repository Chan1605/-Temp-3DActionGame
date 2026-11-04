using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 1단계 다듬기 (개발영상3 피드백): 한 번 실행하면 아래를 적용하고 구역을 다시 생성
//  - 구역 1의 허수아비 끄기 (화면을 가림)
//  - 카메라 충돌 반경 키우기 (문 기둥에 화면이 박히는 현상)
//  - 구역 1 지형 140 → 110 (연결 길 9초 → 약 6초)
//  - 구역 2 안개를 옅게 (어두운 흑검이 묻힘) + 흑검 몸 색을 조금 밝게
//  - 등장 낙뢰·마무리 연출(ZoneFx) 추가
public static class PolishPass
{
    const string Zone1Path = "Assets/11.Data/Zone/Zone1_Sanmun.asset";
    const string Zone2Path = "Assets/11.Data/Zone/Zone2_Forest.asset";
    const string EnemyPrefabPath = "Assets/2.Model/Prefabs/Swordsman.prefab";
    const string BoltPrefab = "Assets/ThirdParty/ProceduralLightning/Prefab/LightningBoltPrefabHDR.prefab";

    [MenuItem("Tools/Setup/Polish Pass (Stage 1)")]
    static void Run()
    {
        int dummies = DisableDummies();
        TuneCamera();
        TuneThemes();
        TuneEnemyColor();
        SetupZoneFx();

        ZoneGenerator.GenerateAll();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[PolishPass] 완료 · 허수아비 {dummies}개 끔, 카메라·구역·흑검 색·연출 적용 후 구역 재생성");
    }

    // 지우지 않고 끔 → 테스트가 필요하면 다시 켜면 됨
    static int DisableDummies()
    {
        int count = 0;
        foreach (TrainingDummy dummy in Object.FindObjectsByType<TrainingDummy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            Undo.RecordObject(dummy.gameObject, "Disable Dummy");
            dummy.gameObject.SetActive(false);
            count++;
        }
        return count;
    }

    static void TuneCamera()
    {
        var cam = Object.FindAnyObjectByType<ThirdPersonCamera>();
        if (cam == null) return;
        var so = new SerializedObject(cam);
        so.FindProperty("collisionRadius").floatValue = 0.45f;
        so.ApplyModifiedProperties();

        Camera c = cam.GetComponent<Camera>();
        if (c != null && c.nearClipPlane > 0.1f)
        {
            Undo.RecordObject(c, "Near Clip");
            c.nearClipPlane = 0.1f;
        }
    }

    // 사용자가 이미 바꾼 값은 건드리지 않고, 처음 기본값일 때만 바꿈
    static void TuneThemes()
    {
        var zone1 = AssetDatabase.LoadAssetAtPath<ZoneTheme>(Zone1Path);
        if (zone1 != null && Mathf.Approximately(zone1.terrainSize, 140f))
        {
            Undo.RecordObject(zone1, "Zone1 Size");
            zone1.terrainSize = 110f;
            EditorUtility.SetDirty(zone1);
        }

        var zone2 = AssetDatabase.LoadAssetAtPath<ZoneTheme>(Zone2Path);
        if (zone2 != null && Mathf.Approximately(zone2.fogDensity, 0.02f))
        {
            Undo.RecordObject(zone2, "Zone2 Fog");
            zone2.fogDensity = 0.012f;
            zone2.fogColor = new Color(0.68f, 0.74f, 0.76f);
            EditorUtility.SetDirty(zone2);
        }
        AssetDatabase.SaveAssets();
    }

    // 흑검 톤은 유지하되 안개 속에서 실루엣이 보이도록 약간 밝고 보랏빛(사기)으로
    static void TuneEnemyColor()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath) == null) return;

        GameObject root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
        var enemy = root.GetComponent<EnemySwordsman>();
        if (enemy != null)
        {
            var so = new SerializedObject(enemy);
            SerializedProperty tint = so.FindProperty("bodyTint");
            if (tint != null && tint.colorValue.r <= 0.36f) tint.colorValue = new Color(0.44f, 0.4f, 0.54f); // 현재 0.35 → 한 단계 밝게
            so.ApplyModifiedProperties();
            PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
        }
        PrefabUtility.UnloadPrefabContents(root);
    }

    static void SetupZoneFx()
    {
        var fx = Object.FindAnyObjectByType<ZoneFx>();
        if (fx == null)
        {
            var go = new GameObject("ZoneFx");
            Undo.RegisterCreatedObjectUndo(go, "ZoneFx");
            fx = go.AddComponent<ZoneFx>();
        }

        var so = new SerializedObject(fx);
        so.FindProperty("boltPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(BoltPrefab);
        so.FindProperty("followCamera").objectReferenceValue = Object.FindAnyObjectByType<ThirdPersonCamera>();
        so.FindProperty("skill").objectReferenceValue = Object.FindAnyObjectByType<PlayerSkill>();
        so.ApplyModifiedProperties();
    }
}
