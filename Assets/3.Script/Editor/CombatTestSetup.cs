using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

public static class CombatTestSetup
{
    const string AttackClipRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/02_Attack/";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string InputPath = "Assets/10.Input/InputSystem_Actions.inputactions";
    const string DataRoot = "Assets/11.Data";
    const string AttackFolder = DataRoot + "/Attack";

    const string SwordName = "sword";
    const string HitboxName = "Hitbox";
    const string DummyName = "TrainingDummy";

    // 이전 버전 메뉴가 만든 상태, 콤보 트리로 대체되어 정리
    static readonly string[] LegacyStates = { "Attack1", "Attack2", "Attack3" };

    const string LightStarter = "Combo_01_1";
    const string HeavyStarter = "Combo_04_1";

    // 이름(=에셋 이름=Animator 상태 이름), 클립 경로, 피해, □ 다음, △ 다음
    static readonly ComboNode[] Nodes =
    {
        new ComboNode("Combo_01_1", "01_Combo_Attack_01/Combo_Attack_01_01", 10f, "Combo_01_2", "Combo_03_2"),
        new ComboNode("Combo_01_2", "01_Combo_Attack_01/Combo_Attack_01_02", 10f, "Combo_01_3", "Combo_02_3"),
        new ComboNode("Combo_01_3", "01_Combo_Attack_01/Combo_Attack_01_03", 12f, "Combo_01_4", "Wave_05_4"),
        new ComboNode("Combo_01_4", "01_Combo_Attack_01/Combo_Attack_01_04", 18f, null, null),

        new ComboNode("Wave_05_4", "05_Combo_Attack_Wave_05/Combo_Attack_Wave_05_04", 30f, null, null),

        new ComboNode("Combo_02_3", "02_Combo_Attack_02/Combo_Attack_02_03", 20f, null, "Combo_02_4"),
        new ComboNode("Combo_02_4", "02_Combo_Attack_02/Combo_Attack_02_04", 30f, null, null),

        new ComboNode("Combo_03_2", "03_Combo_Attack_03/Combo_Attack_03_02", 20f, null, "Combo_03_3"),
        new ComboNode("Combo_03_3", "03_Combo_Attack_03/Combo_Attack_03_03", 28f, null, null),

        new ComboNode("Combo_04_1", "04_Combo_Attack_04/Combo_Attack_04_01", 20f, null, "Combo_04_2"),
        new ComboNode("Combo_04_2", "04_Combo_Attack_04/Combo_Attack_04_02", 22f, null, "Combo_04_3"),
        new ComboNode("Combo_04_3", "04_Combo_Attack_04/Combo_Attack_04_03", 32f, null, null),
    };

    readonly struct ComboNode
    {
        public readonly string Name, ClipPath, Light, Heavy;
        public readonly float Damage;

        public ComboNode(string name, string clipPath, float damage, string light, string heavy)
        {
            Name = name;
            ClipPath = clipPath;
            Damage = damage;
            Light = light;
            Heavy = heavy;
        }

        public bool IsEnd => Light == null && Heavy == null;
    }

    [MenuItem("Tools/Setup/Combat Test")]
    static void Run()
    {
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        if (locomotion == null)
        {
            Debug.LogError("[CombatSetup] PlayerLocomotion이 없습니다. Tools/Setup/Player Locomotion을 먼저 실행하세요.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("[CombatSetup] Player.controller가 없습니다. Player Locomotion 설정을 먼저 실행하세요.");
            return;
        }

        GameObject player = locomotion.gameObject;
        Animator animator = player.GetComponentInChildren<Animator>();

        RemoveLegacyStates(controller);

        // 1단계: 노드마다 클립·상태·에셋 준비 / 2단계: 이름으로 다음 공격 연결
        var attacks = new Dictionary<string, AttackData>();
        foreach (ComboNode node in Nodes)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackClipRoot + node.ClipPath + ".anim");
            if (clip == null)
            {
                Debug.LogError($"[CombatSetup] {node.ClipPath} 클립을 찾지 못했습니다.");
                return;
            }

            DisableLoop(clip);
            EnsureState(controller, node.Name, clip);
            attacks[node.Name] = GetOrCreateAttackData(node, clip);
        }

        foreach (ComboNode node in Nodes)
            LinkNext(attacks[node.Name], Lookup(attacks, node.Light), Lookup(attacks, node.Heavy));

        AssetDatabase.SaveAssets();

        WeaponHitbox hitbox = GetOrCreateHitbox(player);
        if (hitbox == null) return;

        var combatSo = new SerializedObject(GetOrAdd<PlayerCombat>(player));
        combatSo.FindProperty("lightAttackAction").objectReferenceValue = FindAction("Attack");
        combatSo.FindProperty("heavyAttackAction").objectReferenceValue = FindAction("HeavyAttack");
        combatSo.FindProperty("lightStarter").objectReferenceValue = attacks[LightStarter];
        combatSo.FindProperty("heavyStarter").objectReferenceValue = attacks[HeavyStarter];
        combatSo.FindProperty("animator").objectReferenceValue = animator;
        combatSo.FindProperty("weapon").objectReferenceValue = hitbox;
        combatSo.FindProperty("locomotion").objectReferenceValue = locomotion;
        combatSo.ApplyModifiedProperties();

        EnsureDummy(player.transform);

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log($"[CombatSetup] 완료 · 공격 노드 {Nodes.Length}개, □=좌클릭/Square, △=우클릭/Triangle");
    }

    // 팩의 모든 클립이 Loop Time 켜짐 상태라 1회성 공격 클립만 끔
    static void DisableLoop(AnimationClip clip)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (!settings.loopTime) return;

        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }

    static void EnsureState(AnimatorController controller, string stateName, AnimationClip clip)
    {
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
        if (state == null) state = sm.AddState(stateName);

        state.motion = clip;
        EditorUtility.SetDirty(controller);
    }

    static void RemoveLegacyStates(AnimatorController controller)
    {
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in sm.states)
        {
            if (LegacyStates.Contains(child.state.name)) sm.RemoveState(child.state);
        }
    }

    // 클립·상태 이름은 매번 맞추고, 피해량과 타이밍은 처음 만들 때만 넣어 튜닝 값을 보존
    static AttackData GetOrCreateAttackData(ComboNode node, AnimationClip clip)
    {
        string path = $"{AttackFolder}/{node.Name}.asset";
        var data = AssetDatabase.LoadAssetAtPath<AttackData>(path);
        bool created = data == null;

        if (created)
        {
            if (!AssetDatabase.IsValidFolder(DataRoot)) AssetDatabase.CreateFolder("Assets", "11.Data");
            if (!AssetDatabase.IsValidFolder(AttackFolder)) AssetDatabase.CreateFolder(DataRoot, "Attack");

            data = ScriptableObject.CreateInstance<AttackData>();
            AssetDatabase.CreateAsset(data, path);
        }

        var so = new SerializedObject(data);
        so.FindProperty("clip").objectReferenceValue = clip;
        so.FindProperty("stateName").stringValue = node.Name;

        if (created)
        {
            so.FindProperty("damage").floatValue = node.Damage;
            so.FindProperty("postureDamage").floatValue = node.Damage * 0.5f;
            if (node.IsEnd) so.FindProperty("recoveryEnd").floatValue = 1f;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        return data;
    }

    static void LinkNext(AttackData data, AttackData nextLight, AttackData nextHeavy)
    {
        var so = new SerializedObject(data);
        so.FindProperty("nextLight").objectReferenceValue = nextLight;
        so.FindProperty("nextHeavy").objectReferenceValue = nextHeavy;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static AttackData Lookup(Dictionary<string, AttackData> attacks, string name) =>
        name != null && attacks.TryGetValue(name, out AttackData data) ? data : null;

    static WeaponHitbox GetOrCreateHitbox(GameObject player)
    {
        // 카타나 교체 등으로 히트박스가 sword 밖으로 옮겨졌어도 기존 것을 재사용
        WeaponHitbox existingAny = player.GetComponentInChildren<WeaponHitbox>(true);
        if (existingAny != null) return existingAny;

        Transform sword = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == SwordName);
        if (sword == null)
        {
            Debug.LogError($"[CombatSetup] '{SwordName}' 오브젝트를 찾지 못했습니다.");
            return null;
        }

        Transform existing = sword.Find(HitboxName);
        if (existing != null && existing.TryGetComponent(out WeaponHitbox found)) return found;

        var go = new GameObject(HitboxName);
        Undo.RegisterCreatedObjectUndo(go, "Create Hitbox");
        go.transform.SetParent(sword, false);

        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;

        // 칼 메시 범위에 맞추되, 얇은 날은 최소 두께를 보장
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
        so.FindProperty("owner").objectReferenceValue = player.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        return hitbox;
    }

    static void EnsureDummy(Transform player)
    {
        if (GameObject.Find(DummyName) != null) return;

        GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        dummy.name = DummyName;
        Undo.RegisterCreatedObjectUndo(dummy, "Create Training Dummy");

        Vector3 pos = player.position + player.forward * 2.5f;
        pos.y = player.position.y + 1f;
        dummy.transform.position = pos;
        dummy.transform.rotation = Quaternion.LookRotation(-player.forward, Vector3.up);

        dummy.AddComponent<TrainingDummy>();
    }

    static InputActionReference FindAction(string actionName)
    {
        var reference = AssetDatabase.LoadAllAssetsAtPath(InputPath)
            .OfType<InputActionReference>()
            .FirstOrDefault(r => r.action != null && r.action.actionMap.name == "Player" && r.action.name == actionName);

        if (reference == null) Debug.LogWarning($"[CombatSetup] Player/{actionName} 액션을 찾지 못했습니다.");
        return reference;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        return comp != null ? comp : Undo.AddComponent<T>(go);
    }
}
