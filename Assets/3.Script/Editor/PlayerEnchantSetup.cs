using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 뇌검 각성(R) 셋업: 플레이어(프리팹이면 프리팹 원본)에 PlayerEnchant를 붙임. 참조는 실행 중 자동으로 찾음
public static class PlayerEnchantSetup
{
    [MenuItem("Tools/Setup/Player Enchant (R)")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[EnchantSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        if (combat == null)
        {
            Debug.LogError("[EnchantSetup] 씬에 PlayerCombat이 없습니다.");
            return;
        }

        GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(combat.gameObject);
        string path = root != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) : null;

        if (!string.IsNullOrEmpty(path))
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            PlayerCombat inPrefab = contents.GetComponentInChildren<PlayerCombat>(true);
            if (inPrefab != null && inPrefab.GetComponent<PlayerEnchant>() == null) inPrefab.gameObject.AddComponent<PlayerEnchant>();
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            Debug.Log($"[EnchantSetup] 완료 · {path}에 PlayerEnchant 추가 (R = 뇌검 각성)");
            return;
        }

        if (combat.GetComponent<PlayerEnchant>() == null) Undo.AddComponent<PlayerEnchant>(combat.gameObject);
        EditorSceneManager.MarkSceneDirty(combat.gameObject.scene);
        Debug.Log("[EnchantSetup] 완료 · 씬의 플레이어에 PlayerEnchant 추가 (R = 뇌검 각성)");
    }
}
