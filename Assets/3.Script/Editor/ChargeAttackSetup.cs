using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

// 우클릭 차지 강타 연결 + 가드 정리
//  - Animator: Charge_Start → Charge_Loop(유지), Charge_Release(내려치기) / Guard_* 상태 삭제
//  - AttackData: 11.Data/Attack/Charge_Release (피해는 차지 단계 배율로 곱해짐)
//  - Player: PlayerChargeAttack 추가·연결, PlayerGuard 컴포넌트 제거
//  - 끝나면 Fit Attack Hit Windows를 실행해 내려치기 판정 구간을 클립에 맞춤
public static class ChargeAttackSetup
{
    const string ClipRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/02_Attack/16_Power_Attack/";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string InputPath = "Assets/10.Input/InputSystem_Actions.inputactions";
    const string AttackPath = "Assets/11.Data/Attack/Charge_Release.asset";
    const string ReleaseState = "Charge_Release";

    [MenuItem("Tools/Setup/Charge Attack")]
    static void Run()
    {
        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (combat == null || controller == null)
        {
            Debug.LogError("[ChargeSetup] Player 또는 Player.controller가 없습니다.");
            return;
        }

        var start = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipRoot + "Power_Attack_Start.anim");
        var loop = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipRoot + "Power_Attack_Loop.anim");
        var end = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipRoot + "Power_Attack_End.anim");
        if (start == null || loop == null || end == null)
        {
            Debug.LogError($"[ChargeSetup] {ClipRoot}의 Power_Attack 클립을 찾지 못했습니다.");
            return;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        SetState(sm, "Charge_Start", start, false);
        SetState(sm, "Charge_Loop", loop, true);
        SetState(sm, ReleaseState, end, false);
        Link(sm, "Charge_Start", "Charge_Loop", 0.9f, 0.1f);
        int removed = RemoveStates(sm, "Guard_Start", "Guard_Loop", "Guard_Hit");
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        AttackData release = GetOrCreateReleaseAttack(end);

        GameObject player = combat.gameObject;
        int guards = RemoveGuard(player);

        var charge = player.GetComponent<PlayerChargeAttack>();
        if (charge == null) charge = Undo.AddComponent<PlayerChargeAttack>(player);
        SetRefs(charge,
            ("heavyAction", FindAction("HeavyAttack")),
            ("releaseAttack", release),
            ("animator", player.GetComponentInChildren<Animator>()),
            ("combat", combat),
            ("locomotion", player.GetComponent<PlayerLocomotion>()),
            ("parry", player.GetComponent<PlayerParry>()),
            ("dodge", player.GetComponent<PlayerDodge>()),
            ("skill", player.GetComponent<PlayerSkill>()),
            ("health", player.GetComponent<PlayerHealth>()),
            ("followCamera", Object.FindAnyObjectByType<ThirdPersonCamera>()));
        SetRefs(player.GetComponent<PlayerDodge>(), ("charge", charge));
        SetRefs(player.GetComponent<PlayerVfx>(), ("heavyCharge", charge));
        SetRefs(Object.FindAnyObjectByType<GameHUD>(), ("chargeSource", charge));

        PlayerClipFixer.Fix(ControllerPath);
        EditorSceneManager.MarkSceneDirty(player.scene);

        // 새 내려치기의 판정 구간을 칼 속도로 측정 (기존 공격도 같은 방식으로 다시 맞춰짐)
        EditorApplication.ExecuteMenuItem("Tools/Setup/Fit Attack Hit Windows");

        Selection.activeGameObject = player;
        Debug.Log($"[ChargeSetup] 완료 · 우클릭 탭=강공격/간파, 홀드=차지 강타. 가드 컴포넌트 {guards}개·상태 {removed}개 제거");
    }

    // 피해는 차지 단계 배율(1.4·2·3배)이 곱해지는 기준값. 판정 구간은 Fit Attack Hit Windows가 다시 계산
    static AttackData GetOrCreateReleaseAttack(AnimationClip clip)
    {
        var data = AssetDatabase.LoadAssetAtPath<AttackData>(AttackPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<AttackData>();
            AssetDatabase.CreateAsset(data, AttackPath);
        }

        var so = new SerializedObject(data);
        so.FindProperty("clip").objectReferenceValue = clip;
        so.FindProperty("stateName").stringValue = ReleaseState;
        so.FindProperty("damage").floatValue = 16f;
        so.FindProperty("postureDamage").floatValue = 15f;
        so.FindProperty("weight").enumValueIndex = (int)AttackWeight.Heavy;
        so.FindProperty("hitStart").floatValue = 0.3f;
        so.FindProperty("hitEnd").floatValue = 0.5f;
        so.FindProperty("inputOpen").floatValue = 0.6f;
        so.FindProperty("comboTransition").floatValue = 0.95f; // 다음 연결 없음 (콤보 마무리 취급)
        so.FindProperty("recoveryEnd").floatValue = 0.9f;
        so.FindProperty("moveCancelStart").floatValue = 0.6f;
        so.FindProperty("stepDistance").floatValue = 1.2f;
        so.FindProperty("stepEnd").floatValue = 0.35f;
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        return data;
    }

    // 타입 이름으로 찾음 → PlayerGuard.cs를 지운 뒤에도 이 셋업은 컴파일됨
    static int RemoveGuard(GameObject player)
    {
        int count = 0;
        foreach (MonoBehaviour mb in player.GetComponents<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "PlayerGuard") continue;
            Undo.DestroyObjectImmediate(mb);
            count++;
        }
        count += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(player);
        return count;
    }

    static void SetState(AnimatorStateMachine sm, string name, AnimationClip clip, bool loop)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime != loop)
        {
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }

        AnimatorState state = FindState(sm, name) ?? sm.AddState(name);
        state.motion = clip;
    }

    static int RemoveStates(AnimatorStateMachine sm, params string[] names)
    {
        int count = 0;
        foreach (string name in names)
        {
            AnimatorState state = FindState(sm, name);
            if (state == null) continue;
            sm.RemoveState(state);
            count++;
        }
        return count;
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name) =>
        sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

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

    static void SetRefs(Object target, params (string field, Object value)[] refs)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        foreach ((string field, Object value) in refs)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null) prop.objectReferenceValue = value;
            else Debug.LogWarning($"[ChargeSetup] {target.GetType().Name}에 '{field}' 필드가 없습니다.");
        }
        so.ApplyModifiedProperties();
    }

    static InputActionReference FindAction(string actionName)
    {
        var reference = AssetDatabase.LoadAllAssetsAtPath(InputPath)
            .OfType<InputActionReference>()
            .FirstOrDefault(r => r.action != null && r.action.actionMap.name == "Player" && r.action.name == actionName);
        if (reference == null) Debug.LogWarning($"[ChargeSetup] Player/{actionName} 액션을 찾지 못했습니다.");
        return reference;
    }
}
