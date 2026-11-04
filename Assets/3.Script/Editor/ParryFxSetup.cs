using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class ParryFxSetup
{
    const string MaterialPath = "Assets/6.Materials/Afterimage.mat";

    [MenuItem("Tools/Setup/Parry Fx (v2)")]
    static void Run()
    {
        var parry = Object.FindAnyObjectByType<PlayerParry>();
        if (parry == null)
        {
            Debug.LogError("[ParryFxSetup] PlayerParry가 없습니다. Tools/Setup/Just Dodge and Parry를 먼저 실행하세요.");
            return;
        }

        GameObject player = parry.gameObject;
        ParryFx fx = player.GetComponent<ParryFx>();
        if (fx == null) fx = Undo.AddComponent<ParryFx>(player);

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) Debug.LogWarning($"[ParryFxSetup] {MaterialPath}가 없어 링·적 잔상이 꺼집니다.");

        // 낙뢰 프리팹은 HitFeedback에 연결된 것을 그대로 사용
        Object bolt = null;
        var feedback = player.GetComponent<HitFeedback>();
        if (feedback != null) bolt = new SerializedObject(feedback).FindProperty("boltPrefab").objectReferenceValue;
        if (bolt == null) Debug.LogWarning("[ParryFxSetup] HitFeedback의 Bolt Prefab이 비어 있어 간파 낙뢰가 꺼집니다.");

        var so = new SerializedObject(fx);
        so.FindProperty("parry").objectReferenceValue = parry;
        so.FindProperty("skill").objectReferenceValue = player.GetComponent<PlayerSkill>();
        so.FindProperty("followCamera").objectReferenceValue = Object.FindAnyObjectByType<ThirdPersonCamera>();
        so.FindProperty("fxMaterial").objectReferenceValue = material;
        so.FindProperty("boltPrefab").objectReferenceValue = bolt;
        so.ApplyModifiedProperties();

        var parrySo = new SerializedObject(parry);
        parrySo.FindProperty("clashStyle").boolValue = true;
        parrySo.ApplyModifiedProperties();

        // 화면 펄스(색수차·비네트·채도)는 카메라 후처리가 켜져 있어야 보임
        Camera cam = Camera.main;
        if (cam != null)
        {
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null && !data.renderPostProcessing)
            {
                Undo.RecordObject(data, "Enable Post Processing");
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
            }
        }

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log("[ParryFxSetup] 완료 · 간파 v2(부딪히는 순간 연출) 연결. 비교는 PlayerParry의 Clash Style 토글");
    }
}
