using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 적 맴돌기: 검병 공용 컨트롤러에 StrafeX/StrafeZ 파라미터와 Strafe 상태(전투 걷기 8방향 + 가운데 전투 대기) 추가
public static class EnemyStrafeSetup
{
    const string ControllerPath = "Assets/5.Animation/Enemy/Swordsman.controller";
    const string WalkRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/03_Walk/02_Walk_Combat_RM/";
    const string IdlePath = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/01_Idle/Idle_Combat.anim";

    static readonly (string file, Vector2 pos)[] Directions =
    {
        ("01_Walk_Combat_F_0_RM/Walk_Combat_Loop_F_0_RM", new Vector2(0f, 1f)),
        ("02_Walk_Combat_F_L_45_RM/Walk_Combat_Loop_F_L_45_RM", new Vector2(-0.707f, 0.707f)),
        ("03_Walk_Combat_F_R_45_RM/Walk_Combat_Loop_F_R_45_RM", new Vector2(0.707f, 0.707f)),
        ("04_Walk_Combat_F_L_90_RM/Walk_Combat_Loop_F_L_90_RM", new Vector2(-1f, 0f)),
        ("05_Walk_Combat_F_R_90_RM/Walk_Combat_Loop_F_R_90_RM", new Vector2(1f, 0f)),
        ("06_Walk_Combat_B_180_RM/Walk_Combat_Loop_B_180_RM", new Vector2(0f, -1f)),
        ("07_Walk_Combat_B_L_45_RM/Walk_Combat_Loop_B_L_45_RM", new Vector2(-0.707f, -0.707f)),
        ("08_Walk_Combat_B_R_45_RM/Walk_Combat_Loop_B_R_45_RM", new Vector2(0.707f, -0.707f)),
    };

    [MenuItem("Tools/Setup/Enemy Strafe")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[EnemyStrafeSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
        if (controller == null || idle == null)
        {
            Debug.LogError("[EnemyStrafeSetup] Swordsman.controller 또는 Idle_Combat 클립이 없습니다.");
            return;
        }

        foreach (string p in new[] { "StrafeX", "StrafeZ" })
            if (controller.parameters.All(x => x.name != p)) controller.AddParameter(p, AnimatorControllerParameterType.Float);

        var tree = new BlendTree
        {
            name = "Strafe_8Dir",
            blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = "StrafeX",
            blendParameterY = "StrafeZ",
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(idle, Vector2.zero);
        int added = 0;
        foreach (var d in Directions)
        {
            string file = d.file;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkRoot + file + ".anim");
            if (clip == null)
            {
                Debug.LogWarning($"[EnemyStrafeSetup] {file}를 찾지 못해 그 방향은 비워 둡니다.");
                continue;
            }
            SetLoop(clip);
            tree.AddChild(clip, d.pos);
            added++;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Strafe") ?? sm.AddState("Strafe");
        state.motion = tree;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(ControllerPath);
        Debug.Log($"[EnemyStrafeSetup] 완료 · Strafe 상태 (방향 {added}개 + 대기)");
    }

    static void SetLoop(AnimationClip clip)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime) return;
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }
}
