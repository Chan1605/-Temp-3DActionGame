using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class KanpaSetup
{
    [MenuItem("Tools/Setup/Kanpa")]
    static void Run()
    {
        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        if (combat == null)
        {
            Debug.LogError("[KanpaSetup] PlayerCombat이 없습니다.");
            return;
        }

        GameObject player = combat.gameObject;
        PlayerKanpa kanpa = player.GetComponent<PlayerKanpa>();
        if (kanpa == null) kanpa = Undo.AddComponent<PlayerKanpa>(player);

        SetRefs(kanpa, ("combat", combat), ("afterimage", player.GetComponent<PlayerAfterimage>()));

        // 간파를 받아야 하는 쪽: 피격 판정 · 우클릭 입력 · 스파크 · 뇌인/뇌기 · 연출
        SetRefs(player.GetComponent<PlayerHealth>(), ("kanpa", kanpa));
        SetRefs(player.GetComponent<PlayerChargeAttack>(), ("kanpa", kanpa));
        SetRefs(player.GetComponent<HitFeedback>(), ("kanpa", kanpa));
        SetRefs(player.GetComponent<PlayerThunder>(), ("kanpa", kanpa));
        SetRefs(player.GetComponent<ParryFx>(), ("kanpa", kanpa));

        if (player.GetComponent<ParryFx>() == null)
            Debug.LogWarning("[KanpaSetup] ParryFx가 없어 간파 연출이 나오지 않습니다. Tools/Setup/Parry Fx (v2)를 먼저 실행하세요.");

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log("[KanpaSetup] 완료 · 콤보 중 △ 초반에 적 공격이 닿으면 간파");
    }

    static void SetRefs(Object target, params (string field, Object value)[] refs)
    {
        if (target == null) return;

        var so = new SerializedObject(target);
        foreach ((string field, Object value) in refs)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null) prop.objectReferenceValue = value;
            else Debug.LogWarning($"[KanpaSetup] {target.GetType().Name}에 '{field}' 필드가 없습니다.");
        }
        so.ApplyModifiedProperties();
    }
}
