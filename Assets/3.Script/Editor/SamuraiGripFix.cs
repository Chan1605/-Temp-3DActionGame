using UnityEditor;
using UnityEngine;

// 칼 쥔 자세 보정 (다시 실행해도 됨)
// 애니메이션 팩 클립은 weapon_r(칼 쥔 자리)을 손 뼈 기준으로 움직이는데, 그 값은 예전 모델(9CG_Sword)의 손 뼈 축 기준
// → 두 모델에 같은 클립(Idle_Combat)을 샘플링해 "예전 손 축 → 새 손 축" 회전을 구하고, Player의 WeaponBoneRetarget에 넣음
//   런타임에 애니메이터가 쓴 weapon_r 값 앞에 곱해지므로 털기·납도처럼 손 안에서 칼이 도는 동작도 예전과 같게 나옴
// 예전 방식(weapon_r/WeaponAdjust 고정 회전)은 대기 자세에만 맞아서, WeaponAdjust는 0으로 되돌리고 미세 조정용으로만 남김
public static class SamuraiGripFix
{
    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";
    const string NewModelPath = "Assets/ThirdParty/Samurai/Mesh/SKM_Samurai.fbx";
    const string OldModelGuid = "a9dd6fa1e9a629744b675820bae11a76"; // 예전 플레이어 모델 (9CG_Sword)
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string ClipPath = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/01_Idle/Idle_Combat.anim";
    const string AdjustName = "WeaponAdjust";

    [MenuItem("Tools/Setup/Samurai Weapon Grip Fix")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[GripFix] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var oldModel = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(OldModelGuid));
        var newModel = AssetDatabase.LoadAssetAtPath<GameObject>(NewModelPath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (oldModel == null || newModel == null || clip == null)
        {
            Debug.LogError("[GripFix] 예전 모델(9CG_Sword) · Samurai 모델 · Idle_Combat 클립 중 없는 것이 있습니다.");
            return;
        }

        if (!MeasureHandOffset(oldModel, newModel, clip, controller, out Quaternion offset)) return;
        ApplyToPrefab(offset);
    }

    // 핵심: 같은 자세에서 두 손 뼈의 월드 회전 차이 = 새 손 축 기준으로 본 예전 손 축
    static bool MeasureHandOffset(GameObject oldModel, GameObject newModel, AnimationClip clip, RuntimeAnimatorController controller, out Quaternion offset)
    {
        offset = Quaternion.identity;
        GameObject a = Spawn(oldModel, controller);
        GameObject b = Spawn(newModel, controller);
        try
        {
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(a, clip, 0f);
            AnimationMode.SampleAnimationClip(b, clip, 0f);
            AnimationMode.EndSampling();

            Transform oldHand = SamuraiSkinSetup.FindDeep(a.transform, "hand_r");
            Transform newHand = SamuraiSkinSetup.FindDeep(b.transform, "hand_r");
            if (oldHand == null || newHand == null)
            {
                Debug.LogError("[GripFix] hand_r 뼈를 찾지 못했습니다.");
                return false;
            }

            offset = Quaternion.Inverse(newHand.rotation) * oldHand.rotation;
            Debug.Log($"[GripFix] 손 축 보정 {offset.eulerAngles}");
            return true;
        }
        finally
        {
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    static GameObject Spawn(GameObject model, RuntimeAnimatorController controller)
    {
        var go = (GameObject)Object.Instantiate(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Animator animator = go.GetComponent<Animator>();
        if (animator == null) animator = go.AddComponent<Animator>();
        if (animator.runtimeAnimatorController == null) animator.runtimeAnimatorController = controller;
        return go;
    }

    static void ApplyToPrefab(Quaternion offset)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform weapon = SamuraiSkinSetup.FindDeep(root.transform, "weapon_r");
            if (weapon == null)
            {
                Debug.LogError("[GripFix] Player 프리팹에서 weapon_r을 찾지 못했습니다.");
                return;
            }

            var retarget = root.GetComponent<WeaponBoneRetarget>();
            if (retarget == null) retarget = root.AddComponent<WeaponBoneRetarget>();
            var so = new SerializedObject(retarget);
            so.FindProperty("bone").objectReferenceValue = weapon;
            so.FindProperty("handRotationOffset").quaternionValue = offset;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 예전 고정 회전 보정은 끔 (남겨 두면 두 번 돌아감). 노드는 미세 조정용으로 유지
            Transform adjust = weapon.Find(AdjustName);
            if (adjust != null)
            {
                Debug.Log($"[GripFix] {AdjustName} 회전 {adjust.localEulerAngles} → 0 (손 축 보정으로 대체)");
                adjust.localPosition = Vector3.zero;
                adjust.localRotation = Quaternion.identity;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log("[GripFix] 완료 · Player에 WeaponBoneRetarget 적용");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
