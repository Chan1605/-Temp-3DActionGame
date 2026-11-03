using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

public static class PlayerJumpGuardSetup
{
    const string AnimRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/";
    const string JumpRoot = AnimRoot + "05_Jump/02_Jump_Combat/01_Jump_Combat_0/";
    const string BlockRoot = AnimRoot + "08_Hit/12_Block/";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string InputPath = "Assets/10.Input/InputSystem_Actions.inputactions";
    const string LocomotionState = "Locomotion";

    // 착지·막기 모션이 길면 이 값을 줄여 더 빨리 다음 상태로 넘김
    const float LandExitTime = 0.5f;
    const float BlockHitExitTime = 0.7f;

    static readonly (string state, string clip, bool loop)[] States =
    {
        ("Jump_Start", JumpRoot + "Jump_Combat_Start_0.anim", false),
        ("Jump_Loop", JumpRoot + "Jump_Combat_Loop_0.anim", true),
        ("Jump_Land", JumpRoot + "Jump_Combat_End_0.anim", false),
        ("Guard_Start", BlockRoot + "Block_Start.anim", false),
        ("Guard_Loop", BlockRoot + "Block_Loop.anim", true),
        ("Guard_Hit", BlockRoot + "Block_Hit.anim", false),
    };

    [MenuItem("Tools/Setup/Player Jump and Guard")]
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
        Link(sm, "Guard_Start", "Guard_Loop", 0.9f, 0.1f);
        Link(sm, "Guard_Hit", "Guard_Loop", BlockHitExitTime, 0.15f);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        GameObject player = locomotion.gameObject;
        Animator animator = player.GetComponentInChildren<Animator>();
        PlayerGuard guard = player.GetComponent<PlayerGuard>();
        if (guard == null) guard = Undo.AddComponent<PlayerGuard>(player);

        SetRefs(guard,
            ("guardAction", FindAction("Guard")),
            ("animator", animator),
            ("locomotion", locomotion),
            ("combat", player.GetComponent<PlayerCombat>()),
            ("parry", player.GetComponent<PlayerParry>()),
            ("dodge", player.GetComponent<PlayerDodge>()),
            ("skill", player.GetComponent<PlayerSkill>()),
            ("health", player.GetComponent<PlayerHealth>()),
            ("followCamera", Object.FindAnyObjectByType<ThirdPersonCamera>()));

        SetRefs(locomotion, ("jumpAction", FindAction("Jump")));
        SetRefs(player.GetComponent<PlayerHealth>(), ("guard", guard));
        SetRefs(player.GetComponent<HitFeedback>(), ("guard", guard));
        SetRefs(player.GetComponent<PlayerDodge>(), ("guard", guard)); // [변경] 회피 → 가드 캔슬

        PlayerClipFixer.Fix(ControllerPath);

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log("[JumpGuardSetup] 완료 · Space 점프, 우클릭 간파·가드, 상태 6개, PlayerGuard 연결");
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
