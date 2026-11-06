using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 저스트 회피 반격: 플레이어 컨트롤러에 Dodge_Counter 상태(달리며 베기 2) + 반격 공격 데이터 + PlayerCombat 연결
// 수치는 처음 만들 때만 넣음 (이후 튜닝 값 보존)
public static class PlayerDodgeCounterSetup
{
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string ClipPath = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/02_Attack/12_Run_Attack/Run_Attack_02.anim";
    const string DataPath = "Assets/11.Data/Attack/Dodge_Counter.asset";
    const string StateName = "Dodge_Counter";

    [MenuItem("Tools/Setup/Player Dodge Counter")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[DodgeCounterSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        if (controller == null || clip == null || combat == null)
        {
            Debug.LogError("[DodgeCounterSetup] Player.controller · Run_Attack_02 클립 · 씬의 PlayerCombat 중 없는 것이 있습니다.");
            return;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state = null;
        foreach (ChildAnimatorState s in sm.states)
            if (s.state.name == StateName) state = s.state;
        if (state == null) state = sm.AddState(StateName);
        state.motion = clip;
        EditorUtility.SetDirty(controller);

        var data = AssetDatabase.LoadAssetAtPath<AttackData>(DataPath);
        bool created = data == null;
        if (created)
        {
            data = ScriptableObject.CreateInstance<AttackData>();
            AssetDatabase.CreateAsset(data, DataPath);
        }
        var so = new SerializedObject(data);
        so.FindProperty("clip").objectReferenceValue = clip;
        so.FindProperty("stateName").stringValue = StateName;
        if (created)
        {
            so.FindProperty("damage").floatValue = 22f;
            so.FindProperty("postureDamage").floatValue = 20f;
            so.FindProperty("weight").enumValueIndex = (int)AttackWeight.Heavy;
            so.FindProperty("stepDistance").floatValue = 1.8f;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        var combatSo = new SerializedObject(combat);
        combatSo.FindProperty("counterStarter").objectReferenceValue = data;
        combatSo.FindProperty("dodge").objectReferenceValue = combat.GetComponent<PlayerDodge>();
        combatSo.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(combat.gameObject.scene);

        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(ControllerPath);
        EditorApplication.ExecuteMenuItem("Tools/Setup/Fit Attack Hit Windows");
        Debug.Log("[DodgeCounterSetup] 완료 · Dodge_Counter 상태 + 반격 데이터 연결");
    }
}
