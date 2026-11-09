using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 플레이어 스킨을 Samurai 팩(MoonCat)으로 교체. 플레이 모드가 아닐 때 한 번 실행
//  1. 아바타: Hips가 root에 잘못 연결돼 있어 pelvis로 고침 (그대로면 골반이 바닥 기준이라 몸이 뜨거나 꺾임)
//  2. 텍스처 4K → 2048 제한
//  3. 머티리얼: Built-in(Standard·팩 전용 양면) → URP Lit 사본을 6.Materials/Samurai에 만듦 (원본 팩은 그대로)
//  4. Player 프리팹: Animator가 달린 모델 오브젝트는 그대로 두고 뼈대·메시만 교체
//     → 컴포넌트들의 animator 참조가 끊기지 않음. 칼(weapon_r 아래 판정·카타나)은 새 hand_r로 옮김
//  5. 활·팩 기본 칼은 숨김 (판정이 달린 기존 카타나 사용), 칼집은 허리에 그대로
// 이미 적용된 프리팹이면 머티리얼·숨김만 다시 맞춤
public static class SamuraiSkinSetup
{
    const string PackRoot = "Assets/ThirdParty/Samurai";
    const string FbxPath = PackRoot + "/Mesh/SKM_Samurai.fbx";
    const string VendorPrefabPath = PackRoot + "/Prefabs/SKM_Samurai.prefab";
    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";
    const string MaterialParent = "Assets/6.Materials";
    const string MaterialFolder = MaterialParent + "/Samurai";
    const string LitShader = "Universal Render Pipeline/Lit";
    const string ModelName = "SamuraiModel";
    const int MaxTextureSize = 2048;

    static readonly string[] HiddenParts = { "bow", "katana" };
    static readonly string[] MovedBones = { "weapon_r", "sword_holder" }; // 기존 뼈대에 붙여 둔 것 (칼 판정·카타나, 칼집 자리)

    [MenuItem("Tools/Setup/Samurai Player Skin")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[SamuraiSkin] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        Avatar avatar = FixAvatar();
        if (avatar == null) return;

        LimitTextures();
        Dictionary<Material, Material> materials = ConvertMaterials();
        if (materials == null) return;

        SwapPlayerModel(avatar, materials);
    }

    // ===== 1. 아바타 =====
    static Avatar FixAvatar()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[SamuraiSkin] {FbxPath} 를 찾지 못했습니다.");
            return null;
        }

        bool changed = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            changed = true;
        }

        HumanDescription description = importer.humanDescription;
        HumanBone[] bones = description.human;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].humanName != "Hips" || bones[i].boneName == "pelvis") continue;
            bones[i].boneName = "pelvis"; // 핵심: 언리얼 마네킹 뼈대는 root(바닥) 아래 pelvis가 골반
            changed = true;
        }

        if (changed)
        {
            description.human = bones;
            importer.humanDescription = description;
            importer.SaveAndReimport();
        }

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError("[SamuraiSkin] Samurai 아바타가 Humanoid로 만들어지지 않았습니다. FBX의 Rig 탭을 확인하세요.");
            return null;
        }
        return avatar;
    }

    // ===== 2. 텍스처 =====
    static void LimitTextures()
    {
        int count = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PackRoot + "/Textures" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

                bool changed = false;
                if (importer.maxTextureSize > MaxTextureSize)
                {
                    importer.maxTextureSize = MaxTextureSize;
                    changed = true;
                }
                TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
                if (standalone.overridden && standalone.maxTextureSize > MaxTextureSize)
                {
                    standalone.maxTextureSize = MaxTextureSize;
                    importer.SetPlatformTextureSettings(standalone);
                    changed = true;
                }
                if (!changed) continue;
                importer.SaveAndReimport();
                count++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        if (count > 0) Debug.Log($"[SamuraiSkin] 텍스처 {count}장을 {MaxTextureSize}로 제한");
    }

    // ===== 3. 머티리얼 =====
    static Dictionary<Material, Material> ConvertMaterials()
    {
        var vendor = AssetDatabase.LoadAssetAtPath<GameObject>(VendorPrefabPath);
        Shader lit = Shader.Find(LitShader);
        if (vendor == null || lit == null)
        {
            Debug.LogError("[SamuraiSkin] SKM_Samurai 프리팹 또는 URP Lit 셰이더를 찾지 못했습니다.");
            return null;
        }
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder(MaterialParent, "Samurai");

        var map = new Dictionary<Material, Material>();
        foreach (Renderer r in vendor.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material src in r.sharedMaterials)
            {
                if (src == null || map.ContainsKey(src)) continue;

                map[src] = GetOrCreateLit(src, lit);
            }
        }
        AssetDatabase.SaveAssets();
        return map;
    }

    // [변경] 칼 교체(SamuraiKatanaSetup)에서도 같은 변환을 쓰도록 분리. 이미 만든 사본이 있으면 그대로 씀 (사용자가 조정한 값 보존)
    internal static Material GetOrCreateLit(Material src, Shader lit = null)
    {
        if (lit == null) lit = Shader.Find(LitShader);
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder(MaterialParent, "Samurai");
        string path = $"{MaterialFolder}/{src.name}.mat";
        Material converted = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (converted != null) return converted;
        converted = ToLit(src, lit);
        AssetDatabase.CreateAsset(converted, path);
        return converted;
    }

    static Material ToLit(Material src, Shader lit)
    {
        var m = new Material(lit) { name = src.name };

        Texture main = Tex(src, "_MainTex");
        if (main != null)
        {
            m.SetTexture("_BaseMap", main);
            m.SetTexture("_MainTex", main);
        }
        if (src.HasProperty("_Color"))
        {
            m.SetColor("_BaseColor", src.GetColor("_Color"));
            m.SetColor("_Color", src.GetColor("_Color"));
        }

        Texture normal = Tex(src, "_BumpMap");
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", Float(src, "_BumpScale", 1f));
            m.EnableKeyword("_NORMALMAP");
        }

        Texture occlusion = Tex(src, "_OcclusionMap");
        if (occlusion != null)
        {
            m.SetTexture("_OcclusionMap", occlusion);
            m.SetFloat("_OcclusionStrength", Float(src, "_OcclusionStrength", 1f));
            m.EnableKeyword("_OCCLUSIONMAP");
        }

        Texture metallic = Tex(src, "_MetallicGlossMap");
        if (metallic != null)
        {
            m.SetTexture("_MetallicGlossMap", metallic);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        m.SetFloat("_Metallic", Float(src, "_Metallic", 0f));
        m.SetFloat("_Smoothness", Float(src, "_Glossiness", 0.3f));

        // Standard의 _Mode 1 = Cutout (머리카락) → URP 알파 클립
        if (Mathf.RoundToInt(Float(src, "_Mode", 0f)) == 1)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", Float(src, "_Cutoff", 0.5f));
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }

        // 팩 전용 양면 셰이더(머리카락·옷) → 양면 렌더링
        string shaderPath = src.shader != null ? AssetDatabase.GetAssetPath(src.shader) : "";
        if (shaderPath.Contains("Two_Sided") || (src.shader != null && src.shader.name.Contains("Two")))
        {
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.doubleSidedGI = true;
        }
        return m;
    }

    static Texture Tex(Material m, string name) => m.HasProperty(name) ? m.GetTexture(name) : null;
    static float Float(Material m, string name, float fallback) => m.HasProperty(name) ? m.GetFloat(name) : fallback;

    // ===== 4. 모델 교체 =====
    static void SwapPlayerModel(Avatar avatar, Dictionary<Material, Material> materials)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogError("[SamuraiSkin] Player 프리팹에서 Animator를 찾지 못했습니다.");
                return;
            }
            Transform model = animator.transform;

            bool alreadySwapped = animator.avatar == avatar;
            if (!alreadySwapped && !ReplaceSkeleton(root, model)) return;

            animator.avatar = avatar;
            model.name = ModelName;

            int hidden = 0;
            foreach (string part in HiddenParts)
            {
                Transform t = FindDeep(model, part);
                if (t == null || t.GetComponent<SkinnedMeshRenderer>() == null) continue;
                t.gameObject.SetActive(false);
                hidden++;
            }

            int swapped = 0;
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material[] mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !materials.TryGetValue(mats[i], out Material converted)) continue;
                    mats[i] = converted;
                    swapped++;
                }
                smr.sharedMaterials = mats;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log($"[SamuraiSkin] 완료 · {(alreadySwapped ? "이미 교체된 모델 갱신" : "뼈대·메시 교체")}, 머티리얼 {swapped}칸 URP로, 숨긴 파츠 {hidden}개");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 핵심: 새 뼈대·메시를 기존 모델 오브젝트 아래로 옮기고, 기존 뼈대에 붙어 있던 칼·칼집 자리는 같은 이름의 새 뼈로 옮긴 뒤 옛 것을 지움
    static bool ReplaceSkeleton(GameObject root, Transform model)
    {
        Transform oldSkeleton = model.Find("root");
        var vendor = AssetDatabase.LoadAssetAtPath<GameObject>(VendorPrefabPath);
        if (oldSkeleton == null || vendor == null)
        {
            Debug.LogError("[SamuraiSkin] 기존 뼈대(root) 또는 SKM_Samurai 프리팹을 찾지 못했습니다.");
            return false;
        }

        var temp = (GameObject)PrefabUtility.InstantiatePrefab(vendor, root.scene);
        PrefabUtility.UnpackPrefabInstance(temp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Transform newSkeleton = temp.transform.Find("root");
        if (newSkeleton == null)
        {
            Object.DestroyImmediate(temp);
            Debug.LogError("[SamuraiSkin] Samurai 모델에서 root 뼈를 찾지 못했습니다.");
            return false;
        }

        // 언리얼 마네킹 계열이라 뼈 이름·축이 같음 → 로컬 위치·회전을 그대로 옮기면 손에 쥔 자리가 유지됨
        foreach (string boneName in MovedBones)
        {
            Transform moved = FindDeep(oldSkeleton, boneName);
            if (moved == null) continue;
            Transform newParent = FindDeep(newSkeleton, moved.parent.name);
            if (newParent == null)
            {
                Debug.LogWarning($"[SamuraiSkin] '{moved.parent.name}' 뼈가 새 모델에 없어 {boneName}을 옮기지 못했습니다.");
                continue;
            }
            Vector3 position = moved.localPosition;
            Quaternion rotation = moved.localRotation;
            Vector3 scale = moved.localScale;
            moved.SetParent(newParent, false);
            moved.localPosition = position;
            moved.localRotation = rotation;
            moved.localScale = scale;
        }

        List<Transform> oldChildren = model.Cast<Transform>().ToList();
        foreach (Transform child in temp.transform.Cast<Transform>().ToList()) child.SetParent(model, false);
        foreach (Transform old in oldChildren) Object.DestroyImmediate(old.gameObject);
        Object.DestroyImmediate(temp);
        return true;
    }

    internal static Transform FindDeep(Transform parent, string name) // [변경] 칼 교체에서도 사용
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
