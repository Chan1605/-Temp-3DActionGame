using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 1) 검병 공격 2개를 플레이어 □1·□2와 다른 클립(콤보 세트 2의 1·2타)으로 교체
// 2) 플레이어 달리기 공격(Run_Attack_01) 추가: 달리는 중 □ → 달리기 공격 → 이어서 □2로 콤보 계속
// 마지막에 Fix Player Clips · Fit Attack Hit Windows를 자동 실행해 판정 구간을 새 클립에 맞춤
public static class AttackClipSetup
{
    const string AttackRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/02_Attack/";
    const string EnemyClip1 = AttackRoot + "02_Combo_Attack_02/Combo_Attack_02_01.anim";
    const string EnemyClip2 = AttackRoot + "02_Combo_Attack_02/Combo_Attack_02_02.anim";
    const string RunClip = AttackRoot + "12_Run_Attack/Run_Attack_01.anim";

    const string PlayerController = "Assets/5.Animation/Player/Player.controller";
    const string EnemyController = "Assets/5.Animation/Enemy/Swordsman.controller";
    const string EnemyDataFolder = "Assets/11.Data/Enemy/";
    const string AttackFolder = "Assets/11.Data/Attack/";
    const string RunState = "Run_Attack";
    const string SpeedParam = "AttackSpeed";

    [MenuItem("Tools/Setup/Swordsman Clips + Run Attack")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[AttackClipSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        bool enemyOk = SwapEnemyClips();
        bool runOk = AddRunAttack();

        PlayerClipFixer.Fix(PlayerController);
        PlayerClipFixer.Fix(EnemyController);
        EditorApplication.ExecuteMenuItem("Tools/Setup/Fit Attack Hit Windows");

        Debug.Log($"[AttackClipSetup] 완료 · 검병 클립 교체 {(enemyOk ? "O" : "X")} · 달리기 공격 {(runOk ? "O" : "X")}");
    }

    // ===== 검병 =====
    static bool SwapEnemyClips()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyController);
        var clip1 = LoadOneShot(EnemyClip1);
        var clip2 = LoadOneShot(EnemyClip2);
        if (controller == null || clip1 == null || clip2 == null)
        {
            Debug.LogError("[AttackClipSetup] Swordsman.controller 또는 콤보 세트 2 클립을 찾지 못했습니다.");
            return false;
        }

        SetStateMotion(controller, "Attack_1", clip1);
        SetStateMotion(controller, "Attack_2", clip2);
        SetClip(EnemyDataFolder + "Swordsman_Attack_1.asset", clip1);
        SetClip(EnemyDataFolder + "Swordsman_Attack_2.asset", clip2);
        EditorUtility.SetDirty(controller);
        return true;
    }

    // ===== 달리기 공격 =====
    static bool AddRunAttack()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerController);
        var clip = LoadOneShot(RunClip);
        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        if (controller == null || clip == null || combat == null)
        {
            Debug.LogError("[AttackClipSetup] Player.controller · Run_Attack_01 · 씬의 PlayerCombat 중 하나가 없습니다.");
            return false;
        }

        AnimatorState state = SetStateMotion(controller, RunState, clip);
        if (controller.parameters.Any(p => p.name == SpeedParam))
        {
            state.speedParameter = SpeedParam;
            state.speedParameterActive = true;
        }
        EditorUtility.SetDirty(controller);

        // □1을 대신하는 시작 타: 다음 □는 □2, 다음 △는 □1의 △ 분기를 그대로 이어받음
        var light1 = AssetDatabase.LoadAssetAtPath<AttackData>(AttackFolder + "Combo_01_1.asset");
        var light2 = AssetDatabase.LoadAssetAtPath<AttackData>(AttackFolder + "Combo_01_2.asset");
        AttackData heavyBranch = light1 != null ? light1.GetNext(AttackInput.Heavy) : null;

        string path = AttackFolder + "Run_Attack.asset";
        var data = AssetDatabase.LoadAssetAtPath<AttackData>(path);
        bool created = data == null;
        if (created)
        {
            data = ScriptableObject.CreateInstance<AttackData>();
            AssetDatabase.CreateAsset(data, path);
        }

        var so = new SerializedObject(data);
        so.FindProperty("clip").objectReferenceValue = clip;
        so.FindProperty("stateName").stringValue = RunState;
        so.FindProperty("nextLight").objectReferenceValue = light2;
        so.FindProperty("nextHeavy").objectReferenceValue = heavyBranch;
        if (created)
        {
            // 처음 만들 때만 수치 입력 → 이후 인스펙터 튜닝 값 보존
            so.FindProperty("damage").floatValue = 14f;
            so.FindProperty("postureDamage").floatValue = 8f;
            so.FindProperty("stepDistance").floatValue = 1.6f;
            so.FindProperty("stepEnd").floatValue = 0.35f;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        var combatSo = new SerializedObject(combat);
        combatSo.FindProperty("runStarter").objectReferenceValue = data;
        combatSo.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(combat.gameObject.scene);

        AssetDatabase.SaveAssets();
        return true;
    }

    // ===== 공용 =====
    static AnimationClip LoadOneShot(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) return null;

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime)
        {
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }
        return clip;
    }

    static AnimatorState SetStateMotion(AnimatorController controller, string stateName, AnimationClip clip)
    {
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
        if (state == null) state = sm.AddState(stateName);
        state.motion = clip;
        return state;
    }

    static void SetClip(string assetPath, AnimationClip clip)
    {
        var data = AssetDatabase.LoadAssetAtPath<AttackData>(assetPath);
        if (data == null)
        {
            Debug.LogWarning($"[AttackClipSetup] {assetPath}가 없습니다.");
            return;
        }
        var so = new SerializedObject(data);
        so.FindProperty("clip").objectReferenceValue = clip;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
