using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 적 스킨을 Samurai 팩 모델로 교체 ("사기에 물든 같은 문파 검객"). 플레이 모드가 아닐 때 한 번 실행
//  - Swordsman.prefab 하나만 바꾸면 변형(보스 · 정예 · 방패병 · 돌격병 · 수련 상대)이 모두 따라옴
//  - 종류별 색은 각 변형의 EnemySwordsman.bodyTint가 텍스처에 곱해져서 나옴 (보스 검붉음, 정예 적색, 방패병 청동, 돌격병 갈색)
//  - 플레이어와 구분되게 삿갓 · 활 · 가방 · 칼집 속 칼은 숨김
//  - 칼: 플레이어와 같은 보정(WeaponAdjust)과 칼 겉모습(SamuraiKatana · 잡는 자리 조정값)을 복사. 판정(Hitbox)은 원래 자리 그대로
//  - 플레이어 스킨(Tools/Setup/Samurai Player Skin)을 먼저 실행해 둔 상태여야 함 (아바타 · URP 머티리얼 재사용)
public static class SamuraiEnemySkin
{
    const string EnemyPrefabPath = "Assets/2.Model/Prefabs/Swordsman.prefab";
    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";
    const string FbxPath = "Assets/ThirdParty/Samurai/Mesh/SKM_Samurai.fbx";
    const string VendorPrefabPath = "Assets/ThirdParty/Samurai/Prefabs/SKM_Samurai.prefab";

    static readonly string[] HiddenParts = { "hat", "bow", "bag", "katana" };
    static readonly string[] MovedBones = { "weapon_r", "sword_holder" }; // 애니메이션이 직접 움직이는 무기 뼈 (새 손 · 골반 아래로 옮김)

    [MenuItem("Tools/Setup/Samurai Enemy Skin")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[EnemySkin] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError("[EnemySkin] Samurai 아바타가 없습니다. Tools/Setup/Samurai Player Skin을 먼저 실행하세요.");
            return;
        }

        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform playerAdjust = playerPrefab != null ? SamuraiSkinSetup.FindDeep(playerPrefab.transform, "WeaponAdjust") : null;
        Transform playerKatana = playerAdjust != null ? playerAdjust.Find("SamuraiKatana") : null;
        if (playerAdjust == null || playerKatana == null)
        {
            Debug.LogError("[EnemySkin] Player 프리팹에서 WeaponAdjust/SamuraiKatana를 찾지 못했습니다.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
        try
        {
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogError("[EnemySkin] Swordsman 프리팹에서 Animator를 찾지 못했습니다.");
                return;
            }
            Transform model = animator.transform;

            bool alreadySwapped = animator.avatar == avatar;
            if (!alreadySwapped && !SwapModel(root, model, playerAdjust, playerKatana)) return;
            animator.avatar = avatar;

            int hidden = HideParts(model);
            int swapped = ConvertMaterials(model);

            PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
            Debug.Log($"[EnemySkin] 완료 · {(alreadySwapped ? "이미 교체된 모델 갱신" : "뼈대 · 메시 교체")}, 머티리얼 {swapped}칸 URP로, 숨긴 파츠 {hidden}개 → 변형 프리팹에도 반영됨");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 핵심: 9CG 마네킹 → Samurai. 뼈 이름 · 축 체계가 같아(언리얼 마네킹) 애니메이션은 그대로 붙음
    static bool SwapModel(GameObject root, Transform model, Transform playerAdjust, Transform playerKatana)
    {
        // 모델이 FBX 프리팹 인스턴스라 자식을 지울 수 없음 → 풀어서 일반 오브젝트로
        if (PrefabUtility.IsPartOfPrefabInstance(model.gameObject))
        {
            GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(model.gameObject);
            PrefabUtility.UnpackPrefabInstance(instanceRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        Transform oldSkeleton = model.Find("root");
        Transform oldWeapon = oldSkeleton != null ? SamuraiSkinSetup.FindDeep(oldSkeleton, "weapon_r") : null;
        var vendor = AssetDatabase.LoadAssetAtPath<GameObject>(VendorPrefabPath);
        if (oldSkeleton == null || oldWeapon == null || vendor == null)
        {
            Debug.LogError("[EnemySkin] 기존 뼈대(root · weapon_r) 또는 SKM_Samurai 프리팹을 찾지 못했습니다.");
            return false;
        }

        // 판정은 weapon_r 기준 자리를 기억해 두고 잠시 밖으로 (옛 뼈대와 함께 지워지지 않게)
        WeaponHitbox hitbox = root.GetComponentInChildren<WeaponHitbox>(true);
        Matrix4x4 hitboxPose = Matrix4x4.identity;
        if (hitbox != null)
        {
            hitboxPose = oldWeapon.worldToLocalMatrix * hitbox.transform.localToWorldMatrix;
            hitbox.transform.SetParent(root.transform, true);
        }

        var temp = (GameObject)PrefabUtility.InstantiatePrefab(vendor, root.scene);
        PrefabUtility.UnpackPrefabInstance(temp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Transform newSkeleton = temp.transform.Find("root");
        if (newSkeleton == null)
        {
            Object.DestroyImmediate(temp);
            Debug.LogError("[EnemySkin] Samurai 모델에서 root 뼈를 찾지 못했습니다.");
            return false;
        }

        // 무기 뼈를 같은 이름의 새 부모(hand_r · pelvis) 아래로, 로컬 값 그대로
        foreach (string boneName in MovedBones)
        {
            Transform moved = SamuraiSkinSetup.FindDeep(oldSkeleton, boneName);
            if (moved == null) continue;
            Transform newParent = SamuraiSkinSetup.FindDeep(newSkeleton, moved.parent.name);
            if (newParent == null) continue;
            Vector3 position = moved.localPosition;
            Quaternion rotation = moved.localRotation;
            Vector3 scale = moved.localScale;
            moved.SetParent(newParent, false);
            moved.localPosition = position;
            moved.localRotation = rotation;
            moved.localScale = scale;
        }

        // 옛 칼 메시 등 weapon_r 아래 것은 지우고, 플레이어와 같은 보정 + 칼 겉모습으로
        foreach (Transform child in oldWeapon.Cast<Transform>().ToList()) Object.DestroyImmediate(child.gameObject);
        var adjust = new GameObject("WeaponAdjust").transform;
        adjust.SetParent(oldWeapon, false);
        adjust.localPosition = playerAdjust.localPosition;
        adjust.localRotation = playerAdjust.localRotation;
        adjust.localScale = playerAdjust.localScale;

        GameObject katana = Object.Instantiate(playerKatana.gameObject, adjust);
        katana.name = playerKatana.name;
        katana.transform.localPosition = playerKatana.localPosition;
        katana.transform.localRotation = playerKatana.localRotation;
        katana.transform.localScale = playerKatana.localScale;

        // 판정: 보정 오브젝트를 예전 weapon_r 자리로 보고 같은 상대 위치에 (플레이어와 같은 방식)
        if (hitbox != null)
        {
            hitbox.transform.SetParent(adjust, false);
            hitbox.transform.localPosition = hitboxPose.GetColumn(3);
            hitbox.transform.localRotation = hitboxPose.rotation;
            hitbox.transform.localScale = hitboxPose.lossyScale;
        }

        // 새 뼈대 · 메시를 모델 아래로 옮기고 옛 것은 삭제 (Animator 오브젝트는 그대로 → 참조 유지)
        List<Transform> oldChildren = model.Cast<Transform>().ToList();
        foreach (Transform child in temp.transform.Cast<Transform>().ToList()) child.SetParent(model, false);
        foreach (Transform old in oldChildren) Object.DestroyImmediate(old.gameObject);
        Object.DestroyImmediate(temp);
        return true;
    }

    static int HideParts(Transform model)
    {
        int hidden = 0;
        foreach (string part in HiddenParts)
        {
            Transform t = SamuraiSkinSetup.FindDeep(model, part);
            if (t == null || t.GetComponent<SkinnedMeshRenderer>() == null) continue;
            t.gameObject.SetActive(false);
            hidden++;
        }
        return hidden;
    }

    // 팩 머티리얼(Built-in) → 플레이어 스킨 때 만든 URP Lit 사본 재사용
    static int ConvertMaterials(Transform model)
    {
        int swapped = 0;
        foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] mats = smr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].shader == null || mats[i].shader.name.StartsWith("Universal Render Pipeline")) continue;
                mats[i] = SamuraiSkinSetup.GetOrCreateLit(mats[i]);
                swapped++;
            }
            smr.sharedMaterials = mats;
        }
        AssetDatabase.SaveAssets();
        return swapped;
    }
}
