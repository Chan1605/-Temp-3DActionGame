using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 보스 흑뢰: 검병 프리팹의 Variant(1.3배, 검붉은 몸) + 검병 컨트롤러에 보스 상태 + 플레이어 컨트롤러에 잡힘(Grabbed) 상태
// 공격 데이터(연속 베기 4타 · 잡기 · 도약 내려찍기 · 검기 3연)와 보스 적 데이터를 만듦. 수치는 처음 만들 때만 넣어 이후 튜닝 값을 보존
// 이어서 Tools/Map/Generate All Zones를 실행하면 폐사찰 다음에 보스 구역(흑뢰의 제단)이 생김
public static class BossSetup
{
    const string AnimRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/";
    const string ComboClip = AnimRoot + "02_Attack/03_Combo_Attack_03/Combo_Attack_03_0{0}.anim";
    const string WaveClip = AnimRoot + "02_Attack/05_Combo_Attack_Wave_05/Combo_Attack_Wave_05_0{0}.anim";
    const string GrabClip = AnimRoot + "02_Attack/15_Execution/01_Execution/Execution_0{0}.anim";          // [변경] 1~3
    const string GrabbedClip = AnimRoot + "02_Attack/15_Execution/02_Target/Execution_Target_0{0}.anim";
    const string LeapAirClip = AnimRoot + "02_Attack/08_Attack_Air_to_Floor_01/Attack_Air_To_Floor_Start_01.anim";
    const string LeapSlamClip = AnimRoot + "02_Attack/08_Attack_Air_to_Floor_01/Attack_Air_To_Floor_End_01.anim";
    const string LeapWindupClip = AnimRoot + "02_Attack/16_Power_Attack/Power_Attack_Start.anim";
    const string RoarClip = AnimRoot + "02_Attack/16_Power_Attack/Power_Attack_Loop.anim";
    const string IntroClip = AnimRoot + "01_Idle/Idle_To_Idle_Combat.anim";
    const string StaggerClip = AnimRoot + "08_Hit/06_Hit_Large_Combat/Hit_Large_Combat_B.anim";
    const string GetUpClip = AnimRoot + "08_Hit/09_Get_Up/Get_Up_Combat.anim";
    const string WaveVisualPath = "Assets/ThirdParty/MasterStylizedProjectiles/Projectiles/RedSwordBeam/Prefabs/Par_RedSwordBeam.prefab";
    const string WaveHitPath = "Assets/ThirdParty/MasterStylizedProjectiles/Projectiles/RedSwordBeam/Prefabs/Par_RedSwordBeam_Hit.prefab";

    const string EnemyController = "Assets/5.Animation/Enemy/Swordsman.controller";
    const string PlayerController = "Assets/5.Animation/Player/Player.controller";
    const string EnemyFolder = "Assets/11.Data/Enemy/";
    const string BaseData = EnemyFolder + "Swordsman.asset";
    const string BasePrefab = "Assets/2.Model/Prefabs/Swordsman.prefab";
    public const string BossPrefab = "Assets/2.Model/Prefabs/Swordsman_Boss.prefab";
    const string TelegraphMaterialPath = "Assets/6.Materials/Boss_Telegraph.mat";

    static readonly Color BossTint = new Color(0.16f, 0.05f, 0.07f);
    const float BossScale = 1.3f;

    [MenuItem("Tools/Setup/Boss")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[BossSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var enemyController = AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyController);
        var playerController = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerController);
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefab);
        if (enemyController == null || playerController == null || basePrefab == null || AssetDatabase.LoadAssetAtPath<EnemyData>(BaseData) == null)
        {
            Debug.LogError("[BossSetup] 검병 컨트롤러 · 플레이어 컨트롤러 · 검병 프리팹 · 검병 데이터 중 없는 것이 있습니다. Swordsman 셋업을 먼저 실행하세요.");
            return;
        }

        AnimationClip[] combo = Enumerable.Range(1, 4).Select(i => Clip(string.Format(ComboClip, i), false)).ToArray();
        AnimationClip[] wave = Enumerable.Range(1, 3).Select(i => Clip(string.Format(WaveClip, i), false)).ToArray();
        AnimationClip[] grabs = Enumerable.Range(1, 3).Select(i => Clip(string.Format(GrabClip, i), false)).ToArray(); // [변경]
        AnimationClip[] grabbeds = Enumerable.Range(1, 3).Select(i => Clip(string.Format(GrabbedClip, i), false)).ToArray();
        AnimationClip grab = grabs[0], grabbed = grabbeds[0];
        AnimationClip leapAir = Clip(LeapAirClip, false), leapSlam = Clip(LeapSlamClip, false), leapWindup = Clip(LeapWindupClip, false);
        AnimationClip roar = Clip(RoarClip, true), intro = Clip(IntroClip, false), stagger = Clip(StaggerClip, false), getUp = Clip(GetUpClip, false);
        if (combo.Concat(wave).Concat(grabs).Concat(grabbeds).Concat(new[] { grab, grabbed, leapAir, leapSlam, leapWindup, roar, intro, stagger, getUp }).Any(c => c == null)) return;

        // 1) 보스 상태 (검병 공용 컨트롤러. 다른 적은 이 상태를 부르지 않음)
        AnimatorStateMachine sm = enemyController.layers[0].stateMachine;
        for (int i = 0; i < 4; i++) State(sm, $"Boss_Combo_{i + 1}").motion = combo[i];
        for (int i = 0; i < 3; i++) State(sm, $"Boss_Wave_{i + 1}").motion = wave[i];
        State(sm, "Boss_Grab").motion = grab;
        for (int i = 0; i < 3; i++) State(sm, $"Boss_Grab_{i + 1}").motion = grabs[i]; // [변경] 잡기 3종 (무작위)
        State(sm, "Boss_LeapAir").motion = leapAir;
        State(sm, "Boss_LeapSlam").motion = leapSlam;
        State(sm, "Boss_Roar").motion = roar;
        State(sm, "Boss_Intro").motion = intro;
        State(sm, "Boss_Stagger").motion = stagger;
        State(sm, "Boss_GetUp").motion = getUp;
        AnimatorState windup = State(sm, "Charge_Windup"); // 돌격병과 같은 상태 (없을 때만 채움)
        if (windup.motion == null) windup.motion = leapWindup;
        EditorUtility.SetDirty(enemyController);

        // 플레이어: 보스에게 잡혔을 때 짝 모션
        State(playerController.layers[0].stateMachine, "Grabbed").motion = grabbed;
        for (int i = 0; i < 3; i++) State(playerController.layers[0].stateMachine, $"Grabbed_{i + 1}").motion = grabbeds[i]; // [변경]
        EditorUtility.SetDirty(playerController);

        // 2) 공격 데이터
        float[] comboDamage = { 14f, 14f, 16f, 26f };
        AttackData next = null;
        AttackData[] comboData = new AttackData[4];
        for (int i = 3; i >= 0; i--)
        {
            int n = i;
            comboData[i] = Attack($"Boss_Combo_{i + 1}", $"Boss_Combo_{i + 1}", combo[i], next, a =>
            {
                a.FindProperty("damage").floatValue = comboDamage[n];
                a.FindProperty("postureDamage").floatValue = 10f;
                a.FindProperty("weight").enumValueIndex = (int)(n == 3 ? AttackWeight.Heavy : AttackWeight.Light);
                a.FindProperty("stepDistance").floatValue = n == 3 ? 1.2f : 0.7f;
            });
            next = comboData[i];
        }

        next = null;
        AttackData[] waveData = new AttackData[3];
        for (int i = 2; i >= 0; i--)
        {
            waveData[i] = Attack($"Boss_Wave_{i + 1}", $"Boss_Wave_{i + 1}", wave[i], next, a =>
            {
                a.FindProperty("parryable").boolValue = false;
                a.FindProperty("weight").enumValueIndex = (int)AttackWeight.Heavy;
                a.FindProperty("stepDistance").floatValue = 0.3f;
            });
            next = waveData[i];
        }

        AttackData grabData = Attack("Boss_Grab", "Boss_Grab", grab, null, a =>
        {
            a.FindProperty("parryable").boolValue = false;
            a.FindProperty("weight").enumValueIndex = (int)AttackWeight.Heavy;
            a.FindProperty("stepDistance").floatValue = 0f;
        });
        AttackData leapData = Attack("Boss_LeapSlam", "Boss_LeapSlam", leapSlam, null, a =>
        {
            a.FindProperty("parryable").boolValue = false;
            a.FindProperty("weight").enumValueIndex = (int)AttackWeight.Heavy;
            a.FindProperty("knockDown").boolValue = true;
            a.FindProperty("stepDistance").floatValue = 0f;
        });

        // 3) 보스 데이터 (검병 데이터 복사)
        EnemyData bossData = Data("Swordsman_Boss", d =>
        {
            d.FindProperty("firstAttack").objectReferenceValue = comboData[0];
            d.FindProperty("grabAttack").objectReferenceValue = grabData;
            d.FindProperty("leapAttack").objectReferenceValue = leapData;
            d.FindProperty("waveAttack").objectReferenceValue = waveData[0];
        }, d =>
        {
            d.FindProperty("maxHealth").floatValue = 900f;
            d.FindProperty("detectRange").floatValue = 40f;
            d.FindProperty("attackRange").floatValue = 2.6f;
            d.FindProperty("walkSpeed").floatValue = 1.8f;
            d.FindProperty("runSpeed").floatValue = 4.8f;
            d.FindProperty("runDistance").floatValue = 4f;
            d.FindProperty("windupTime").floatValue = 0.4f;
            d.FindProperty("attackCooldown").floatValue = 1.2f;
            d.FindProperty("parryStunTime").floatValue = 0.9f;
            d.FindProperty("heavyKnockback").floatValue = 0.3f;
            d.FindProperty("xpReward").intValue = 200;
        });

        // 4) 프리팹 Variant
        Material telegraph = TelegraphMaterial();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = "Swordsman_Boss";
        instance.transform.localScale = Vector3.one * BossScale;
        var so = new SerializedObject(instance.GetComponent<EnemySwordsman>());
        so.FindProperty("data").objectReferenceValue = bossData;
        so.FindProperty("bodyTint").colorValue = BossTint;
        so.FindProperty("showWindupTell").boolValue = false; // 연속 베기는 예고 없음, 잡기·도약만 색으로 예고
        so.FindProperty("telegraphMaterial").objectReferenceValue = telegraph;
        so.FindProperty("waveVisual").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(WaveVisualPath);
        so.FindProperty("waveHitVfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(WaveHitPath);
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(instance, BossPrefab);
        Object.DestroyImmediate(instance);

        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(EnemyController);
        PlayerClipFixer.Fix(PlayerController);
        EditorApplication.ExecuteMenuItem("Tools/Setup/Fit Attack Hit Windows");
        Debug.Log("[BossSetup] 완료 · Swordsman_Boss 프리팹. 이어서 Tools/Map/Generate All Zones로 보스 구역을 만드세요. (바로 보려면 Tools/Setup/Add Boss (Test))");
    }

    // 테스트용: 플레이어 앞 10m (부활하는 일반 배치)
    [MenuItem("Tools/Setup/Add Boss (Test)")]
    static void AddBoss()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefab);
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (prefab == null || player == null)
        {
            Debug.LogError("[BossSetup] 보스 프리팹 또는 Player가 없습니다. Tools/Setup/Boss를 먼저 실행하세요.");
            return;
        }

        Transform p = player.transform;
        Vector3 pos = p.position + p.forward * 10f;
        if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-p.forward, Vector3.up));
        Undo.RegisterCreatedObjectUndo(go, "Add Boss");
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    static Material TelegraphMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(TelegraphMaterialPath);
        if (mat != null) return mat;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return null; // 실행 중에 만들어 씀
        if (!AssetDatabase.IsValidFolder("Assets/6.Materials")) AssetDatabase.CreateFolder("Assets", "6.Materials");
        mat = new Material(shader) { name = "Boss_Telegraph" };
        BossTelegraph.MakeTransparent(mat);
        AssetDatabase.CreateAsset(mat, TelegraphMaterialPath);
        return mat;
    }

    static AttackData Attack(string assetName, string stateName, AnimationClip clip, AttackData nextLight, System.Action<SerializedObject> firstValues)
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
        so.FindProperty("nextLight").objectReferenceValue = nextLight;
        if (created) firstValues(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        return data;
    }

    static EnemyData Data(string assetName, System.Action<SerializedObject> always, System.Action<SerializedObject> firstValues)
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
        so.FindProperty("kind").enumValueIndex = (int)EnemyKind.Boss;
        always(so);
        if (created) firstValues(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        return data;
    }

    static AnimationClip Clip(string path, bool loop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            Debug.LogError($"[BossSetup] {path}를 찾지 못했습니다.");
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
