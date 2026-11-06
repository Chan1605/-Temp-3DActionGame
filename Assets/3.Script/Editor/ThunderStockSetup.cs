using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// [변경] 스톡형 되돌리기: Thunder Stock을 실행했다면 씬의 PlayerThunder 값을 이전(최대 4칸, 일섬 2/3/4칸)으로 복구
// 한 번 실행한 뒤(또는 Thunder Stock을 실행한 적이 없다면 바로) 이 파일은 지워도 됨
public static class ThunderStockSetup
{
    [MenuItem("Tools/Setup/Thunder Stock Revert")]
    static void Run()
    {
        var thunder = Object.FindAnyObjectByType<PlayerThunder>();
        if (thunder == null)
        {
            Debug.LogError("[ThunderStock] 씬에 PlayerThunder가 없습니다.");
            return;
        }

        var so = new SerializedObject(thunder);
        Set(so, "maxGauge", 4f);
        Set(so, "lightHitGain", 0.2f);
        Set(so, "heavyHitGain", 0.4f);
        Set(so, "chargeGainPerLevel", 0.3f);
        Set(so, "justDodgeGain", 1f);
        Set(so, "kanpaGain", 0.5f);
        Set(so, "executionGain", 1.5f);
        Set(so, "parryCost", 1f);

        SerializedProperty cost = so.FindProperty("skillCost");
        float[] values = { 2f, 3f, 4f };
        cost.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) cost.GetArrayElementAtIndex(i).floatValue = values[i];

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(thunder.gameObject.scene);
        Debug.Log("[ThunderStock] 이전 값으로 복구 · 뇌기 4칸, 일섬 2/3/4칸");
    }

    static void Set(SerializedObject so, string name, float value)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p != null) p.floatValue = value;
    }
}
