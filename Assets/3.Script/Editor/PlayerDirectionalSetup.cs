using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 방향 모션 (Player.controller)
//  - Jump_Start · Jump_Land · Double_Jump: 자세(Stance)마다 앞·뒤·좌·우·제자리 5방향 2D 블렌드 (MoveX, MoveZ)
//  - 질주(Locomotion 마지막 칸): 자세마다 왼쪽 기울기 · 직진 · 오른쪽 기울기 (Turn)
// 걷기·달리기의 8방향(옆걸음·뒷걸음)은 락온 이동이 생길 때 같은 MoveX/MoveZ로 추가
// 다시 실행해도 이미 방향별로 나뉜 칸은 그대로 둠
public static class PlayerDirectionalSetup
{
    const string AnimRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string Jump = AnimRoot + "05_Jump/01_Jump/";
    const string JumpCombat = AnimRoot + "05_Jump/02_Jump_Combat/";
    const string Double = AnimRoot + "05_Jump/03_Double_Jump/";
    const string DoubleCombat = AnimRoot + "05_Jump/04_Double_Jump_Combat/";
    const string Sprint = AnimRoot + "04_Run/01_Run_RM/11_Run_Fast_RM/";
    const string SprintCombat = AnimRoot + "04_Run/02_Run_Combat_RM/11_Run_Fast_Combat_RM/";

    static readonly Vector2 Center = Vector2.zero, F = new Vector2(0f, 1f), B = new Vector2(0f, -1f), L = new Vector2(-1f, 0f), R = new Vector2(1f, 0f);

    // 상태 → (평상시 5방향, 전투 5방향). 순서: 제자리, 앞, 뒤, 왼, 오른
    static readonly Dictionary<string, (string[] relaxed, string[] combat, bool loop)> Directional = new Dictionary<string, (string[], string[], bool)>
    {
        ["Jump_Start"] = (new[]
        {
            Jump + "01_Jump_0/Jump_Start_0.anim", Jump + "02_Jump_F_0/Jump_Start_F_0.anim", Jump + "03_Jump_B_180/Jump_Start_B_180.anim",
            Jump + "04_Jump_L_90/Jump_Start_L_90.anim", Jump + "05_Jump_R_90/Jump_Start_R_90.anim"
        }, new[]
        {
            JumpCombat + "01_Jump_Combat_0/Jump_Combat_Start_0.anim", JumpCombat + "02_Jump_Combat_F_0/Jump_Combat_Start_F_0.anim", JumpCombat + "03_Jump_Combat_B_180/Jump_Combat_Start_B_180.anim",
            JumpCombat + "04_Jump_Combat_L_90/Jump_Combat_Start_L_90.anim", JumpCombat + "05_Jump_Combat_R_90/Jump_Combat_Start_R_90.anim"
        }, false),
        ["Jump_Land"] = (new[]
        {
            Jump + "01_Jump_0/Jump_Stop_0.anim", Jump + "02_Jump_F_0/Jump_End_F_0.anim", Jump + "03_Jump_B_180/Jump_End_B_180.anim",
            Jump + "04_Jump_L_90/Jump_End_L_90.anim", Jump + "05_Jump_R_90/Jump_End_R_90.anim"
        }, new[]
        {
            JumpCombat + "01_Jump_Combat_0/Jump_Combat_End_0.anim", JumpCombat + "02_Jump_Combat_F_0/Jump_Combat_End_F_0.anim", JumpCombat + "03_Jump_Combat_B_180/Jump_Combat_End_B_180.anim",
            JumpCombat + "04_Jump_Combat_L_90/Jump_Combat_End_L_90.anim", JumpCombat + "05_Jump_Combat_R_90/Jump_Combat_End_R_90.anim"
        }, false),
        ["Double_Jump"] = (new[]
        {
            Double + "Double_Jump_0.anim", Double + "Double_Jump_F_0.anim", Double + "Double_Jump_B_180.anim",
            Double + "Double_Jump_L_90.anim", Double + "Double_Jump_R_90.anim"
        }, new[]
        {
            DoubleCombat + "Double_Jump_Combat_0.anim", DoubleCombat + "Double_Jump_Combat_F_0.anim", DoubleCombat + "Double_Jump_Combat_B_180.anim",
            DoubleCombat + "Double_Jump_Combat_L_90.anim", DoubleCombat + "Double_Jump_Combat_R_90.anim"
        }, false),
    };

    static readonly (string left, string right)[] SprintLean =
    {
        (Sprint + "Run_Fast_Lean_L_Loop_RM.anim", Sprint + "Run_Fast_Lean_R_Loop_RM.anim"),
        (SprintCombat + "Run_Fast_Combat_Lean_L_Loop_RM.anim", SprintCombat + "Run_Fast_Combat_Lean_R_Loop_RM.anim"),
    };

    [MenuItem("Tools/Setup/Player Directional (Jump + Sprint Lean)")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[DirectionalSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || !controller.parameters.Any(p => p.name == "Stance"))
        {
            Debug.LogError("[DirectionalSetup] Stance 파라미터가 없습니다. Player Stance (Idle) → Player Jump + Death 셋업을 먼저 실행하세요.");
            return;
        }

        var allPaths = Directional.Values.SelectMany(v => v.relaxed.Concat(v.combat)).Concat(SprintLean.SelectMany(s => new[] { s.left, s.right }));
        string missing = allPaths.FirstOrDefault(p => AssetDatabase.LoadAssetAtPath<AnimationClip>(p) == null);
        if (missing != null)
        {
            Debug.LogError($"[DirectionalSetup] {missing}를 찾지 못했습니다.");
            return;
        }

        foreach (string param in new[] { "MoveX", "MoveZ", "Turn" })
            if (!controller.parameters.Any(p => p.name == param)) controller.AddParameter(param, AnimatorControllerParameterType.Float);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // 1) 점프 3종: Stance 트리의 두 칸(평상시·전투)을 각각 5방향 2D 트리로 교체
        int jumpCount = 0;
        foreach (var pair in Directional)
        {
            AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == pair.Key);
            if (state == null || !(state.motion is BlendTree stance) || stance.blendParameter != "Stance")
            {
                Debug.LogWarning($"[DirectionalSetup] {pair.Key}가 자세별로 나뉘어 있지 않아 건너뜁니다. (Player Jump + Death 셋업)");
                continue;
            }

            ChildMotion[] children = stance.children;
            if (children.Length < 2 || children.All(c => c.motion is BlendTree)) continue; // 이미 방향별
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is BlendTree) continue;
                string[] paths = children[i].threshold < 0.5f ? pair.Value.relaxed : pair.Value.combat;
                children[i].motion = Directional2D(controller, $"{pair.Key}_{(i == 0 ? "Relaxed" : "Combat")}", paths, pair.Value.loop);
            }
            stance.children = children;
            EditorUtility.SetDirty(stance);
            jumpCount++;
        }

        // 2) 질주 칸: 자세별 클립을 [왼쪽 기울기, 직진, 오른쪽 기울기]로
        bool leanDone = false;
        AnimatorState loco = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Locomotion");
        if (loco != null && loco.motion is BlendTree top)
        {
            ChildMotion sprintSlot = top.children.OrderBy(c => c.threshold).Last();
            if (sprintSlot.motion is BlendTree sprintStance && sprintStance.blendParameter == "Stance")
            {
                ChildMotion[] children = sprintStance.children;
                for (int i = 0; i < children.Length && i < SprintLean.Length; i++)
                {
                    if (!(children[i].motion is AnimationClip straight)) continue; // 이미 나뉨
                    var tree = NewTree(controller, $"Sprint_{(i == 0 ? "Relaxed" : "Combat")}", BlendTreeType.Simple1D, "Turn");
                    tree.AddChild(Clip(SprintLean[i].left, true), -1f);
                    tree.AddChild(straight, 0f);
                    tree.AddChild(Clip(SprintLean[i].right, true), 1f);
                    children[i].motion = tree;
                    leanDone = true;
                }
                sprintStance.children = children;
                EditorUtility.SetDirty(sprintStance);
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(ControllerPath);
        Debug.Log($"[DirectionalSetup] 완료 · 방향 점프 상태 {jumpCount}개, 질주 기울기 {(leanDone ? "추가" : "변경 없음")}");
    }

    static BlendTree Directional2D(AnimatorController controller, string name, string[] paths, bool loop)
    {
        var tree = NewTree(controller, name, BlendTreeType.FreeformDirectional2D, "MoveX");
        tree.blendParameterY = "MoveZ";
        Vector2[] positions = { Center, F, B, L, R };
        for (int i = 0; i < paths.Length; i++) tree.AddChild(Clip(paths[i], loop), positions[i]);
        return tree;
    }

    static BlendTree NewTree(AnimatorController controller, string name, BlendTreeType type, string parameter)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = type,
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
