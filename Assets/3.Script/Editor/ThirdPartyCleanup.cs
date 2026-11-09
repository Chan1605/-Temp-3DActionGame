using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 서드파티 정리 (git으로 되돌릴 수 있음)
//  - MasterStylizedProjectiles: 지금 쓰는 타격 이펙트 + 앞으로 쓸 후보만 남기고 지움
//    남길 프리팹과 그 의존성(머티리얼·텍스처·셰이더·스크립트), 팩 밖에서 참조하는 에셋은 모두 보존
//  - 젠이츠 칼: Player 프리팹에서 빼고 모델 폴더와 칼 전환용 셋업 스크립트를 지움 (Samurai 팩 칼로 확정)
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
        // 후보: 발도 자세 차징 · 벽력일섬 검기 · 베기 궤적 · 충격파 링 · 먼지
        Fx + "LightningExplosion/Prefabs/LightningExplosionCharge.prefab",
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
}
