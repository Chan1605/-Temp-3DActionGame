using UnityEditor;
using UnityEngine;

// 칼 잡는 자리 · 길이 조정기 붙이기 + 플레이 중 맞춘 값을 Player 프리팹에 저장
public static class KatanaGripSetup
{
    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";
    static readonly string[] SwordNames = { "SamuraiKatana", "ZenitsuKatana" }; // 팩 칼 · 원래 칼

    [MenuItem("Tools/Setup/Katana/Add Grip Tuner")]
    static void AddTuner()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Katana] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            int added = 0;
            foreach (string name in SwordNames)
            {
                Transform sword = FindDeep(root.transform, name);
                if (sword == null || sword.GetComponent<KatanaGrip>() != null) continue;
                sword.gameObject.AddComponent<KatanaGrip>();
                added++;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log($"[Katana] 조정기 {added}개 추가 → 플레이 중 Player의 칼 오브젝트(SamuraiKatana)를 골라 슬라이더로 맞춘 뒤 'Player 프리팹에 저장'");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 플레이 중 값은 끝나면 사라지므로, 같은 이름의 칼 오브젝트를 프리팹에서 찾아 값만 옮겨 저장
    public static void SaveToPrefab(KatanaGrip grip)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform sword = FindDeep(root.transform, grip.gameObject.name);
            KatanaGrip target = sword != null ? sword.GetComponent<KatanaGrip>() : null;
            if (target == null)
            {
                Debug.LogError($"[Katana] 프리팹에서 {grip.gameObject.name}의 KatanaGrip을 찾지 못했습니다. Tools/Setup/Katana/Add Grip Tuner를 먼저 실행하세요.");
                return;
            }
            grip.CopyValuesTo(target);
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log($"[Katana] {grip.gameObject.name} 조정값을 Player 프리팹에 저장했습니다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}

[CustomEditor(typeof(KatanaGrip))]
public class KatanaGripEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 중에 슬라이더를 움직이면 바로 보입니다. 맞춘 뒤 아래 버튼으로 저장하세요.", MessageType.Info);
            return;
        }
        if (GUILayout.Button("Player 프리팹에 저장", GUILayout.Height(28f)))
            KatanaGripSetup.SaveToPrefab((KatanaGrip)target);
    }
}
