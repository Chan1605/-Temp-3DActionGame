using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 프레임 안정화용 렌더 설정 (한 번 실행). 처음 기본값일 때만 바꾸므로 이후 직접 조정한 값은 건드리지 않음
//  - 그림자 캐스케이드 4 → 2, 소프트 섀도 High → Medium, 그림자 거리 50 → 40, 추가 조명 그림자 끔
//  - SSAO 절반 해상도
//  - 카메라 Far 1000 → 300 (안개 Exp2 0.008이면 300m 밖은 거의 안 보임)
public static class PerformanceSetup
{
    const string RPAssetPath = "Assets/2.Model/PIpeLine/Settings/PC_RPAsset.asset";
    const string RendererPath = "Assets/2.Model/PIpeLine/Settings/PC_Renderer.asset";

    [MenuItem("Tools/Setup/Performance")]
    static void Run()
    {
        int changed = 0;
        changed += TunePipeline();
        changed += TuneSsao();
        changed += TuneCamera();

        AssetDatabase.SaveAssets();
        Debug.Log($"[PerformanceSetup] 완료 · 바꾼 항목 {changed}개 (이미 조정된 값은 그대로 둠). 씬을 저장하세요");
    }

    static int TunePipeline()
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(RPAssetPath);
        if (asset == null) { Debug.LogWarning($"[PerformanceSetup] {RPAssetPath} 없음"); return 0; }

        var so = new SerializedObject(asset);
        int n = 0;
        n += SetInt(so, "m_ShadowCascadeCount", 4, 2);
        n += SetInt(so, "m_SoftShadowQuality", 3, 2);       // High → Medium
        n += SetFloat(so, "m_ShadowDistance", 50f, 40f);
        n += SetBool(so, "m_AdditionalLightShadowsSupported", true, false); // 점광원은 전부 그림자 없음
        so.ApplyModifiedProperties();
        if (n > 0) EditorUtility.SetDirty(asset);
        return n;
    }

    static int TuneSsao()
    {
        foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(RendererPath))
        {
            if (sub == null || sub.name != "ScreenSpaceAmbientOcclusion") continue;
            var so = new SerializedObject(sub);
            int n = SetBool(so, "m_Settings.Downsample", false, true);
            so.ApplyModifiedProperties();
            if (n > 0) EditorUtility.SetDirty(sub);
            return n;
        }
        return 0;
    }

    static int TuneCamera()
    {
        var follow = Object.FindAnyObjectByType<ThirdPersonCamera>();
        Camera cam = follow != null ? follow.GetComponent<Camera>() : Camera.main;
        if (cam == null || cam.farClipPlane < 999f) return 0;

        Undo.RecordObject(cam, "Camera Far");
        cam.farClipPlane = 300f;
        EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
        return 1;
    }

    static int SetInt(SerializedObject so, string name, int from, int to)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p == null || p.intValue != from) return 0;
        p.intValue = to;
        return 1;
    }

    static int SetFloat(SerializedObject so, string name, float from, float to)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p == null || !Mathf.Approximately(p.floatValue, from)) return 0;
        p.floatValue = to;
        return 1;
    }

    static int SetBool(SerializedObject so, string name, bool from, bool to)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p == null || p.boolValue != from) return 0;
        p.boolValue = to;
        return 1;
    }
}
