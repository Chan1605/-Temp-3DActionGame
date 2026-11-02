using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class PlayerVfxSetup
{
    // [변경] HDR 프리팹 사용 → 밝기가 1을 넘어 블룸으로 빛남 (스타일 값은 PlayerVfx가 덮어씀)
    const string BoltPrefabPath = "Assets/ThirdParty/ProceduralLightning/Prefab/LightningBoltPrefabHDR.prefab";
    const string TrailMaterialPath = "Assets/6.Materials/SwordTrail.mat";
    const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";

    [MenuItem("Tools/Setup/Player Vfx")]
    static void Run()
    {
        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        if (combat == null)
        {
            Debug.LogError("[VfxSetup] 씬에 PlayerCombat이 없습니다.");
            return;
        }

        GameObject player = combat.gameObject;
        WeaponHitbox weapon = player.GetComponentInChildren<WeaponHitbox>(true);
        BoxCollider blade = weapon != null ? weapon.GetComponent<BoxCollider>() : null;
        if (blade == null)
        {
            Debug.LogError("[VfxSetup] Player의 WeaponHitbox(BoxCollider)를 찾지 못했습니다.");
            return;
        }

        var boltPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoltPrefabPath);
        if (boltPrefab == null)
            Debug.LogWarning("[VfxSetup] LightningBoltPrefab을 찾지 못해 번개 이펙트 없이 진행합니다. ThirdParty/ProceduralLightning 경로를 확인하세요.");

        Material trailMaterial = GetOrCreateTrailMaterial();

        SwordTrail trail = blade.GetComponent<SwordTrail>();
        if (trail == null) trail = Undo.AddComponent<SwordTrail>(blade.gameObject);
        var trailSo = new SerializedObject(trail);
        trailSo.FindProperty("blade").objectReferenceValue = blade;
        trailSo.FindProperty("handReference").objectReferenceValue = blade.transform.parent;
        trailSo.FindProperty("material").objectReferenceValue = trailMaterial;
        // [변경] 셋업을 다시 돌리면 궤적 모양도 새 기본값으로 맞춤
        trailSo.FindProperty("color").colorValue = new Color(0.35f, 0.65f, 1f, 0.9f);
        trailSo.FindProperty("lifetime").floatValue = 0.1f;
        trailSo.FindProperty("baseRatio").floatValue = 0.65f;
        trailSo.ApplyModifiedProperties();

        PlayerVfx vfx = player.GetComponent<PlayerVfx>();
        if (vfx == null) vfx = Undo.AddComponent<PlayerVfx>(player);
        var vfxSo = new SerializedObject(vfx);
        vfxSo.FindProperty("combat").objectReferenceValue = combat;
        vfxSo.FindProperty("skill").objectReferenceValue = player.GetComponent<PlayerSkill>();
        vfxSo.FindProperty("trail").objectReferenceValue = trail;
        vfxSo.FindProperty("blade").objectReferenceValue = blade;
        vfxSo.FindProperty("boltPrefab").objectReferenceValue = boltPrefab;
        vfxSo.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log("[VfxSetup] 완료 · 검 궤적, 강공격 검기, 벽력일섬 돌진 번개·낙뢰 연결");
    }

    // 정점 색(알파)을 쓰는 URP 파티클 셰이더 + 가산 블렌딩, 양면 렌더링 (궤적은 앞뒤 모두 보여야 함)
    // [변경] HDR 밝기(1 초과)로 블룸을 받게 함. 이미 있는 머티리얼도 매번 맞춤
    static readonly Color TrailHdrColor = new Color(2.5f, 2.5f, 2.5f, 1f);

    static Material GetOrCreateTrailMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(TrailMaterialPath);
        if (material != null)
        {
            material.SetColor("_BaseColor", TrailHdrColor);
            EditorUtility.SetDirty(material);
            return material;
        }

        Shader shader = Shader.Find(ParticleShader);
        if (shader == null)
        {
            Debug.LogError($"[VfxSetup] '{ParticleShader}' 셰이더를 찾지 못했습니다.");
            return null;
        }

        material = new Material(shader) { name = "SwordTrail" };
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 2f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetColor("_BaseColor", TrailHdrColor); // [변경]

        if (!AssetDatabase.IsValidFolder("Assets/6.Materials")) AssetDatabase.CreateFolder("Assets", "6.Materials");
        AssetDatabase.CreateAsset(material, TrailMaterialPath);
        return material;
    }
}
