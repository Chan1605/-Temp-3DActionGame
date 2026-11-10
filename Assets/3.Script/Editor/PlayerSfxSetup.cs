using System.Linq;
using UnityEditor;
using UnityEngine;

// Player에 PlayerSfx를 붙이고 Assets/8.Audio/SFX/Player의 소리를 연결 (다시 실행해도 됨, 볼륨 등 조절값은 유지)
//  - sword_swing_*.wav 전부 → 적중 소리 변형
//  - stance_charge_start / stance_charge_loop → 발도 자세 진입 / Q 차지 반복
// [변경] 진입음은 처음 실행할 때만 연결 (인스펙터에서 바꾼 소리를 덮어쓰지 않음)
public static class PlayerSfxSetup
{
    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";
    const string Folder = "Assets/8.Audio/SFX/Player";

    [MenuItem("Tools/Setup/Player Sfx")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[PlayerSfx] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        AudioClip[] swings = AssetDatabase.FindAssets("sword_swing_ t:AudioClip", new[] { Folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
            .Where(c => c != null)
            .ToArray();
        var start = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/stance_charge_start.wav");
        var loop = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/stance_charge_loop.wav");

        // 반복음은 메모리에 풀어 두고(끊김 방지), 짧은 휘두르기도 바로 재생되게
        foreach (AudioClip clip in swings.Append(start).Append(loop).Where(c => c != null))
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
            if (importer == null) continue;
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = s;
            importer.SaveAndReimport();
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            PlayerSfx sfx = root.GetComponent<PlayerSfx>();
            if (sfx == null) sfx = root.AddComponent<PlayerSfx>();

            var so = new SerializedObject(sfx);
            so.FindProperty("skill").objectReferenceValue = root.GetComponent<PlayerSkill>();
            so.FindProperty("weapon").objectReferenceValue = root.GetComponentInChildren<WeaponHitbox>(true); // [변경] 적중 소리용 (공격 시작·공속 연결 삭제)

            SerializedProperty array = so.FindProperty("swingClips");
            array.arraySize = swings.Length;
            for (int i = 0; i < swings.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = swings[i];
            SerializedProperty startProp = so.FindProperty("stanceStart");
            if (startProp.objectReferenceValue == null) startProp.objectReferenceValue = start;
            so.FindProperty("stanceLoop").objectReferenceValue = loop;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log($"[PlayerSfx] 완료 · 휘두르기 {swings.Length}개, 발도 진입 {(start != null ? "O" : "X")}, 반복 {(loop != null ? "O" : "X")}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
