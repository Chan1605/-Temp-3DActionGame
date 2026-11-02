using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

public static class PlayerDodgeSetup
{
    const string ClipRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/06_Dodge/02_Dodge_Combat/";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string InputPath = "Assets/10.Input/InputSystem_Actions.inputactions";
    const string DodgeActionName = "Dodge";

    static readonly (string state, string clip)[] DodgeStates =
    {
        ("Dodge_F", ClipRoot + "Dodge_Combat_F.anim"),
        ("Dodge_B", ClipRoot + "Dodge_Combat_B.anim"),
        ("Dodge_L", ClipRoot + "Dodge_Combat_L.anim"),
        ("Dodge_R", ClipRoot + "Dodge_Combat_R.anim"),
    };

    [MenuItem("Tools/Setup/Player Dodge")]
    static void Run()
    {
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (locomotion == null || controller == null)
        {
            Debug.LogError("[DodgeSetup] Player 또는 Player.controller가 없습니다. Player Locomotion 셋업을 먼저 실행하세요.");
            return;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        foreach ((string stateName, string clipPath) in DodgeStates)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                Debug.LogError($"[DodgeSetup] {clipPath} 클립을 찾지 못했습니다.");
                return;
            }

            SetLoop(clip, false);
            AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
            if (state == null) state = sm.AddState(stateName);
            state.motion = clip;
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        GameObject player = locomotion.gameObject;
        PlayerDodge dodge = player.GetComponent<PlayerDodge>();
        if (dodge == null) dodge = Undo.AddComponent<PlayerDodge>(player);

        var so = new SerializedObject(dodge);
        so.FindProperty("dodgeAction").objectReferenceValue = FindAction(DodgeActionName);
        so.FindProperty("animator").objectReferenceValue = player.GetComponentInChildren<Animator>();
        so.FindProperty("locomotion").objectReferenceValue = locomotion;
        so.FindProperty("combat").objectReferenceValue = player.GetComponent<PlayerCombat>();
        so.FindProperty("skill").objectReferenceValue = player.GetComponent<PlayerSkill>();
        so.FindProperty("health").objectReferenceValue = player.GetComponent<PlayerHealth>();
        so.ApplyModifiedProperties();

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health != null)
        {
            var healthSo = new SerializedObject(health);
            healthSo.FindProperty("dodge").objectReferenceValue = dodge;
            healthSo.ApplyModifiedProperties();
        }

        PlayerClipFixer.Fix(ControllerPath);

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log("[DodgeSetup] 완료 · Space(패드 B) 회피, 회피 상태 4개, PlayerDodge 연결");
    }

    static void SetLoop(AnimationClip clip, bool loop)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime == loop) return;

        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }

    static InputActionReference FindAction(string actionName)
    {
        var reference = AssetDatabase.LoadAllAssetsAtPath(InputPath)
            .OfType<InputActionReference>()
            .FirstOrDefault(r => r.action != null && r.action.actionMap.name == "Player" && r.action.name == actionName);

        if (reference == null) Debug.LogWarning($"[DodgeSetup] Player/{actionName} 액션을 찾지 못했습니다. Input Actions 에셋을 확인하세요.");
        return reference;
    }
}
