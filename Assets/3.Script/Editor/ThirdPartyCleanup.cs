using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 서드파티 정리 (git으로 되돌릴 수 있음)
//  - MasterStylizedProjectiles: 지금 쓰는 타격 이펙트 + 앞으로 쓸 후보만 남기고 지움
//    남길 프리팹과 그 의존성(머티리얼·텍스처·셰이더·스크립트), 팩 밖에서 참조하는 에셋은 모두 보존
//  - 젠이츠 칼: Player 프리팹에서 빼고 모델 폴더와 칼 전환용 셋업 스크립트를 지움 (Samurai 팩 칼로 확정)
//  - [변경] 안 쓰는 기능 정리: 저스트 회피 반격 · 손 축 보정(WeaponBoneRetarget) · 잔심 자세(Skill_Finish)의 데이터·상태·스크립트
// 먼저 "(미리보기)" 메뉴로 콘솔에서 지울 목록을 확인한 뒤 실행
public static class ThirdPartyCleanup
{
    const string PackRoot = "Assets/ThirdParty/MasterStylizedProjectiles";
    const string Fx = PackRoot + "/Projectiles/";

    static readonly string[] KeepPrefabs =
    {
        // 사용 중 (HitFeedback: 약타·강타·패링·플레이어 피격)
        Fx + "CyanBlueBullet/Prefabs/CyanBlueHit.prefab",
        Fx + "LightningExplosion/Prefabs/LightningExplosionHit.prefab",
        Fx + "EnergyExplosion/Prefabs/EnergyExplosionHit.prefab",
        Fx + "Shoot_Red/Prefabs/Par_RedShoot_Hit.prefab",
        // 후보: 벽력일섬 검기 · 베기 궤적 · 충격파 링 · 먼지
        Fx + "YellowSwordBeam/Prefabs/Par_YellowSwordBeam.prefab",
        Fx + "YellowSwordBeam/Prefabs/Par_YellowSwordBeam_Hit.prefab",
        Fx + "YellowSwordBeam/Prefabs/Par_YellowSwordBeam_Muzzle.prefab",
        Fx + "Common/Prefabs/Slash.prefab",
        Fx + "Common/Prefabs/Slash2.prefab",
        Fx + "Common/Prefabs/SlashLine.prefab",
        Fx + "Common/Prefabs/Ring.prefab",
        Fx + "Common/Prefabs/Ripple.prefab",
        Fx + "Common/Prefabs/Smoke.prefab",
    };

    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";
    const string ZenitsuFolder = "Assets/2.Model/Weapon/ZenitsuKatana";
    const string WeaponFolder = "Assets/2.Model/Weapon";
    static readonly string[] KatanaScripts =
    {
        "Assets/3.Script/Editor/KatanaEquipSetup.cs",
        "Assets/3.Script/Editor/SamuraiKatanaSetup.cs",
    };

    // [변경] 안 쓰는 기능
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    static readonly string[] UnusedStates = { "Dodge_Counter", "Skill_Finish" };
    static readonly string[] UnusedComponents = { "WeaponBoneRetarget" }; // 이름으로 찾음 (스크립트를 지운 뒤에도 이 파일이 컴파일되게)
    static readonly string[] UnusedAssets =
    {
        "Assets/11.Data/Attack/Dodge_Counter.asset",
        "Assets/3.Script/Editor/PlayerDodgeCounterSetup.cs",
        "Assets/3.Script/Editor/SkillFinishSetup.cs",
        "Assets/3.Script/Editor/SamuraiGripFix.cs",
        "Assets/3.Script/Player/WeaponBoneRetarget.cs",
    };

    [MenuItem("Tools/Cleanup/Trim Projectiles Pack (미리보기)")]
    static void TrimPreview() => Trim(false);

    [MenuItem("Tools/Cleanup/Trim Projectiles Pack")]
    static void TrimRun()
    {
        if (!EditorUtility.DisplayDialog("Projectiles 팩 정리", "남길 이펙트와 그 의존성만 두고 나머지를 지웁니다.\n(git으로 되돌릴 수 있음)", "정리", "취소")) return;
        Trim(true);
    }

    static void Trim(bool delete)
    {
        if (!AssetDatabase.IsValidFolder(PackRoot))
        {
            Debug.Log("[Cleanup] Projectiles 팩이 없습니다.");
            return;
        }

        HashSet<string> keep = CollectKeep();
        List<string> files = AssetDatabase.FindAssets("", new[] { PackRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !AssetDatabase.IsValidFolder(p))
            .Distinct()
            .ToList();
        List<string> remove = files.Where(p => !keep.Contains(p)).ToList();

        long bytes = remove.Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0L);
        Debug.Log($"[Cleanup] 팩 파일 {files.Count}개 중 보존 {files.Count - remove.Count}개 · 삭제 {remove.Count}개 ({bytes / 1048576f:0.0}MB)\n" +
                  "보존:\n" + string.Join("\n", files.Where(keep.Contains).OrderBy(p => p)));
        if (!delete) return;

        var failed = new List<string>();
        AssetDatabase.DeleteAssets(remove.ToArray(), failed);
        DeleteEmptyFolders(PackRoot);
        AssetDatabase.Refresh();
        Debug.Log(failed.Count == 0 ? "[Cleanup] Projectiles 팩 정리 완료" : "[Cleanup] 지우지 못한 파일:\n" + string.Join("\n", failed));
    }

    // 핵심: 남길 프리팹의 의존성 + 팩 밖 에셋(씬·프리팹·데이터 등)이 참조하는 팩 에셋
    static HashSet<string> CollectKeep()
    {
        var keep = new HashSet<string>();
        foreach (string prefab in KeepPrefabs)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefab) == null)
            {
                Debug.LogWarning($"[Cleanup] 남길 프리팹이 없습니다: {prefab}");
                continue;
            }
            keep.UnionWith(AssetDatabase.GetDependencies(prefab, true));
        }

        string[] outside = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/") && !p.StartsWith(PackRoot + "/") && !AssetDatabase.IsValidFolder(p))
            .Where(p => p.EndsWith(".unity") || p.EndsWith(".prefab") || p.EndsWith(".asset") || p.EndsWith(".mat") || p.EndsWith(".controller"))
            .ToArray();
        foreach (string dep in AssetDatabase.GetDependencies(outside, true))
            if (dep.StartsWith(PackRoot + "/")) keep.Add(dep);

        return keep;
    }

    static void DeleteEmptyFolders(string root)
    {
        foreach (string folder in AllFolders(root).OrderByDescending(f => f.Length))
            if (AssetDatabase.FindAssets("", new[] { folder }).Length == 0) AssetDatabase.DeleteAsset(folder);
        if (AssetDatabase.IsValidFolder(root) && AssetDatabase.FindAssets("", new[] { root }).Length == 0) AssetDatabase.DeleteAsset(root);
    }

    static IEnumerable<string> AllFolders(string root)
    {
        foreach (string sub in AssetDatabase.GetSubFolders(root))
        {
            foreach (string deeper in AllFolders(sub)) yield return deeper;
            yield return sub;
        }
    }

    [MenuItem("Tools/Cleanup/Remove Zenitsu Katana")]
    static void RemoveZenitsu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Cleanup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }
        if (!EditorUtility.DisplayDialog("젠이츠 칼 삭제", "Player 프리팹에서 ZenitsuKatana를 빼고, 모델 폴더와 칼 전환 셋업 스크립트(KatanaEquipSetup·SamuraiKatanaSetup)를 지웁니다.\n(git으로 되돌릴 수 있음)", "삭제", "취소")) return;

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform pack = SamuraiSkinSetup.FindDeep(root.transform, "SamuraiKatana");
            if (pack == null || !pack.gameObject.activeSelf)
            {
                Debug.LogError("[Cleanup] SamuraiKatana가 없거나 꺼져 있어 중단합니다. 손에 들 칼이 없어집니다.");
                return;
            }

            Transform zenitsu = SamuraiSkinSetup.FindDeep(root.transform, "ZenitsuKatana");
            if (zenitsu != null)
            {
                Object.DestroyImmediate(zenitsu.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log("[Cleanup] Player 프리팹에서 ZenitsuKatana 제거");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        if (AssetDatabase.IsValidFolder(ZenitsuFolder)) AssetDatabase.DeleteAsset(ZenitsuFolder);
        if (AssetDatabase.IsValidFolder(WeaponFolder) && AssetDatabase.FindAssets("", new[] { WeaponFolder }).Length == 0) AssetDatabase.DeleteAsset(WeaponFolder);

        // 스크립트 삭제는 재컴파일을 부르므로 마지막에
        foreach (string script in KatanaScripts)
            if (File.Exists(script)) AssetDatabase.DeleteAsset(script);
        Debug.Log("[Cleanup] 젠이츠 칼 모델·셋업 스크립트 삭제 완료");
    }

    // [변경] 핵심: 프리팹 컴포넌트·애니메이터 상태를 먼저 떼고, 스크립트·데이터 삭제는 마지막 (재컴파일 전에 참조를 정리)
    [MenuItem("Tools/Cleanup/Remove Unused Features")]
    static void RemoveUnusedFeatures()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Cleanup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }
        if (!EditorUtility.DisplayDialog("안 쓰는 기능 정리",
                "저스트 회피 반격 · 손 축 보정 · 잔심 자세의 애니메이터 상태, Player 컴포넌트, 데이터·셋업 스크립트를 지웁니다.\n(git으로 되돌릴 수 있음)", "정리", "취소")) return;

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            int removed = 0;
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || !UnusedComponents.Contains(mb.GetType().Name)) continue;
                Object.DestroyImmediate(mb);
                removed++;
            }
            if (removed > 0) PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log($"[Cleanup] Player 컴포넌트 {removed}개 제거");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller != null)
        {
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in sm.states.ToArray())
            {
                if (!UnusedStates.Contains(child.state.name)) continue;
                sm.RemoveState(child.state); // 그 상태로 가는 전환도 함께 지워짐
                Debug.Log($"[Cleanup] 애니메이터 상태 {child.state.name} 제거");
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        var failed = new List<string>();
        AssetDatabase.DeleteAssets(UnusedAssets.Where(p => File.Exists(p)).ToArray(), failed);
        Debug.Log(failed.Count == 0 ? "[Cleanup] 안 쓰는 기능 정리 완료" : "[Cleanup] 지우지 못한 파일:\n" + string.Join("\n", failed));
    }
}
