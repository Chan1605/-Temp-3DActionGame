using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

public static class ThunderSkillSetup
{
    const string ClipRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/02_Attack/";
    const string StanceClip = "16_Power_Attack/Power_Attack_Start";
    const string ChargeClip = "16_Power_Attack/Power_Attack_Loop";
    const string ReleaseClip = "15_Execution/01_Execution/Execution_01";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string InputPath = "Assets/10.Input/InputSystem_Actions.inputactions";
    const string DataFolder = "Assets/11.Data/Skill";
    const string DataPath = DataFolder + "/ThunderFlash.asset";
    const string TrailMaterialPath = "Assets/6.Materials/SkillTrail.mat";

    const string TrailName = "SkillTrail";
    const string DummyBaseName = "TrainingDummy";

    [MenuItem("Tools/Setup/Thunder Skill")]
    static void Run()
    {
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (locomotion == null || controller == null)
        {
            Debug.LogError("[SkillSetup] Player 또는 Player.controller가 없습니다. Player Locomotion / Combat Test 셋업을 먼저 실행하세요.");
            return;
        }

        var stance = LoadClip(StanceClip);
        var charge = LoadClip(ChargeClip);
        var release = LoadClip(ReleaseClip);
        if (stance == null || charge == null || release == null) return;

        SetLoop(stance, false);
        SetLoop(charge, true);
        SetLoop(release, false);

        SkillData data = GetOrCreateData();
        EnsureState(controller, data.StanceState, stance);
        EnsureState(controller, data.ChargeState, charge);
        EnsureState(controller, data.ReleaseState, release);
        AssetDatabase.SaveAssets();

        GameObject player = locomotion.gameObject;
        TrailRenderer trail = GetOrCreateTrail(player.transform);

        var so = new SerializedObject(GetOrAdd<PlayerSkill>(player));
        so.FindProperty("skillAction").objectReferenceValue = FindAction("Skill");
        so.FindProperty("data").objectReferenceValue = data;
        so.FindProperty("animator").objectReferenceValue = player.GetComponentInChildren<Animator>();
        so.FindProperty("locomotion").objectReferenceValue = locomotion;
        so.FindProperty("combat").objectReferenceValue = player.GetComponent<PlayerCombat>();
        so.FindProperty("followCamera").objectReferenceValue = Object.FindAnyObjectByType<ThirdPersonCamera>();
        so.FindProperty("dashTrail").objectReferenceValue = trail;
        so.ApplyModifiedProperties();

        EnsureChainDummies(player.transform);

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log("[SkillSetup] 완료 · Q 짧게 = 즉발, 길게 = 차지, 착지 후 Q = 연쇄");
    }

    static AnimationClip LoadClip(string name)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipRoot}{name}.anim");
        if (clip == null) Debug.LogError($"[SkillSetup] {name} 클립을 찾지 못했습니다.");
        return clip;
    }

    static void SetLoop(AnimationClip clip, bool loop)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime == loop) return;

        settings.loopTime = loop;
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

    static SkillData GetOrCreateData()
    {
        var data = AssetDatabase.LoadAssetAtPath<SkillData>(DataPath);
        if (data != null) return data;

        if (!AssetDatabase.IsValidFolder("Assets/11.Data")) AssetDatabase.CreateFolder("Assets", "11.Data");
        if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/11.Data", "Skill");

        data = ScriptableObject.CreateInstance<SkillData>();
        AssetDatabase.CreateAsset(data, DataPath);
        return data;
    }

    static TrailRenderer GetOrCreateTrail(Transform player)
    {
        Transform existing = player.Find(TrailName);
        if (existing != null && existing.TryGetComponent(out TrailRenderer found)) return found;

        var go = new GameObject(TrailName);
        Undo.RegisterCreatedObjectUndo(go, "Create Skill Trail");
        go.transform.SetParent(player, false);
        go.transform.localPosition = new Vector3(0f, 1f, 0f);

        var trail = go.AddComponent<TrailRenderer>();
        trail.emitting = false;
        trail.time = 0.35f;
        trail.minVertexDistance = 0.05f;
        trail.widthMultiplier = 0.6f;
        trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.colorGradient = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.75f, 0.1f), 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        };
        trail.sharedMaterial = GetOrCreateTrailMaterial();
        return trail;
    }

    // Sprites/Default는 버텍스 컬러와 알파를 그대로 써서 트레일 그라디언트가 반영됨
    static Material GetOrCreateTrailMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(TrailMaterialPath);
        if (mat != null) return mat;

        mat = new Material(Shader.Find("Sprites/Default"));
        AssetDatabase.CreateAsset(mat, TrailMaterialPath);
        return mat;
    }

    // 연쇄 테스트용 허수아비를 플레이어 정면에 부채꼴로 추가 배치
    static void EnsureChainDummies(Transform player)
    {
        Vector3 f = player.forward;
        Vector3 r = player.right;
        Vector3[] offsets = { f * 6f + r * 4f, f * 6f - r * 4f, f * 10f };

        for (int i = 0; i < offsets.Length; i++)
        {
            string name = $"{DummyBaseName}_{i + 2}";
            if (GameObject.Find(name) != null) continue;

            GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummy.name = name;
            Undo.RegisterCreatedObjectUndo(dummy, "Create Chain Dummy");

            Vector3 pos = player.position + offsets[i];
            pos.y = player.position.y + 1f;
            dummy.transform.position = pos;
            dummy.transform.rotation = Quaternion.LookRotation(-f, Vector3.up);
            dummy.AddComponent<TrainingDummy>();
        }
    }

    static InputActionReference FindAction(string actionName)
    {
        var reference = AssetDatabase.LoadAllAssetsAtPath(InputPath)
            .OfType<InputActionReference>()
            .FirstOrDefault(r => r.action != null && r.action.actionMap.name == "Player" && r.action.name == actionName);

        if (reference == null) Debug.LogWarning($"[SkillSetup] Player/{actionName} 액션을 찾지 못했습니다.");
        return reference;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        return comp != null ? comp : Undo.AddComponent<T>(go);
    }
}
