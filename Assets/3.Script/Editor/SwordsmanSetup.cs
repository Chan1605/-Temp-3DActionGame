using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SwordsmanSetup
{
    const string AnimRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/";
    const string IdlePath = AnimRoot + "01_Idle/Idle_Combat.anim";
    const string WalkPath = AnimRoot + "03_Walk/02_Walk_Combat_RM/01_Walk_Combat_F_0_RM/Walk_Combat_Loop_F_0_RM.anim";
    const string RunPath = AnimRoot + "04_Run/02_Run_Combat_RM/01_Run_Combat_F_0_RM/Run_Combat_Loop_F_0_RM.anim";
    const string AttackRoot = AnimRoot + "02_Attack/01_Combo_Attack_01/";
    const string HitRoot = AnimRoot + "08_Hit/";

    const string ModelPath = "Assets/ThirdParty/SwordAnimationPack/Model/9CG_Sword.FBX";
    const string PlayerControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string ControllerFolder = "Assets/5.Animation/Enemy";
    const string ControllerPath = ControllerFolder + "/Swordsman.controller";
    const string DataFolder = "Assets/11.Data/Enemy";
    const string EnemyDataPath = DataFolder + "/Swordsman.asset";
    const string PrefabPath = "Assets/2.Model/Prefabs/Swordsman.prefab";

    const string EnemyName = "Swordsman";
    const string SwordName = "sword";
    const string HitboxName = "Hitbox";
    const string PlayerTag = "Player";

    // [변경] 셋업을 다시 돌리면 프리팹의 색도 이 값으로 맞춤
    static readonly Color BodyTint = new Color(0.18f, 0.18f, 0.22f);
    static readonly Color WindupColor = new Color(1.6f, 0.7f, 0.15f);

    // 상태 이름, 클립 경로 (전부 1회 재생)
    static readonly (string state, string clip)[] OneShotStates =
    {
        ("Attack_1", AttackRoot + "Combo_Attack_01_01.anim"),
        ("Attack_2", AttackRoot + "Combo_Attack_01_02.anim"),
        ("Hit_F", HitRoot + "02_Hit_Combat/Hit_Combat_F.anim"),
        ("Hit_B", HitRoot + "02_Hit_Combat/Hit_Combat_B.anim"),
        ("Hit_L", HitRoot + "02_Hit_Combat/Hit_Combat_L.anim"),
        ("Hit_R", HitRoot + "02_Hit_Combat/Hit_Combat_R.anim"),
        ("HitLarge_F", HitRoot + "06_Hit_Large_Combat/Hit_Large_Combat_F.anim"),
        ("HitLarge_B", HitRoot + "06_Hit_Large_Combat/Hit_Large_Combat_B.anim"),
        ("HitLarge_L", HitRoot + "06_Hit_Large_Combat/Hit_Large_Combat_L.anim"),
        ("HitLarge_R", HitRoot + "06_Hit_Large_Combat/Hit_Large_Combat_R.anim"),
        ("Death", HitRoot + "02_Hit_Combat/Hit_Combat_Death.anim"),
    };

    const string PlayerHitState = "Hit";
    const string PlayerHitClip = HitRoot + "02_Hit_Combat/Hit_Combat_F.anim";

    [MenuItem("Tools/Setup/Swordsman Enemy")]
    static void Run()
    {
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        var playerController = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        if (locomotion == null || playerController == null)
        {
            Debug.LogError("[SwordsmanSetup] Player 또는 Player.controller가 없습니다. Player Locomotion 셋업을 먼저 실행하세요.");
            return;
        }

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (avatar == null || model == null)
        {
            Debug.LogError("[SwordsmanSetup] 9CG_Sword.FBX 모델 또는 Avatar를 찾지 못했습니다.");
            return;
        }

        AnimatorController controller = BuildController();
        if (controller == null) return;

        AttackData attack1 = GetOrCreateAttack("Swordsman_Attack_1", "Attack_1", 10f);
        AttackData attack2 = GetOrCreateAttack("Swordsman_Attack_2", "Attack_2", 12f);
        LinkNext(attack1, attack2);
        EnemyData data = GetOrCreateEnemyData(attack1);
        AssetDatabase.SaveAssets();

        GameObject prefab = BuildPrefab(model, avatar, controller, data);
        SetupPlayer(locomotion.gameObject, playerController);

        if (Object.FindAnyObjectByType<EnemySwordsman>() == null) SpawnInFront(prefab, locomotion.transform, 0f);

        // 팩 .anim은 재생 바인딩이 비어 있을 수 있어 새로 쓰는 클립까지 다시 저장
        PlayerClipFixer.Fix(PlayerControllerPath);
        PlayerClipFixer.Fix(ControllerPath);

        EditorSceneManager.MarkSceneDirty(locomotion.gameObject.scene);
        Debug.Log("[SwordsmanSetup] 완료 · 검병 프리팹, Swordsman.controller, 플레이어 피격(PlayerHealth) 준비");
    }

    [MenuItem("Tools/Setup/Add Swordsman")]
    static void AddOne()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        if (prefab == null || locomotion == null)
        {
            Debug.LogError("[SwordsmanSetup] Swordsman 프리팹이 없습니다. Tools/Setup/Swordsman Enemy를 먼저 실행하세요.");
            return;
        }

        int count = Object.FindObjectsByType<EnemySwordsman>(FindObjectsSortMode.None).Length;
        SpawnInFront(prefab, locomotion.transform, (count % 2 == 0 ? 1 : -1) * (1 + count / 2) * 2.5f);
        EditorSceneManager.MarkSceneDirty(locomotion.gameObject.scene);
    }

    static AnimatorController BuildController()
    {
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
        var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkPath);
        var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunPath);
        if (idle == null || walk == null || run == null)
        {
            Debug.LogError("[SwordsmanSetup] 로코모션 클립을 찾지 못했습니다.");
            return null;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            if (!AssetDatabase.IsValidFolder(ControllerFolder)) AssetDatabase.CreateFolder("Assets/5.Animation", "Enemy");
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        if (!controller.parameters.Any(p => p.name == "MoveBlend"))
            controller.AddParameter("MoveBlend", AnimatorControllerParameterType.Float);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState locomotionState = FindOrAddState(sm, "Locomotion");

        if (locomotionState.motion is BlendTree oldTree && AssetDatabase.GetAssetPath(oldTree) == ControllerPath)
            AssetDatabase.RemoveObjectFromAsset(oldTree);

        var tree = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "MoveBlend",
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, 1f);
        tree.AddChild(run, 2f);
        locomotionState.motion = tree;
        sm.defaultState = locomotionState;

        foreach ((string state, string clipPath) in OneShotStates)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                Debug.LogError($"[SwordsmanSetup] {clipPath} 클립을 찾지 못했습니다.");
                return null;
            }

            SetLoop(clip, false);
            FindOrAddState(sm, state).motion = clip;
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }

    // 클립·상태 이름은 매번 맞추고, 수치는 처음 만들 때만 넣어 튜닝 값을 보존
    static AttackData GetOrCreateAttack(string assetName, string stateName, float damage)
    {
        string path = $"{DataFolder}/{assetName}.asset";
        var data = AssetDatabase.LoadAssetAtPath<AttackData>(path);
        bool created = data == null;

        if (created)
        {
            EnsureDataFolder();
            data = ScriptableObject.CreateInstance<AttackData>();
            AssetDatabase.CreateAsset(data, path);
        }

        string clipPath = OneShotStates.First(s => s.state == stateName).clip;
        var so = new SerializedObject(data);
        so.FindProperty("clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        so.FindProperty("stateName").stringValue = stateName;

        if (created)
        {
            so.FindProperty("damage").floatValue = damage;
            so.FindProperty("postureDamage").floatValue = damage * 0.5f;
            so.FindProperty("stepDistance").floatValue = 0.4f;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        return data;
    }

    static void LinkNext(AttackData from, AttackData next)
    {
        var so = new SerializedObject(from);
        so.FindProperty("nextLight").objectReferenceValue = next;
        so.FindProperty("nextHeavy").objectReferenceValue = null;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static EnemyData GetOrCreateEnemyData(AttackData firstAttack)
    {
        var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyDataPath);
        if (data == null)
        {
            EnsureDataFolder();
            data = ScriptableObject.CreateInstance<EnemyData>();
            AssetDatabase.CreateAsset(data, EnemyDataPath);
        }

        var so = new SerializedObject(data);
        so.FindProperty("firstAttack").objectReferenceValue = firstAttack;
        so.ApplyModifiedPropertiesWithoutUndo();
        return data;
    }

    // 이미 있으면 내용만 갱신해서 씬 인스턴스의 위치 등 오버라이드를 보존
    static GameObject BuildPrefab(GameObject model, Avatar avatar, AnimatorController controller, EnemyData data)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            Animator existingAnimator = contents.GetComponentInChildren<Animator>();
            if (existingAnimator != null)
            {
                existingAnimator.runtimeAnimatorController = controller;
                existingAnimator.avatar = avatar;
                existingAnimator.applyRootMotion = false;
            }

            var existingSo = new SerializedObject(contents.GetComponent<EnemySwordsman>());
            existingSo.FindProperty("data").objectReferenceValue = data;
            existingSo.FindProperty("animator").objectReferenceValue = existingAnimator;
            existingSo.FindProperty("bodyTint").colorValue = BodyTint; // [변경]
            existingSo.FindProperty("windupColor").colorValue = WindupColor;
            existingSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        var root = new GameObject(EnemyName);
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
        body.name = "Model";
        body.transform.SetParent(root.transform, false);

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.avatar = avatar;
        animator.applyRootMotion = false;

        WeaponHitbox hitbox = CreateHitbox(body.transform, root.transform);

        var enemy = root.AddComponent<EnemySwordsman>();
        var so = new SerializedObject(enemy);
        so.FindProperty("data").objectReferenceValue = data;
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("weapon").objectReferenceValue = hitbox;
        so.FindProperty("bodyTint").colorValue = BodyTint; // [변경]
        so.FindProperty("windupColor").colorValue = WindupColor;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (!AssetDatabase.IsValidFolder("Assets/2.Model/Prefabs")) AssetDatabase.CreateFolder("Assets/2.Model", "Prefabs");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static WeaponHitbox CreateHitbox(Transform body, Transform owner)
    {
        Transform sword = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == SwordName);
        if (sword == null)
        {
            Debug.LogWarning($"[SwordsmanSetup] 모델에서 '{SwordName}'을 찾지 못해 히트박스 없이 진행합니다.");
            return null;
        }

        var go = new GameObject(HitboxName);
        go.transform.SetParent(sword, false);

        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;

        MeshFilter mf = sword.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Bounds b = mf.sharedMesh.bounds;
            box.center = b.center;
            box.size = Vector3.Max(b.size * 1.1f, Vector3.one * 0.08f / Mathf.Max(sword.lossyScale.x, 0.0001f));
        }

        var hitbox = go.AddComponent<WeaponHitbox>();
        var so = new SerializedObject(hitbox);
        so.FindProperty("box").objectReferenceValue = box;
        so.FindProperty("owner").objectReferenceValue = owner;
        so.FindProperty("targetTag").stringValue = PlayerTag;
        so.ApplyModifiedPropertiesWithoutUndo();
        return hitbox;
    }

    static void SetupPlayer(GameObject player, AnimatorController playerController)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerHitClip);
        if (clip != null)
        {
            SetLoop(clip, false);
            FindOrAddState(playerController.layers[0].stateMachine, PlayerHitState).motion = clip;
            EditorUtility.SetDirty(playerController);
        }

        if (!player.CompareTag(PlayerTag))
        {
            Undo.RecordObject(player, "Tag Player");
            player.tag = PlayerTag;
        }

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health == null) health = Undo.AddComponent<PlayerHealth>(player);

        var so = new SerializedObject(health);
        so.FindProperty("animator").objectReferenceValue = player.GetComponentInChildren<Animator>();
        so.FindProperty("combat").objectReferenceValue = player.GetComponent<PlayerCombat>();
        so.FindProperty("locomotion").objectReferenceValue = player.GetComponent<PlayerLocomotion>();
        so.FindProperty("skill").objectReferenceValue = player.GetComponent<PlayerSkill>();
        so.FindProperty("followCamera").objectReferenceValue = Object.FindAnyObjectByType<ThirdPersonCamera>();
        so.ApplyModifiedProperties();
    }

    static void SpawnInFront(GameObject prefab, Transform player, float sideOffset)
    {
        var enemy = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(enemy, "Spawn Swordsman");

        Vector3 pos = player.position + player.forward * 7f + player.right * sideOffset;
        enemy.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-player.forward, Vector3.up));
        Selection.activeGameObject = enemy;
    }

    static AnimatorState FindOrAddState(AnimatorStateMachine sm, string name)
    {
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
        return state != null ? state : sm.AddState(name);
    }

    static void SetLoop(AnimationClip clip, bool loop)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime == loop) return;

        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }

    static void EnsureDataFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/11.Data")) AssetDatabase.CreateFolder("Assets", "11.Data");
        if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/11.Data", "Enemy");
    }
}
