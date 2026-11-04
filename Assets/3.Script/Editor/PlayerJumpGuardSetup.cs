using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

// [변경] 가드 제거 → 점프만 셋업 (가드 상태 정리는 Tools/Setup/Charge Attack이 담당)
public static class PlayerJumpGuardSetup
{
    const string AnimRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/";
    const string JumpRoot = AnimRoot + "05_Jump/02_Jump_Combat/01_Jump_Combat_0/";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string InputPath = "Assets/10.Input/InputSystem_Actions.inputactions";
    const string LocomotionState = "Locomotion";

    // 착지 모션이 길면 이 값을 줄여 더 빨리 로코모션으로 넘김
    const float LandExitTime = 0.5f;

    static readonly (string state, string clip, bool loop)[] States =
    {
        ("Jump_Start", JumpRoot + "Jump_Combat_Start_0.anim", false),
        ("Jump_Loop", JumpRoot + "Jump_Combat_Loop_0.anim", true),
        ("Jump_Land", JumpRoot + "Jump_Combat_End_0.anim", false),
    };

    [MenuItem("Tools/Setup/Player Jump")] // [변경]
    static void Run()
    {
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (locomotion == null || controller == null)
        {
            Debug.LogError("[JumpGuardSetup] Player 또는 Player.controller가 없습니다. Player Locomotion 셋업을 먼저 실행하세요.");
            return;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState locomotionState = FindState(sm, LocomotionState);
        if (locomotionState == null)
        {
            Debug.LogError("[JumpGuardSetup] Locomotion 상태가 없습니다.");
            return;
        }

        foreach ((string stateName, string clipPath, bool loop) in States)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                Debug.LogError($"[JumpGuardSetup] {clipPath} 클립을 찾지 못했습니다.");
                return;
            }

            SetLoop(clip, loop);
            AnimatorState state = FindState(sm, stateName) ?? sm.AddState(stateName);
            state.motion = clip;
        }

        // 다른 전환은 코드의 CrossFade가 담당하고, 한 동작이 끝나면 자동으로 넘어가는 것만 연결
        Link(sm, "Jump_Start", "Jump_Loop", 0.9f, 0.1f);
        Link(sm, "Jump_Land", LocomotionState, LandExitTime, 0.2f);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        GameObject player = locomotion.gameObject;
        SetRefs(locomotion, ("jumpAction", FindAction("Jump")));

        PlayerClipFixer.Fix(ControllerPath);

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log("[JumpSetup] 완료 · Space 점프, 상태 3개"); // [변경]
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name) =>
        sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

    // 재실행해도 전환이 중복되지 않게 기존 전환을 지우고 하나만 둠
    static void Link(AnimatorStateMachine sm, string from, string to, float exitTime, float duration)
    {
        AnimatorState src = FindState(sm, from);
        AnimatorState dst = FindState(sm, to);
        if (src == null || dst == null) return;

        foreach (AnimatorStateTransition t in src.transitions.ToArray()) src.RemoveTransition(t);

        AnimatorStateTransition transition = src.AddTransition(dst);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.hasFixedDuration = true;
        transition.duration = duration;
    }

    static void SetLoop(AnimationClip clip, bool loop)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime == loop) return;

        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }

    static void SetRefs(Object target, params (string field, Object value)[] refs)
    {
        if (target == null) return;

        var so = new SerializedObject(target);
        foreach ((string field, Object value) in refs)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null) prop.objectReferenceValue = value;
            else Debug.LogWarning($"[JumpGuardSetup] {target.GetType().Name}에 '{field}' 필드가 없습니다.");
        }
        so.ApplyModifiedProperties();
    }

    static InputActionReference FindAction(string actionName)
    {
        var reference = AssetDatabase.LoadAllAssetsAtPath(InputPath)
            .OfType<InputActionReference>()
            .FirstOrDefault(r => r.action != null && r.action.actionMap.name == "Player" && r.action.name == actionName);

        if (reference == null) Debug.LogWarning($"[JumpGuardSetup] Player/{actionName} 액션을 찾지 못했습니다. Input Actions 에셋을 확인하세요.");
        return reference;
    }
}
