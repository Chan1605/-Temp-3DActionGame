using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class JustDodgeParrySetup
{
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string CounterClipPath = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/02_Attack/13_Parry_Counter_Attack/Parry_Counter_Attack.anim";
    const string CounterState = "Parry_Counter";
    const string MaterialPath = "Assets/6.Materials/Afterimage.mat";
    const string UnlitShader = "Universal Render Pipeline/Unlit";

    [MenuItem("Tools/Setup/Just Dodge and Parry")]
    static void Run()
    {
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (locomotion == null || controller == null)
        {
            Debug.LogError("[JustDodgeParrySetup] Player 또는 Player.controller가 없습니다.");
            return;
        }

        GameObject player = locomotion.gameObject;
        if (player.GetComponent<PlayerDodge>() == null)
        {
            Debug.LogError("[JustDodgeParrySetup] PlayerDodge가 없습니다. Tools/Setup/Player Dodge를 먼저 실행하세요.");
            return;
        }

        if (!EnsureCounterState(controller)) return;
        Material material = GetOrCreateAfterimageMaterial();

        PlayerAfterimage afterimage = GetOrAdd<PlayerAfterimage>(player);
        PlayerParry parry = GetOrAdd<PlayerParry>(player);
        var cam = Object.FindAnyObjectByType<ThirdPersonCamera>();

        var afterSo = new SerializedObject(afterimage);
        afterSo.FindProperty("material").objectReferenceValue = material;
        afterSo.FindProperty("sourceRoot").objectReferenceValue = player.GetComponentInChildren<Animator>().transform;
        afterSo.ApplyModifiedProperties();

        var parrySo = new SerializedObject(parry);
        parrySo.FindProperty("animator").objectReferenceValue = player.GetComponentInChildren<Animator>();
        parrySo.FindProperty("locomotion").objectReferenceValue = locomotion;
        parrySo.FindProperty("combat").objectReferenceValue = player.GetComponent<PlayerCombat>();
        parrySo.FindProperty("skill").objectReferenceValue = player.GetComponent<PlayerSkill>();
        parrySo.FindProperty("health").objectReferenceValue = player.GetComponent<PlayerHealth>();
        parrySo.FindProperty("dodge").objectReferenceValue = player.GetComponent<PlayerDodge>();
        parrySo.FindProperty("afterimage").objectReferenceValue = afterimage;
        parrySo.FindProperty("followCamera").objectReferenceValue = cam;
        parrySo.ApplyModifiedProperties();

        SetRefs(player.GetComponent<PlayerDodge>(), ("parry", parry), ("afterimage", afterimage), ("followCamera", cam));
        SetRefs(player.GetComponent<PlayerHealth>(), ("parry", parry));
        SetRefs(player.GetComponent<PlayerCombat>(), ("parry", parry));

        PlayerClipFixer.Fix(ControllerPath);

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log("[JustDodgeParrySetup] 완료 · 저스트 회피(적 슬로 + 잔상), 간파(우클릭) 연결");
    }

    static bool EnsureCounterState(AnimatorController controller)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CounterClipPath);
        if (clip == null)
        {
            Debug.LogError($"[JustDodgeParrySetup] {CounterClipPath} 클립을 찾지 못했습니다.");
            return false;
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime)
        {
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == CounterState);
        if (state == null) state = sm.AddState(CounterState);
        state.motion = clip;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return true;
    }

    // URP Unlit을 가산(Additive) 투명으로 설정 → 겹칠수록 밝아지는 번개 느낌의 잔상
    static Material GetOrCreateAfterimageMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null) return material;

        Shader shader = Shader.Find(UnlitShader);
        if (shader == null)
        {
            Debug.LogError($"[JustDodgeParrySetup] '{UnlitShader}' 셰이더를 찾지 못했습니다.");
            return null;
        }

        material = new Material(shader) { name = "Afterimage" };
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 2f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Back);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetColor("_BaseColor", new Color(0.45f, 0.75f, 1f, 0.6f));

        if (!AssetDatabase.IsValidFolder("Assets/6.Materials")) AssetDatabase.CreateFolder("Assets", "6.Materials");
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    static void SetRefs(Object target, params (string field, Object value)[] refs)
    {
        if (target == null) return;

        var so = new SerializedObject(target);
        foreach ((string field, Object value) in refs)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null) prop.objectReferenceValue = value;
        }
        so.ApplyModifiedProperties();
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        return comp != null ? comp : Undo.AddComponent<T>(go);
    }
}
