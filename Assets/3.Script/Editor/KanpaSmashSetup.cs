using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 1) 뇌락(간파 후속 내려찍기): Player.controller 상태 + AttackData + PlayerKanpa 연결
// 2) 간파 수련 검병: 제자리에서 한 박자씩 예고(주황 발광) 후 단타를 반복하는 검병 Variant. 체력 9999 · 낮은 피해 · 구역 전투 제외
// 마지막에 클립 정리 + 판정 구간 측정. 수치는 처음 만들 때만 넣어 이후 튜닝 값을 보존
public static class KanpaSmashSetup
{
    const string AttackRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/02_Attack/";
    const string SmashClip = AttackRoot + "08_Attack_Air_to_Floor_01/Attack_Air_To_Floor_End_01.anim";
    const string TrainerClip = AttackRoot + "02_Combo_Attack_02/Combo_Attack_02_01.anim";

    const string PlayerController = "Assets/5.Animation/Player/Player.controller";
    const string EnemyController = "Assets/5.Animation/Enemy/Swordsman.controller";
    const string AttackFolder = "Assets/11.Data/Attack/";
    const string EnemyFolder = "Assets/11.Data/Enemy/";
    const string BaseEnemyData = EnemyFolder + "Swordsman.asset";
    const string BasePrefab = "Assets/2.Model/Prefabs/Swordsman.prefab";
    const string TrainerPrefabPath = "Assets/2.Model/Prefabs/Swordsman_Trainer.prefab";
    const string SmashState = "Kanpa_Smash";
    const string SpeedParam = "AttackSpeed";

    static readonly Color TrainerTint = new Color(0.16f, 0.32f, 0.36f);

    [MenuItem("Tools/Setup/Kanpa Smash + Trainer")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[KanpaSmashSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        bool smashOk = SetupSmash();
        bool trainerOk = SetupTrainer();

        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(PlayerController);
        PlayerClipFixer.Fix(EnemyController);
        EditorApplication.ExecuteMenuItem("Tools/Setup/Fit Attack Hit Windows");

        Debug.Log($"[KanpaSmashSetup] 완료 · 뇌락 {(smashOk ? "O" : "X")} · 간파 수련 검병 {(trainerOk ? "O" : "X")}");
    }

    // ===== 뇌락 =====
    static bool SetupSmash()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerController);
        var clip = LoadOneShot(SmashClip);
        var kanpa = Object.FindAnyObjectByType<PlayerKanpa>();
        if (controller == null || clip == null || kanpa == null)
        {
            Debug.LogError("[KanpaSmashSetup] Player.controller · 내려찍기 클립 · 씬의 PlayerKanpa 중 하나가 없습니다. (PlayerKanpa는 Tools/Setup/Kanpa)");
            return false;
        }

        // 공속 배율은 PlayerCombat이 모든 공격 시간에 곱하므로 애니메이션도 같은 파라미터로 맞춰야 판정이 어긋나지 않음
        AnimatorState state = SetStateMotion(controller, SmashState, clip);
        if (controller.parameters.Any(p => p.name == SpeedParam))
        {
            state.speedParameter = SpeedParam;
            state.speedParameterActive = true;
        }
        EditorUtility.SetDirty(controller);

        AttackData data = GetOrCreateAttack(AttackFolder + "Kanpa_Smash.asset", SmashState, clip, out bool created);
        if (created)
        {
            var so = new SerializedObject(data);
            so.FindProperty("damage").floatValue = 45f;
            so.FindProperty("postureDamage").floatValue = 60f;
            so.FindProperty("weight").enumValueIndex = 1; // Heavy
            so.FindProperty("stepDistance").floatValue = 1.2f;
            so.FindProperty("stepEnd").floatValue = 0.3f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var kanpaSo = new SerializedObject(kanpa);
        kanpaSo.FindProperty("smashAttack").objectReferenceValue = data;
        kanpaSo.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(kanpa.gameObject.scene);
        return true;
    }

    // ===== 간파 수련 검병 =====
    static bool SetupTrainer()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyController);
        var clip = LoadOneShot(TrainerClip);
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefab);
        if (controller == null || clip == null || basePrefab == null || AssetDatabase.LoadAssetAtPath<EnemyData>(BaseEnemyData) == null)
        {
            Debug.LogError("[KanpaSmashSetup] 검병 컨트롤러 · 프리팹 · 데이터가 없습니다. Swordsman Enemy 셋업을 먼저 실행하세요.");
            return false;
        }

        // 단타 하나만 반복 (연타 없음) → 박자가 일정해 간파 타이밍을 익히기 좋음. 상태는 검병 공용 Attack_1
        SetStateMotion(controller, "Attack_1", clip);
        AttackData attack = GetOrCreateAttack(EnemyFolder + "Trainer_Attack.asset", "Attack_1", clip, out bool attackCreated);
        var attackSo = new SerializedObject(attack);
        attackSo.FindProperty("nextLight").objectReferenceValue = null;
        if (attackCreated)
        {
            attackSo.FindProperty("damage").floatValue = 5f;
            attackSo.FindProperty("postureDamage").floatValue = 2f;
            attackSo.FindProperty("stepDistance").floatValue = 0.2f;
        }
        attackSo.ApplyModifiedPropertiesWithoutUndo();

        string dataPath = EnemyFolder + "Swordsman_Trainer.asset";
        var data = AssetDatabase.LoadAssetAtPath<EnemyData>(dataPath);
        bool dataCreated = data == null;
        if (dataCreated)
        {
            AssetDatabase.CopyAsset(BaseEnemyData, dataPath);
            data = AssetDatabase.LoadAssetAtPath<EnemyData>(dataPath);
        }
        var dataSo = new SerializedObject(data);
        dataSo.FindProperty("firstAttack").objectReferenceValue = attack;
        if (dataCreated)
        {
            dataSo.FindProperty("maxHealth").floatValue = 9999f;
            dataSo.FindProperty("walkSpeed").floatValue = 0f;   // 제자리 (돌아보기만 함)
            dataSo.FindProperty("runSpeed").floatValue = 0f;
            dataSo.FindProperty("windupTime").floatValue = 0.8f;
            dataSo.FindProperty("attackCooldown").floatValue = 1.4f;
            dataSo.FindProperty("parryStunTime").floatValue = 1.0f;
            dataSo.FindProperty("xpReward").intValue = 0;
            dataSo.FindProperty("respawnTime").floatValue = 2f;
        }
        dataSo.ApplyModifiedPropertiesWithoutUndo();

        // Variant: 청록빛 몸 + 예고 발광 켬 + 구역 전투 제외
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = "Swordsman_Trainer";
        var enemySo = new SerializedObject(instance.GetComponent<EnemySwordsman>());
        enemySo.FindProperty("data").objectReferenceValue = data;
        enemySo.FindProperty("bodyTint").colorValue = TrainerTint;
        enemySo.FindProperty("showWindupTell").boolValue = true;
        enemySo.FindProperty("trainer").boolValue = true;
        enemySo.ApplyModifiedPropertiesWithoutUndo();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, TrainerPrefabPath);
        Object.DestroyImmediate(instance);

        PlaceInScene(prefab);
        return true;
    }

    // 씬에 없을 때만 플레이어 앞 6m에 하나 배치
    static void PlaceInScene(GameObject prefab)
    {
        if (Object.FindObjectsByType<EnemySwordsman>(FindObjectsSortMode.None).Any(e => e.IsTrainer)) return;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Vector3 pos = player != null ? player.transform.position + player.transform.forward * 6f : Vector3.zero;
        if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "KanpaTrainer";
        go.transform.position = pos;
        if (player != null) go.transform.rotation = Quaternion.LookRotation(-player.transform.forward, Vector3.up);
        Undo.RegisterCreatedObjectUndo(go, "Place Kanpa Trainer");
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    // ===== 공용 =====
    static AttackData GetOrCreateAttack(string path, string stateName, AnimationClip clip, out bool created)
    {
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

    static AnimationClip LoadOneShot(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            Debug.LogError($"[KanpaSmashSetup] {path}를 찾지 못했습니다.");
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

    static AnimatorState SetStateMotion(AnimatorController controller, string stateName, AnimationClip clip)
    {
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName) ?? sm.AddState(stateName);
        state.motion = clip;
        return state;
    }
}
