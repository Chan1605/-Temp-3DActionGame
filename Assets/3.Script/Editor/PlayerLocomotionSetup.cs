using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

public static class PlayerLocomotionSetup
{
    const string AnimRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/";
    const string IdlePath = AnimRoot + "01_Idle/Idle_Combat.anim";
    const string WalkPath = AnimRoot + "03_Walk/02_Walk_Combat_RM/01_Walk_Combat_F_0_RM/Walk_Combat_Loop_F_0_RM.anim";
    const string RunPath = AnimRoot + "04_Run/02_Run_Combat_RM/01_Run_Combat_F_0_RM/Run_Combat_Loop_F_0_RM.anim";
    const string SprintPath = AnimRoot + "04_Run/02_Run_Combat_RM/11_Run_Fast_Combat_RM/Run_Fast_Combat_Loop_RM.anim";

    const string BlendParam = "MoveBlend";
    const string AnimSpeedParam = "MoveAnimSpeed";
    const string LegacyParam = "Speed";
    const string LocomotionState = "Locomotion";

    const string ModelPath = "Assets/ThirdParty/SwordAnimationPack/Model/9CG_Sword.FBX";
    const string InputPath = "Assets/10.Input/InputSystem_Actions.inputactions";
    const string ControllerFolder = "Assets/5.Animation/Player";
    const string ControllerPath = ControllerFolder + "/Player.controller";

    const string ModelName = "9CG_Sword";
    const string PlayerName = "Player";

    [MenuItem("Tools/Setup/Player Locomotion")]
    static void Run()
    {
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
        var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkPath);
        var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunPath);
        var sprint = AssetDatabase.LoadAssetAtPath<AnimationClip>(SprintPath);
        if (idle == null || walk == null || run == null || sprint == null)
        {
            Debug.LogError("[PlayerSetup] 로코모션 클립을 찾지 못했습니다. 경로를 확인하세요.");
            return;
        }

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isHuman)
        {
            Debug.LogError("[PlayerSetup] 9CG_Sword.FBX의 Humanoid Avatar가 없습니다. Rig를 Humanoid로 설정하세요.");
            return;
        }

        GameObject model = GameObject.Find(ModelName);
        if (model == null)
        {
            Debug.LogError($"[PlayerSetup] 씬에서 '{ModelName}' 오브젝트를 찾지 못했습니다.");
            return;
        }

        // 클립 원래 이동 속도는 런타임 재생 배속 계산에 사용
        float walkClipSpeed = PlanarSpeed(walk, 1.6f);
        float runClipSpeed = PlanarSpeed(run, 4.5f);
        float sprintClipSpeed = PlanarSpeed(sprint, 7f);

        AnimatorController controller = GetOrCreateController();
        RebuildLocomotion(controller, idle, walk, run, sprint);
        GameObject player = GetOrCreatePlayerRoot(model);

        Animator animator = GetOrAdd<Animator>(model);
        Undo.RecordObject(animator, "Setup Animator");
        animator.runtimeAnimatorController = controller;
        animator.avatar = avatar;
        animator.applyRootMotion = false;

        CharacterController cc = GetOrAdd<CharacterController>(player);
        Undo.RecordObject(cc, "Setup CharacterController");
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        Camera cam = Camera.main;

        var loco = new SerializedObject(GetOrAdd<PlayerLocomotion>(player));
        loco.FindProperty("moveAction").objectReferenceValue = FindAction("Move");
        loco.FindProperty("sprintAction").objectReferenceValue = FindAction("Sprint");
        loco.FindProperty("animator").objectReferenceValue = animator;
        loco.FindProperty("cameraTransform").objectReferenceValue = cam != null ? cam.transform : null;
        SetFloat(loco, "walkClipSpeed", walkClipSpeed);
        SetFloat(loco, "runClipSpeed", runClipSpeed);
        SetFloat(loco, "sprintClipSpeed", sprintClipSpeed);
        loco.ApplyModifiedProperties();

        if (cam != null)
        {
            var camSo = new SerializedObject(GetOrAdd<ThirdPersonCamera>(cam.gameObject));
            camSo.FindProperty("target").objectReferenceValue = player.transform;
            camSo.FindProperty("lookAction").objectReferenceValue = FindAction("Look");
            camSo.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning("[PlayerSetup] MainCamera 태그가 붙은 카메라가 없어 카메라 연결을 건너뜁니다.");
        }

        EnsureGround();

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log($"[PlayerSetup] 완료 · 클립 속도 Walk {walkClipSpeed:F2} / Run {runClipSpeed:F2} / Sprint {sprintClipSpeed:F2} m/s");
    }

    static AnimatorController GetOrCreateController()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(ControllerFolder))
            AssetDatabase.CreateFolder("Assets/5.Animation", "Player");

        return AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
    }

    // Locomotion 상태의 블렌드 트리만 교체하고 공격 상태 등 나머지는 그대로 둔다
    static void RebuildLocomotion(AnimatorController controller, AnimationClip idle, AnimationClip walk,
        AnimationClip run, AnimationClip sprint)
    {
        RemoveParameter(controller, LegacyParam);
        EnsureParameter(controller, BlendParam, 0f);
        EnsureParameter(controller, AnimSpeedParam, 1f);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == LocomotionState);
        if (state == null) state = sm.AddState(LocomotionState);

        if (state.motion is BlendTree oldTree && AssetDatabase.GetAssetPath(oldTree) == ControllerPath)
            AssetDatabase.RemoveObjectFromAsset(oldTree);

        var tree = new BlendTree
        {
            name = LocomotionState,
            blendType = BlendTreeType.Simple1D,
            blendParameter = BlendParam,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);

        tree.AddChild(idle, 0f);
        tree.AddChild(walk, 1f);
        tree.AddChild(run, 2f);
        tree.AddChild(sprint, 3f);

        state.motion = tree;
        state.speedParameter = AnimSpeedParam;
        state.speedParameterActive = true;
        sm.defaultState = state;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    static void EnsureParameter(AnimatorController controller, string name, float defaultValue)
    {
        if (controller.parameters.Any(p => p.name == name)) return;

        controller.AddParameter(new AnimatorControllerParameter
        {
            name = name,
            type = AnimatorControllerParameterType.Float,
            defaultFloat = defaultValue
        });
    }

    static void RemoveParameter(AnimatorController controller, string name)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name != name) continue;
            controller.RemoveParameter(i);
            return;
        }
    }

    static GameObject GetOrCreatePlayerRoot(GameObject model)
    {
        Transform parent = model.transform.parent;
        if (parent != null && parent.name == PlayerName) return parent.gameObject;

        var player = new GameObject(PlayerName);
        Undo.RegisterCreatedObjectUndo(player, "Create Player Root");
        player.transform.SetPositionAndRotation(model.transform.position, model.transform.rotation);
        player.tag = "Player";

        Undo.SetTransformParent(model.transform, player.transform, "Parent Model To Player");
        Undo.RecordObject(model.transform, "Reset Model Transform");
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        return player;
    }

    static void EnsureGround()
    {
        if (GameObject.Find("Ground") != null) return;

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
        Undo.RegisterCreatedObjectUndo(ground, "Create Ground");
    }

    static InputActionReference FindAction(string actionName)
    {
        var reference = AssetDatabase.LoadAllAssetsAtPath(InputPath)
            .OfType<InputActionReference>()
            .FirstOrDefault(r => r.action != null && r.action.actionMap.name == "Player" && r.action.name == actionName);

        if (reference == null) Debug.LogWarning($"[PlayerSetup] Player/{actionName} 액션을 찾지 못했습니다.");
        return reference;
    }

    // 필드 이름이 바뀌어도 NRE 대신 경고만 남김
    static void SetFloat(SerializedObject so, string propertyName, float value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogWarning($"[PlayerSetup] {so.targetObject.GetType().Name}에 '{propertyName}' 필드가 없습니다. 스크립트가 최신인지 확인하세요.");
            return;
        }
        prop.floatValue = value;
    }

    static float PlanarSpeed(AnimationClip clip, float fallback)
    {
        Vector3 v = clip.averageSpeed;
        float speed = new Vector2(v.x, v.z).magnitude;
        return speed > 0.1f ? speed : fallback;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        return comp != null ? comp : Undo.AddComponent<T>(go);
    }
}
