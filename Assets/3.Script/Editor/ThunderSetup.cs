using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// P4·P5 연결: 적 프리팹에 뇌인(ThunderMark), 플레이어에 뇌기(PlayerThunder)를 붙이고 일섬·간파·HUD에 연결
public static class ThunderSetup
{
    const string EnemyPrefabPath = "Assets/2.Model/Prefabs/Swordsman.prefab";

    [MenuItem("Tools/Setup/Thunder (P4)")]
    static void Run()
    {
        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        if (combat == null)
        {
            Debug.LogError("[ThunderSetup] 씬에 PlayerCombat이 없습니다.");
            return;
        }

        bool enemyReady = AddMarkToEnemyPrefab();

        GameObject player = combat.gameObject;
        PlayerThunder thunder = player.GetComponent<PlayerThunder>();
        if (thunder == null) thunder = Undo.AddComponent<PlayerThunder>(player);

        SetRefs(thunder,
            ("weapon", player.GetComponentInChildren<WeaponHitbox>(true)),
            ("combat", combat),
            ("parry", player.GetComponent<PlayerParry>()),
            ("dodge", player.GetComponent<PlayerDodge>()));
        SetRefs(player.GetComponent<PlayerSkill>(), ("thunder", thunder));
        SetRefs(player.GetComponent<PlayerParry>(), ("thunder", thunder));
        SetRefs(Object.FindAnyObjectByType<GameHUD>(), ("thunderSource", thunder));

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log($"[ThunderSetup] 완료 · 플레이어 뇌기 연결{(enemyReady ? ", 검병 프리팹 뇌인 추가" : "")}");
    }

    static bool AddMarkToEnemyPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath) == null)
        {
            Debug.LogWarning($"[ThunderSetup] {EnemyPrefabPath}가 없어 적 뇌인을 건너뜁니다.");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
        if (root.GetComponent<ThunderMark>() == null) root.AddComponent<ThunderMark>();
        PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        return true;
    }

    static void SetRefs(Object target, params (string field, Object value)[] refs)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        foreach ((string field, Object value) in refs)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null) prop.objectReferenceValue = value;
            else Debug.LogWarning($"[ThunderSetup] {target.GetType().Name}에 '{field}' 필드가 없습니다.");
        }
        so.ApplyModifiedProperties();
    }
}
