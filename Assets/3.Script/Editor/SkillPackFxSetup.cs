using UnityEditor;
using UnityEngine;

// Player에 SkillPackFx를 붙이고 Projectiles 팩 프리팹을 연결 (다시 실행해도 됨, 조절한 크기·높이는 유지)
public static class SkillPackFxSetup
{
    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";
    const string Fx = "Assets/ThirdParty/MasterStylizedProjectiles/Projectiles/";
    const string MuzzlePath = Fx + "YellowSwordBeam/Prefabs/Par_YellowSwordBeam_Muzzle.prefab";
    const string BeamPath = Fx + "YellowSwordBeam/Prefabs/Par_YellowSwordBeam.prefab";
    const string HitPath = Fx + "YellowSwordBeam/Prefabs/Par_YellowSwordBeam_Hit.prefab";

    [MenuItem("Tools/Setup/Skill Pack Fx")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[SkillPackFx] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            PlayerSkill skill = root.GetComponent<PlayerSkill>();
            if (skill == null)
            {
                Debug.LogError("[SkillPackFx] Player에 PlayerSkill이 없습니다.");
                return;
            }

            SkillPackFx fx = root.GetComponent<SkillPackFx>();
            if (fx == null) fx = root.AddComponent<SkillPackFx>();

            var so = new SerializedObject(fx);
            so.FindProperty("skill").objectReferenceValue = skill;
            // [변경] 발도 자세 차징 연결 삭제
            Assign(so, "dashMuzzle", MuzzlePath);
            Assign(so, "dashBeam", BeamPath);
            Assign(so, "cutHit", HitPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log("[SkillPackFx] 완료 · Player에 검기·타격 이펙트 연결");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void Assign(SerializedObject so, string field, string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) Debug.LogWarning($"[SkillPackFx] 프리팹이 없어 {field}는 비워 둡니다: {path}");
        so.FindProperty(field).objectReferenceValue = prefab;
    }
}
