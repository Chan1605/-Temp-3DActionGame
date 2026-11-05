using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 처형: 플레이어 Execution_1~3 · 적(검병 공용 컨트롤러) Executed_1~3 짝 모션 + PlayerExecution + HUD 안내
// 다시 실행하면 비어 있는 상태만 채움 (애니메이션 고르기 창에서 바꾼 클립 보존)
public static class PlayerExecutionSetup
{
    const string Root = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/02_Attack/15_Execution/";
    const string PlayerController = "Assets/5.Animation/Player/Player.controller";
    const string EnemyController = "Assets/5.Animation/Enemy/Swordsman.controller";

    [MenuItem("Tools/Setup/Player Execution")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[ExecutionSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var player = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerController);
        var enemy = AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyController);
        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        if (player == null || enemy == null || combat == null)
        {
            Debug.LogError("[ExecutionSetup] Player.controller · Swordsman.controller · 씬의 PlayerCombat 중 하나가 없습니다.");
            return;
        }

        for (int i = 1; i <= 3; i++)
        {
            AnimationClip attack = Clip(Root + $"01_Execution/Execution_0{i}.anim");
            AnimationClip target = Clip(Root + $"02_Target/Execution_Target_0{i}.anim");
            if (attack == null || target == null) return;
            Fill(player, $"Execution_{i}", attack);
            Fill(enemy, $"Executed_{i}", target);
        }
        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(enemy);

        GameObject go = combat.gameObject;
        var execution = go.GetComponent<PlayerExecution>();
        if (execution == null) execution = Undo.AddComponent<PlayerExecution>(go);
        SetRef(execution, "followCamera", Object.FindAnyObjectByType<ThirdPersonCamera>());
        SetRef(combat, "execution", execution);
        SetRef(go.GetComponent<PlayerHealth>(), "execution", execution);

        var hud = Object.FindAnyObjectByType<GameHUD>();
        Canvas canvas = hud != null ? hud.GetComponentInParent<Canvas>() : Object.FindAnyObjectByType<Canvas>();
        if (canvas != null && Object.FindAnyObjectByType<ExecutionPrompt>() == null)
        {
            var ui = new GameObject("ExecutionPrompt", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(ui, "Execution Prompt");
            var rt = (RectTransform)ui.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();
            SetRef(ui.AddComponent<ExecutionPrompt>(), "execution", execution);
        }

        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(PlayerController);
        PlayerClipFixer.Fix(EnemyController);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ExecutionSetup] 완료 · 처형 짝 모션 3종, PlayerExecution, 처형 안내 UI");
    }

    static void Fill(AnimatorController controller, string stateName, AnimationClip clip)
    {
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName) ?? sm.AddState(stateName);
        if (state.motion == null) state.motion = clip;
    }

    static AnimationClip Clip(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            Debug.LogError($"[ExecutionSetup] {path}를 찾지 못했습니다.");
            return null;
        }
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime)
        {
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }
        return clip;
    }

    static void SetRef(Object target, string field, Object value)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) return;
        p.objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
}
