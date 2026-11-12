using System.Linq;
using UnityEditor;
using UnityEngine;

// Tools/Setup/Player Ultimate — Player 프리팹에 PlayerUltimate(궁극기, V)를 붙이고 번개 프리팹 · 소리를 연결 (다시 실행해도 됨, 비어 있는 칸만 채움)
public static class PlayerUltimateSetup
{
    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";
    const string SfxFolder = "Assets/8.Audio";

    [MenuItem("Tools/Setup/Player Ultimate")]
    static void Run()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
        {
            Debug.LogError($"[Ultimate] {PlayerPrefabPath}가 없습니다.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            PlayerUltimate ultimate = root.GetComponent<PlayerUltimate>();
            if (ultimate == null) ultimate = root.AddComponent<PlayerUltimate>();
            var so = new SerializedObject(ultimate);

            // 번개 프리팹은 PlayerVfx와 같은 것
            PlayerVfx vfx = root.GetComponentInChildren<PlayerVfx>(true);
            if (vfx != null && so.FindProperty("boltPrefab").objectReferenceValue == null)
                so.FindProperty("boltPrefab").objectReferenceValue = new SerializedObject(vfx).FindProperty("boltPrefab").objectReferenceValue;

            SerializedProperty thunder = so.FindProperty("thunderClips");
            if (thunder.arraySize == 0)
            {
                AudioClip[] clips = Clips("skill_strike_");
                thunder.arraySize = clips.Length;
                for (int i = 0; i < clips.Length; i++) thunder.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }
            FillClip(so, "slashClip", "dash_cut_01");
            FillClip(so, "chargeClip", "stance_charge_start");
            FillClip(so, "clashClip", "parry_clash");
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log($"[Ultimate] 완료 · 번개 {(so.FindProperty("boltPrefab").objectReferenceValue != null ? "연결" : "없음")} · 낙뢰음 {thunder.arraySize}개 → {PlayerPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void FillClip(SerializedObject so, string field, string name)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p.objectReferenceValue != null) return;
        AudioClip clip = Clips(name).FirstOrDefault();
        if (clip != null) p.objectReferenceValue = clip;
    }

    static AudioClip[] Clips(string prefix) =>
        AssetDatabase.FindAssets("t:AudioClip", new[] { SfxFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => System.IO.Path.GetFileNameWithoutExtension(p).StartsWith(prefix))
            .OrderBy(p => p)
            .Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p))
            .Where(c => c != null)
            .ToArray();
}
