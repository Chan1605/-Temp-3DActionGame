using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 일섬 밸런스 값을 PlayerSkill 컴포넌트 → SkillData 에셋(ThunderFlash.asset)으로 옮김 (한 번만 실행)
// 열린 씬의 Player 값을 우선 쓰고, 씬에 없으면 Player 프리팹 값을 씀. 같은 이름의 칸끼리 그대로 복사
public static class SkillBalanceMigration
{
    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";

    static readonly string[] Fields =
    {
        "tapChainCost",
        "stanceCost",
        "stanceEnterTime",
        "stanceDuration",
        "stanceEnemyScale",
        "stanceRange",
        "markTime",
        "reticleRadius",
        "maxMarks",
        "baseMarks",
        "enchantMaxMarks",
        "stanceChainDelay",
        "dodgeStanceWindow",
        "dodgeStanceCost",
        "dodgeAutoMarks",
        "dodgeAutoRange",
        "dodgeDrawDelay",
        "enchantStanceSeconds",
        "freezeTime",
        "lastFreezeTime",
        "freezeWorldScale",
        "freezeBudget",
        "holdFreezeScale",
        "enchantFreezeScale",
        "dodgeFreezeScale",
        "tapFreezeScale",
        "releaseShake",
        "lastFreezePunch",
    };

    [MenuItem("Tools/Setup/Move Skill Balance To Data")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[SkillBalance] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        PlayerSkill skill = Object.FindAnyObjectByType<PlayerSkill>(FindObjectsInactive.Include);
        string source = skill != null ? $"씬 {skill.gameObject.scene.name}" : "Player 프리팹";
        if (skill == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab != null) skill = prefab.GetComponentInChildren<PlayerSkill>(true);
        }
        if (skill == null)
        {
            Debug.LogError("[SkillBalance] PlayerSkill을 찾지 못했습니다.");
            return;
        }

        var from = new SerializedObject(skill);
        var data = from.FindProperty("data").objectReferenceValue as SkillData;
        if (data == null)
        {
            Debug.LogError("[SkillBalance] PlayerSkill에 SkillData가 연결돼 있지 않습니다.");
            return;
        }

        var to = new SerializedObject(data);
        var log = new StringBuilder();
        int copied = 0;
        foreach (string name in Fields)
        {
            SerializedProperty a = from.FindProperty(name);
            SerializedProperty b = to.FindProperty(name);
            if (a == null || b == null || a.propertyType != b.propertyType) continue;
            if (a.propertyType == SerializedPropertyType.Float) b.floatValue = a.floatValue;
            else if (a.propertyType == SerializedPropertyType.Integer) b.intValue = a.intValue;
            else continue;
            copied++;
            log.AppendLine($"  {name} = {(a.propertyType == SerializedPropertyType.Float ? a.floatValue.ToString("0.###") : a.intValue.ToString())}");
        }
        to.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SkillBalance] {source}의 값 {copied}개를 {AssetDatabase.GetAssetPath(data)}로 옮김\n{log}");
        Selection.activeObject = data;
    }
}
