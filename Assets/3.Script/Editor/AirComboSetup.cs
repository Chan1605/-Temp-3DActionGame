using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 공중 콤보 셋업
// 플레이어: Attack_Launcher(띄우기) · Air_Attack_1~3(공중 3연격) · Slam_Start/Loop/End(내려찍기) 상태 + 공격 데이터 + PlayerCombat 연결
// 검병: Launch → Air_Loop · Air_Hit → Air_Loop · Air_Land · Down · Get_Up 상태
// 공격 수치는 처음 만들 때만 넣음 (이후 튜닝 값 보존)
public static class AirComboSetup
{
    const string PlayerControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string EnemyControllerPath = "Assets/5.Animation/Enemy/Swordsman.controller";
    const string Root = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/";
    const string DataFolder = "Assets/11.Data/Attack/";

    [MenuItem("Tools/Setup/Air Combo")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[AirComboSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var player = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        var enemy = AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyControllerPath);
        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        if (player == null || enemy == null || combat == null)
        {
            Debug.LogError("[AirComboSetup] Player.controller · Swordsman.controller · 씬의 PlayerCombat 중 없는 것이 있습니다.");
            return;
        }

        // ===== 플레이어 =====
        AnimationClip launcherClip = Clip("02_Attack/11_Attack_Up/Attack_Up_Floor_To_Air_02");
        AnimationClip[] airClips =
        {
            Clip("02_Attack/06_Combo_Attack_Air_06/Combo_Attack_Air_06_01"),
            Clip("02_Attack/06_Combo_Attack_Air_06/Combo_Attack_Air_06_02"),
            Clip("02_Attack/06_Combo_Attack_Air_06/Combo_Attack_Air_06_03")
        };
        AnimationClip slamStart = Clip("02_Attack/09_Attack_Air_To_Floor_02/Attack_Air_To_Floor_Start_02");
        AnimationClip slamLoop = Clip("02_Attack/09_Attack_Air_To_Floor_02/Attack_Air_To_Floor_Loop_02");
        AnimationClip slamEnd = Clip("02_Attack/09_Attack_Air_To_Floor_02/Attack_Air_To_Floor_End_02");
        if (launcherClip == null || airClips.Any(c => c == null) || slamStart == null || slamLoop == null || slamEnd == null)
        {
            Debug.LogError("[AirComboSetup] 플레이어 공중 공격 클립 중 없는 것이 있습니다.");
            return;
        }

        AnimatorStateMachine psm = player.layers[0].stateMachine;
        State(psm, "Attack_Launcher", launcherClip);
        for (int i = 0; i < airClips.Length; i++) State(psm, $"Air_Attack_{i + 1}", airClips[i]);
        State(psm, "Slam_Start", slamStart);
        SetLoop(slamLoop);
        State(psm, "Slam_Loop", slamLoop);
        State(psm, "Slam_End", slamEnd);
        EditorUtility.SetDirty(player);

        AttackData launcher = Data("Launcher", launcherClip, "Attack_Launcher", out bool newLauncher);
        var lso = new SerializedObject(launcher);
        if (newLauncher)
        {
            lso.FindProperty("launchHeight").floatValue = 3.2f;
            lso.FindProperty("damage").floatValue = 12f;
            lso.FindProperty("postureDamage").floatValue = 8f;
            lso.FindProperty("weight").enumValueIndex = (int)AttackWeight.Heavy;
            lso.FindProperty("stepDistance").floatValue = 0.5f;
            lso.FindProperty("comboTransition").floatValue = 0.45f;
        }

        AttackData[] air = new AttackData[airClips.Length];
        for (int i = 0; i < air.Length; i++)
        {
            air[i] = Data($"Air_Attack_{i + 1}", airClips[i], $"Air_Attack_{i + 1}", out bool created);
            var so = new SerializedObject(air[i]);
            so.FindProperty("airAttack").boolValue = true;
            if (created)
            {
                so.FindProperty("damage").floatValue = i == air.Length - 1 ? 14f : 9f;
                so.FindProperty("postureDamage").floatValue = 5f;
                so.FindProperty("stepDistance").floatValue = 0.35f;
                so.FindProperty("inputOpen").floatValue = 0.15f;
                so.FindProperty("comboTransition").floatValue = 0.45f;
                so.FindProperty("moveCancelStart").floatValue = 0.7f;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 연결: 띄우기 □ → 공중 1 → 2 → 3 (3에서 □ = 내려찍기는 코드가 처리)
        lso.FindProperty("nextLight").objectReferenceValue = air[0];
        lso.ApplyModifiedPropertiesWithoutUndo();
        for (int i = 0; i < air.Length - 1; i++)
        {
            var so = new SerializedObject(air[i]);
            so.FindProperty("nextLight").objectReferenceValue = air[i + 1];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var cso = new SerializedObject(combat);
        cso.FindProperty("launcher").objectReferenceValue = launcher;
        cso.FindProperty("airStarter").objectReferenceValue = air[0];
        cso.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(combat.gameObject.scene);

        // ===== 검병 =====
        AnimationClip launch = Clip("08_Hit/08_Hit_Combat_Air_Large_To_Floor/Hit_Combat_Air_Large_To_Floor_Start");
        AnimationClip airLoop = Clip("08_Hit/08_Hit_Combat_Air_Large_To_Floor/Hit_Combat_Air_Large_To_Floor_Loop");
        AnimationClip airLand = Clip("08_Hit/08_Hit_Combat_Air_Large_To_Floor/Hit_Combat_Air_Large_To_Floor_End");
        AnimationClip airHit = Clip("08_Hit/04_Hit_Combat_Air/Hit_Combat_Air_F");
        AnimationClip down = Clip("08_Hit/11_Knock_Down_Combat/Knock_Down_Combat_Start");
        AnimationClip getUp = Clip("08_Hit/09_Get_Up/Get_Up_Combat");
        if (launch == null || airLoop == null || airLand == null || airHit == null || down == null || getUp == null)
        {
            Debug.LogError("[AirComboSetup] 검병 공중 피격·기상 클립 중 없는 것이 있습니다.");
            return;
        }

        AnimatorStateMachine esm = enemy.layers[0].stateMachine;
        SetLoop(airLoop);
        AnimatorState loopState = State(esm, "Air_Loop", airLoop);
        ExitTo(State(esm, "Launch", launch), loopState);
        ExitTo(State(esm, "Air_Hit", airHit), loopState);
        State(esm, "Air_Land", airLand);
        State(esm, "Down", down);
        State(esm, "Get_Up", getUp);
        EditorUtility.SetDirty(enemy);

        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(PlayerControllerPath);
        PlayerClipFixer.Fix(EnemyControllerPath);
        EditorApplication.ExecuteMenuItem("Tools/Setup/Fit Attack Hit Windows");
        Debug.Log("[AirComboSetup] 완료 · 플레이어 띄우기·공중 3연격·내려찍기 + 검병 공중 피격·기상");
    }

    static AnimationClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + path + ".anim");

    static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion)
    {
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? sm.AddState(name);
        state.motion = motion;
        return state;
    }

    static void ExitTo(AnimatorState from, AnimatorState to)
    {
        if (from.transitions.Any(t => t.destinationState == to)) return;
        AnimatorStateTransition tr = from.AddTransition(to);
        tr.hasExitTime = true;
        tr.exitTime = 0.9f;
        tr.duration = 0.1f;
        tr.hasFixedDuration = true;
    }

    static AttackData Data(string fileName, AnimationClip clip, string stateName, out bool created)
    {
        string path = DataFolder + fileName + ".asset";
        var data = AssetDatabase.LoadAssetAtPath<AttackData>(path);
        created = data == null;
        if (created)
        {
            data = ScriptableObject.CreateInstance<AttackData>();
            AssetDatabase.CreateAsset(data, path);
        }
        var so = new SerializedObject(data);
        so.FindProperty("clip").objectReferenceValue = clip;
        so.FindProperty("stateName").stringValue = stateName;
        so.ApplyModifiedPropertiesWithoutUndo();
        return data;
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
