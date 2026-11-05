using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 평상시 / 전투 자세: Locomotion 블렌드 트리의 칸(대기·걷기·달리기·질주)마다 Stance(0 평상시, 1 전투) 블렌드 트리를 넣음
// + 검 집어넣기 상태(Sheathe) + PlayerStance 컴포넌트
// 다시 실행해도 이미 나뉜 칸은 그대로 둠 (애니메이션 고르기 창에서 바꾼 클립 보존)
public static class PlayerStanceSetup
{
    const string AnimRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string StanceParam = "Stance";
    const string SheatheState = "Sheathe";
    const string SheatheClip = AnimRoot + "01_Idle/Idle_Combat_To_Idle.anim";
    // [변경] 회피 4방향: 평상시 = 검을 넣은 회피, 전투 = 지금 쓰는 클립
    const string RelaxedDodgeRoot = AnimRoot + "06_Dodge/01_Dodge/";
    static readonly string[] DodgeStates = { "Dodge_F", "Dodge_B", "Dodge_L", "Dodge_R" };

    // 칸 순서대로 (평상시, 전투) 기본 클립. 전투 칸은 지금 트리에 들어 있는 클립을 우선 사용 (대기 칸 제외)
    static readonly (string name, string relaxed, string combat)[] Slots =
    {
        ("Idle", AnimRoot + "01_Idle/Idle.anim", AnimRoot + "01_Idle/Idle_Combat.anim"),
        ("Walk", AnimRoot + "03_Walk/01_Walk_RM/01_Walk_F_0_RM/Walk_Loop_F_0_RM.anim", AnimRoot + "03_Walk/02_Walk_Combat_RM/01_Walk_Combat_F_0_RM/Walk_Combat_Loop_F_0_RM.anim"),
        ("Run", AnimRoot + "04_Run/01_Run_RM/01_Run_F_0_RM/Run_Loop_F_0_RM.anim", AnimRoot + "04_Run/02_Run_Combat_RM/01_Run_Combat_F_0_RM/Run_Combat_Loop_F_0_RM.anim"),
        ("Sprint", AnimRoot + "04_Run/01_Run_RM/11_Run_Fast_RM/Run_Fast_Loop_RM.anim", AnimRoot + "04_Run/02_Run_Combat_RM/11_Run_Fast_Combat_RM/Run_Fast_Combat_Loop_RM.anim"),
    };

    [MenuItem("Tools/Setup/Player Stance (Idle)")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[StanceSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        AnimationClip sheathe = Clip(SheatheClip, false);
        if (controller == null || locomotion == null || sheathe == null)
        {
            Debug.LogError("[StanceSetup] Player.controller · 씬의 PlayerLocomotion · 집어넣기 클립 중 하나가 없습니다.");
            return;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState loco = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Locomotion");
        if (loco == null || !(loco.motion is BlendTree oldTree))
        {
            Debug.LogError("[StanceSetup] Locomotion 블렌드 트리가 없습니다. Player Locomotion 셋업을 먼저 실행하세요.");
            return;
        }

        if (Slots.Any(s => AssetDatabase.LoadAssetAtPath<AnimationClip>(s.relaxed) == null || AssetDatabase.LoadAssetAtPath<AnimationClip>(s.combat) == null))
        {
            Debug.LogError("[StanceSetup] 평상시/전투 기본 클립 중 찾지 못한 것이 있습니다. ThirdParty 파일이 복원됐는지 확인하세요.");
            return;
        }

        if (!controller.parameters.Any(p => p.name == StanceParam))
            controller.AddParameter(StanceParam, AnimatorControllerParameterType.Float);

        // 1) Locomotion: MoveBlend 칸마다 Stance 트리
        ChildMotion[] oldChildren = oldTree.children.OrderBy(c => c.threshold).ToArray();
        var top = NewTree(controller, "Locomotion", oldTree.blendParameter);
        var removed = new List<BlendTree> { oldTree };
        for (int i = 0; i < oldChildren.Length; i++)
        {
            Motion motion = oldChildren[i].motion;
            if (motion is BlendTree nested && nested.blendParameter == StanceParam)
            {
                top.AddChild(nested, oldChildren[i].threshold); // 이미 나뉜 칸은 그대로 옮김
                continue;
            }

            var slot = Slots[Mathf.Min(i, Slots.Length - 1)];
            // 대기 칸은 사용자가 평상시 대기로 바꿔 둔 상태라 전투 대기는 기본 경로로, 나머지는 지금 클립을 전투 칸으로
            AnimationClip combat = i == 0 ? Clip(slot.combat, true) : (motion as AnimationClip) ?? Clip(slot.combat, true);
            AnimationClip relaxed = Clip(slot.relaxed, true);

            BlendTree stance = NewTree(controller, slot.name, StanceParam);
            stance.AddChild(relaxed, 0f);
            stance.AddChild(combat, 1f);
            top.AddChild(stance, oldChildren[i].threshold);
        }
        loco.motion = top;
        foreach (BlendTree tree in removed)
            if (AssetDatabase.GetAssetPath(tree) == ControllerPath) AssetDatabase.RemoveObjectFromAsset(tree);

        // [변경] 1-2) 회피 상태도 자세별로 (이미 나뉘었으면 그대로)
        foreach (string name in DodgeStates)
        {
            AnimatorState dodge = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
            if (dodge == null || !(dodge.motion is AnimationClip combatDodge)) continue;
            AnimationClip relaxedDodge = Clip(RelaxedDodgeRoot + name + ".anim", false);
            if (relaxedDodge == null) continue;

            BlendTree stance = NewTree(controller, name, StanceParam);
            stance.AddChild(relaxedDodge, 0f);
            stance.AddChild(combatDodge, 1f);
            dodge.motion = stance;
        }

        // 2) 집어넣기 상태 (전환은 PlayerStance가 코드로)
        AnimatorState sheatheState = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == SheatheState) ?? sm.AddState(SheatheState);
        sheatheState.motion = sheathe;
        EditorUtility.SetDirty(controller);

        // 3) 컴포넌트
        var stanceComp = locomotion.GetComponent<PlayerStance>();
        if (stanceComp == null) stanceComp = Undo.AddComponent<PlayerStance>(locomotion.gameObject);
        var so = new SerializedObject(stanceComp);
        so.FindProperty("animator").objectReferenceValue = locomotion.GetComponentInChildren<Animator>();
        so.FindProperty("locomotion").objectReferenceValue = locomotion;
        so.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(ControllerPath);
        EditorSceneManager.MarkSceneDirty(locomotion.gameObject.scene);
        Debug.Log("[StanceSetup] 완료 · Locomotion·회피 칸마다 평상시/전투 클립, Sheathe 상태, PlayerStance 추가"); // [변경]
    }

    static BlendTree NewTree(AnimatorController controller, string name, string parameter)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = parameter,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    static AnimationClip Clip(string path, bool loop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            Debug.LogError($"[StanceSetup] {path}를 찾지 못했습니다.");
            return null;
        }
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime != loop)
        {
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }
        return clip;
    }
}
