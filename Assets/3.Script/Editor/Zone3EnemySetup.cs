using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 구역 3 적: 방패병(가드 자세로 다가와 정면을 막음) · 돌격병(먼 거리에서 돌진 → 내려베기 → 탈진)
// 검병 프리팹의 Variant + 검병 컨트롤러에 상태 추가. 수치는 처음 만들 때만 넣어 이후 튜닝 값을 보존
public static class Zone3EnemySetup
{
    const string AnimRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/";
    const string BlockLoop = AnimRoot + "08_Hit/12_Block/Block_Loop.anim";
    const string BlockHit = AnimRoot + "08_Hit/12_Block/Block_Hit.anim";
    const string BlockBreak = AnimRoot + "08_Hit/12_Block/Block_Hit_Break.anim";
    const string BlockWalk = AnimRoot + "03_Walk/03_Walk_Block_RM/01_Walk_Block_F_0_RM/Walk_Block_Loop_F_0_RM.anim";
    const string GuardAttackClip = AnimRoot + "02_Attack/04_Combo_Attack_04/Combo_Attack_04_04.anim";
    const string PowerStart = AnimRoot + "02_Attack/16_Power_Attack/Power_Attack_Start.anim";
    const string PowerEnd = AnimRoot + "02_Attack/16_Power_Attack/Power_Attack_End.anim";
    const string FastRun = AnimRoot + "04_Run/02_Run_Combat_RM/11_Run_Fast_Combat_RM/Run_Fast_Combat_Loop_RM.anim";

    const string EnemyController = "Assets/5.Animation/Enemy/Swordsman.controller";
    const string EnemyFolder = "Assets/11.Data/Enemy/";
    const string BaseData = EnemyFolder + "Swordsman.asset";
    const string BasePrefab = "Assets/2.Model/Prefabs/Swordsman.prefab";
    const string GuardianPrefab = "Assets/2.Model/Prefabs/Swordsman_Guardian.prefab";
    const string ChargerPrefab = "Assets/2.Model/Prefabs/Swordsman_Charger.prefab";

    static readonly Color GuardianTint = new Color(0.2f, 0.26f, 0.36f); // 푸른 강철
    static readonly Color ChargerTint = new Color(0.38f, 0.24f, 0.08f); // 황토

    [MenuItem("Tools/Setup/Zone 3 Enemies")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Zone3EnemySetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyController);
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefab);
        if (controller == null || basePrefab == null || AssetDatabase.LoadAssetAtPath<EnemyData>(BaseData) == null)
        {
            Debug.LogError("[Zone3EnemySetup] 검병 컨트롤러 · 프리팹 · 데이터가 없습니다. Swordsman Enemy 셋업을 먼저 실행하세요.");
            return;
        }

        AnimationClip blockLoop = Clip(BlockLoop, true), blockWalk = Clip(BlockWalk, true), fastRun = Clip(FastRun, true);
        AnimationClip blockHit = Clip(BlockHit, false), blockBreak = Clip(BlockBreak, false);
        AnimationClip guardAttack = Clip(GuardAttackClip, false), powerStart = Clip(PowerStart, false), powerEnd = Clip(PowerEnd, false);
        if (new[] { blockLoop, blockWalk, fastRun, blockHit, blockBreak, guardAttack, powerStart, powerEnd }.Any(c => c == null)) return;

        // 1) 컨트롤러 상태 (검병 공용 컨트롤러에 추가해도 기존 적은 이 상태를 부르지 않음)
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        SetGuardMoveTree(controller, sm, blockLoop, blockWalk);
        State(sm, "Guard_Hit").motion = blockHit;
        State(sm, "Guard_Break").motion = blockBreak;
        State(sm, "Attack_Guard").motion = guardAttack;
        State(sm, "Charge_Windup").motion = powerStart;
        State(sm, "Charge_Run").motion = fastRun;
        State(sm, "Charge_Strike").motion = powerEnd;
        EditorUtility.SetDirty(controller);

        // 2) 공격 데이터
        AttackData guardHit = Attack("Guardian_Attack", "Attack_Guard", guardAttack, a =>
        {
            a.FindProperty("damage").floatValue = 18f;
            a.FindProperty("postureDamage").floatValue = 12f;
            a.FindProperty("weight").enumValueIndex = (int)AttackWeight.Heavy;
            a.FindProperty("stepDistance").floatValue = 0.8f;
        });
        AttackData strike = Attack("Charger_Strike", "Charge_Strike", powerEnd, a =>
        {
            a.FindProperty("damage").floatValue = 22f;
            a.FindProperty("postureDamage").floatValue = 20f;
            a.FindProperty("weight").enumValueIndex = (int)AttackWeight.Heavy;
            a.FindProperty("stepDistance").floatValue = 1.6f; // 돌진 관성
            a.FindProperty("stepEnd").floatValue = 0.35f;
        });

        // 3) 적 데이터 (검병 데이터 복사)
        EnemyData guardianData = Data("Swordsman_Guardian", EnemyKind.Guardian, d =>
        {
            d.FindProperty("firstAttack").objectReferenceValue = guardHit;
        }, d =>
        {
            d.FindProperty("maxHealth").floatValue = 90f;
            d.FindProperty("walkSpeed").floatValue = 1.4f;
            d.FindProperty("attackRange").floatValue = 2.4f;
            d.FindProperty("windupTime").floatValue = 0.6f;
            d.FindProperty("attackCooldown").floatValue = 2f;
            d.FindProperty("parryStunTime").floatValue = 1.4f;
            d.FindProperty("xpReward").intValue = 18;
        });
        EnemyData chargerData = Data("Swordsman_Charger", EnemyKind.Charger, d =>
        {
            d.FindProperty("chargeAttack").objectReferenceValue = strike;
        }, d =>
        {
            d.FindProperty("maxHealth").floatValue = 55f;
            d.FindProperty("runSpeed").floatValue = 4.8f;
            d.FindProperty("attackCooldown").floatValue = 1.4f;
            d.FindProperty("xpReward").intValue = 16;
        });

        // 4) 프리팹 Variant
        Variant(basePrefab, GuardianPrefab, "Swordsman_Guardian", guardianData, GuardianTint, 1.06f, true);
        Variant(basePrefab, ChargerPrefab, "Swordsman_Charger", chargerData, ChargerTint, 0.98f, false);

        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(EnemyController);
        EditorApplication.ExecuteMenuItem("Tools/Setup/Fit Attack Hit Windows");
        Debug.Log("[Zone3EnemySetup] 완료 · 방패병 · 돌격병 프리팹. 이어서 Tools/Map/Generate All Zones로 폐사찰 구역을 만드세요.");
    }

    // 테스트용: 플레이어 앞에 하나씩 (부활하는 일반 배치)
    [MenuItem("Tools/Setup/Add Guardian (Test)")]
    static void AddGuardian() => SpawnInFront(GuardianPrefab, -2f);

    [MenuItem("Tools/Setup/Add Charger (Test)")]
    static void AddCharger() => SpawnInFront(ChargerPrefab, 2f);

    static void SpawnInFront(string path, float side)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (prefab == null || player == null)
        {
            Debug.LogError("[Zone3EnemySetup] 프리팹 또는 Player가 없습니다. Tools/Setup/Zone 3 Enemies를 먼저 실행하세요.");
            return;
        }

        Transform p = player.transform;
        Vector3 pos = p.position + p.forward * 8f + p.right * side;
        if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-p.forward, Vector3.up));
        Undo.RegisterCreatedObjectUndo(go, "Add Zone 3 Enemy");
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    // 가드 걷기: MoveBlend 0 = 제자리 가드, 1 = 가드한 채 걷기 (가드 중엔 달리지 않음)
    static void SetGuardMoveTree(AnimatorController controller, AnimatorStateMachine sm, AnimationClip idle, AnimationClip walk)
    {
        AnimatorState state = State(sm, "Guard_Move");
        if (state.motion is BlendTree old && AssetDatabase.GetAssetPath(old) == EnemyController)
            AssetDatabase.RemoveObjectFromAsset(old);

        var tree = new BlendTree
        {
            name = "Guard_Move",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "MoveBlend",
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, 1f);
        state.motion = tree;
    }

    static AttackData Attack(string assetName, string stateName, AnimationClip clip, System.Action<SerializedObject> firstValues)
    {
        string path = EnemyFolder + assetName + ".asset";
        var data = AssetDatabase.LoadAssetAtPath<AttackData>(path);
        bool created = data == null;
        if (created)
        {
            data = ScriptableObject.CreateInstance<AttackData>();
            AssetDatabase.CreateAsset(data, path);
        }

        var so = new SerializedObject(data);
        so.FindProperty("clip").objectReferenceValue = clip;
        so.FindProperty("stateName").stringValue = stateName;
        so.FindProperty("nextLight").objectReferenceValue = null; // 단타
        if (created) firstValues(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        return data;
    }

    // always: 다시 실행할 때마다 맞추는 연결 값 / firstValues: 처음 만들 때만 넣는 수치
    static EnemyData Data(string assetName, EnemyKind kind, System.Action<SerializedObject> always, System.Action<SerializedObject> firstValues)
    {
        string path = EnemyFolder + assetName + ".asset";
        var data = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
        bool created = data == null;
        if (created)
        {
            AssetDatabase.CopyAsset(BaseData, path);
            data = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
        }

        var so = new SerializedObject(data);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        always(so);
        if (created) firstValues(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        return data;
    }

    static void Variant(GameObject basePrefab, string path, string name, EnemyData data, Color tint, float scale, bool windupTell)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = name;
        instance.transform.localScale = Vector3.one * scale;
        var so = new SerializedObject(instance.GetComponent<EnemySwordsman>());
        so.FindProperty("data").objectReferenceValue = data;
        so.FindProperty("bodyTint").colorValue = tint;
        so.FindProperty("showWindupTell").boolValue = windupTell;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);
    }

    static AnimationClip Clip(string path, bool loop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            Debug.LogError($"[Zone3EnemySetup] {path}를 찾지 못했습니다.");
            return null;
        }
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime != loop)
        {
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }
        return clip;
    }

    static AnimatorState State(AnimatorStateMachine sm, string name) =>
        sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? sm.AddState(name);
}
