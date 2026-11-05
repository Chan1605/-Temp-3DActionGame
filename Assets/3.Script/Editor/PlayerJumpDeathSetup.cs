using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 점프 자세 분리 + 이단 점프 + 사망 모션 (Player.controller)
//  - Jump_Start / Loop / Land: 평상시(Stance 0) · 전투(Stance 1) 블렌드. 전투 칸은 지금 쓰는 클립
//  - Double_Jump(제자리) · Double_Jump_F(이동 중): 자세별 이단 점프, 끝나면 Jump_Loop로
//  - Death_KnockDown(강공격에 쓰러짐) · Death_Combat(일반 사망)
// 다시 실행해도 이미 나뉜 상태는 그대로 둠 (애니메이션 고르기 창에서 바꾼 클립 보존)
public static class PlayerJumpDeathSetup
{
    const string AnimRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/";
    const string JumpRoot = AnimRoot + "05_Jump/01_Jump/01_Jump_0/";
    const string DoubleRoot = AnimRoot + "05_Jump/03_Double_Jump/";
    const string DoubleCombatRoot = AnimRoot + "05_Jump/04_Double_Jump_Combat/";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string StanceParam = "Stance";

    // 상태, 평상시 클립, 루프
    static readonly (string state, string relaxed, bool loop)[] JumpStates =
    {
        ("Jump_Start", JumpRoot + "Jump_Start_0.anim", false),
        ("Jump_Loop", JumpRoot + "Jump_Loop_0.anim", true),
        ("Jump_Land", JumpRoot + "Jump_Stop_0.anim", false),
    };

    // 상태, 평상시 클립, 전투 클립
    static readonly (string state, string relaxed, string combat)[] DoubleStates =
    {
        ("Double_Jump", DoubleRoot + "Double_Jump_0.anim", DoubleCombatRoot + "Double_Jump_Combat_0.anim"),
        ("Double_Jump_F", DoubleRoot + "Double_Jump_F_0.anim", DoubleCombatRoot + "Double_Jump_Combat_F_0.anim"),
    };

    static readonly (string state, string clip)[] DeathStates =
    {
        ("Death_KnockDown", AnimRoot + "08_Hit/10_Knock_Down/Knock_Down_Death.anim"),
        ("Death_Combat", AnimRoot + "08_Hit/02_Hit_Combat/Hit_Combat_Death.anim"),
    };

    [MenuItem("Tools/Setup/Player Jump + Death")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[JumpDeathSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || !controller.parameters.Any(p => p.name == StanceParam))
        {
            Debug.LogError("[JumpDeathSetup] Player.controller 또는 Stance 파라미터가 없습니다. Tools/Setup/Player Stance (Idle)을 먼저 실행하세요.");
            return;
        }

        var paths = JumpStates.Select(j => j.relaxed)
            .Concat(DoubleStates.SelectMany(d => new[] { d.relaxed, d.combat }))
            .Concat(DeathStates.Select(d => d.clip));
        string missing = paths.FirstOrDefault(p => AssetDatabase.LoadAssetAtPath<AnimationClip>(p) == null);
        if (missing != null)
        {
            Debug.LogError($"[JumpDeathSetup] {missing}를 찾지 못했습니다.");
            return;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // 1) 기존 점프 3단계를 자세별로
        foreach (var (stateName, relaxedPath, loop) in JumpStates)
        {
            AnimatorState state = Find(sm, stateName);
            if (state == null)
            {
                Debug.LogWarning($"[JumpDeathSetup] {stateName} 상태가 없어 건너뜁니다. (Player Jump and Guard 셋업)");
                continue;
            }
            if (!(state.motion is AnimationClip combat)) continue; // 이미 나뉨
            state.motion = StanceTree(controller, stateName, Clip(relaxedPath, loop), combat);
        }

        // 2) 이단 점프 (끝나면 Jump_Loop로 이어서 떨어짐)
        AnimatorState jumpLoop = Find(sm, "Jump_Loop");
        foreach (var (stateName, relaxedPath, combatPath) in DoubleStates)
        {
            AnimatorState state = Find(sm, stateName) ?? sm.AddState(stateName);
            if (!(state.motion is BlendTree))
                state.motion = StanceTree(controller, stateName, Clip(relaxedPath, false), Clip(combatPath, false));

            if (jumpLoop != null && !state.transitions.Any(t => t.destinationState == jumpLoop))
            {
                AnimatorStateTransition t = state.AddTransition(jumpLoop);
                t.hasExitTime = true;
                t.exitTime = 0.9f;
                t.duration = 0.15f;
                t.hasFixedDuration = true;
            }
        }

        // 3) 사망 (1회 재생 후 마지막 자세 유지)
        foreach (var (stateName, clipPath) in DeathStates)
        {
            AnimatorState state = Find(sm, stateName) ?? sm.AddState(stateName);
            if (state.motion == null) state.motion = Clip(clipPath, false);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(ControllerPath);
        Debug.Log("[JumpDeathSetup] 완료 · 점프 자세 분리, Double_Jump / Double_Jump_F, Death_KnockDown / Death_Combat");
    }

    static BlendTree StanceTree(AnimatorController controller, string name, AnimationClip relaxed, AnimationClip combat)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = StanceParam,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(relaxed, 0f);
        tree.AddChild(combat, 1f);
        return tree;
    }

    static AnimatorState Find(AnimatorStateMachine sm, string name) =>
        sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

    static AnimationClip Clip(string path, bool loop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
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
