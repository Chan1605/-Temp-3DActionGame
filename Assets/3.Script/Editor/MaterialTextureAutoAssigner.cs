using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MaterialTextureAutoAssigner
{
    const string LitShaderName = "Universal Render Pipeline/Lit";
    const string GeneratedFolderName = "Generated";

    enum MapType { None, Base, Normal, Metallic, Roughness, Opacity, Emission }

    [MenuItem("Tools/Material/Auto Assign Textures (URP Lit)")]
    static void Run()
    {
        Material[] materials = Selection.GetFiltered<Material>(SelectionMode.DeepAssets);
        if (materials.Length == 0)
        {
            Debug.LogWarning("[AutoAssign] 머티리얼 또는 머티리얼이 들어있는 폴더를 선택하세요.");
            return;
        }

        string absFolder = EditorUtility.OpenFolderPanel("텍스처 폴더 선택", Application.dataPath, "");
        if (string.IsNullOrEmpty(absFolder)) return;
        if (!absFolder.StartsWith(Application.dataPath))
        {
            Debug.LogWarning("[AutoAssign] 프로젝트 Assets 안의 폴더를 선택하세요.");
            return;
        }
        string texFolder = "Assets" + absFolder.Substring(Application.dataPath.Length);

        Shader lit = Shader.Find(LitShaderName);
        if (lit == null)
        {
            Debug.LogError("[AutoAssign] URP Lit 셰이더를 찾을 수 없습니다.");
            return;
        }

        string[] texturePaths = AssetDatabase.FindAssets("t:Texture2D", new[] { texFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !p.Contains("/" + GeneratedFolderName + "/"))
            .ToArray();

        string generatedFolder = texFolder + "/" + GeneratedFolderName;
        if (!AssetDatabase.IsValidFolder(generatedFolder))
            AssetDatabase.CreateFolder(texFolder, GeneratedFolderName);

        int done = 0;
        foreach (Material mat in materials)
        {
            Dictionary<MapType, string> maps = FindMaps(mat.name, texturePaths);
            if (maps.Count == 0)
            {
                Debug.LogWarning($"[AutoAssign] {mat.name}: 매칭되는 텍스처 없음");
                continue;
            }

            Undo.RecordObject(mat, "Auto Assign Textures");
            mat.shader = lit;
            mat.SetColor("_BaseColor", Color.white);

            ApplyBase(mat, maps, generatedFolder);
            ApplyNormal(mat, maps);
            ApplyMetallicSmoothness(mat, maps, generatedFolder);
            ApplyEmission(mat, maps);

            EditorUtility.SetDirty(mat);
            done++;
            Debug.Log($"[AutoAssign] {mat.name}: {string.Join(", ", maps.Keys)}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[AutoAssign] 완료: {done}/{materials.Length}개 머티리얼");
    }

    // 머티리얼 이름(끝 숫자 제거)이 "_맵종류" 앞에 포함된 텍스처를 찾는다.
    // 예: Medias_mat1 → Medias_low_Medias_mat_BaseColor
    static Dictionary<MapType, string> FindMaps(string materialName, string[] texturePaths)
    {
        string key = materialName.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').ToLowerInvariant();
        var result = new Dictionary<MapType, string>();

        foreach (string path in texturePaths)
        {
            string file = Path.GetFileName(path).ToLowerInvariant();
            int idx = file.IndexOf(key + "_");
            if (idx < 0) continue;

            string suffix = file.Substring(idx + key.Length + 1);
            MapType type = Classify(suffix);
            if (type != MapType.None && !result.ContainsKey(type))
                result.Add(type, path);
        }
        return result;
    }

    static MapType Classify(string suffix)
    {
        if (suffix.StartsWith("normal")) return MapType.Normal;
        if (suffix.StartsWith("baseco") || suffix.StartsWith("albedo") || suffix.StartsWith("diffuse")) return MapType.Base;
        if (suffix.StartsWith("metal")) return MapType.Metallic;
        if (suffix.StartsWith("rough")) return MapType.Roughness;
        if (suffix.StartsWith("opacity") || suffix.StartsWith("alpha")) return MapType.Opacity;
        if (suffix.StartsWith("emiss")) return MapType.Emission;
        return MapType.None;
    }

    static void ApplyBase(Material mat, Dictionary<MapType, string> maps, string outFolder)
    {
        if (!maps.TryGetValue(MapType.Base, out string basePath)) return;

        if (!maps.TryGetValue(MapType.Opacity, out string opacityPath))
        {
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(basePath));
            return;
        }

        // Opacity를 BaseColor 알파에 합치고 Alpha Clipping 사용
        string outPath = Pack(outFolder, mat.name + "_BaseAlpha", true, basePath, opacityPath,
            (b, o) => new Color(b.r, b.g, b.b, o.r));
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(outPath));
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
    }

    static void ApplyNormal(Material mat, Dictionary<MapType, string> maps)
    {
        if (!maps.TryGetValue(MapType.Normal, out string normalPath)) return;

        var importer = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        if (importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        mat.EnableKeyword("_NORMALMAP");
    }

    // URP Lit 마스크 형식: R = Metallic, A = Smoothness(1 - Roughness)
    static void ApplyMetallicSmoothness(Material mat, Dictionary<MapType, string> maps, string outFolder)
    {
        bool hasMetal = maps.TryGetValue(MapType.Metallic, out string metalPath);
        bool hasRough = maps.TryGetValue(MapType.Roughness, out string roughPath);
        if (!hasMetal && !hasRough) return;

        string outPath = Pack(outFolder, mat.name + "_MetallicSmoothness", false,
            hasMetal ? metalPath : roughPath,
            hasRough ? roughPath : null,
            (m, r) =>
            {
                float metallic = hasMetal ? m.r : 0f;
                float smoothness = hasRough ? 1f - r.r : 0.5f;
                return new Color(metallic, 0f, 0f, smoothness);
            });

        mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(outPath));
        mat.SetFloat("_Smoothness", 1f);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
    }

    static void ApplyEmission(Material mat, Dictionary<MapType, string> maps)
    {
        if (!maps.TryGetValue(MapType.Emission, out string emissionPath)) return;

        mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath));
        mat.SetColor("_EmissionColor", Color.white);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
    }

    // 두 텍스처를 픽셀 단위로 합쳐 PNG로 저장. 크기는 첫 번째 텍스처 기준, 두 번째는 UV 샘플링으로 맞춘다.
    static string Pack(string folder, string name, bool sRGB, string pathA, string pathB,
        System.Func<Color, Color, Color> combine)
    {
        Texture2D a = LoadRaw(pathA);
        Texture2D b = pathB != null ? LoadRaw(pathB) : null;
        int w = a.width, h = a.height;

        var output = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color[w * h];
        Color[] aPixels = a.GetPixels();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                Color bColor = b == null ? Color.black
                    : (b.width == w && b.height == h) ? b.GetPixel(x, y)
                    : b.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
                pixels[i] = combine(aPixels[i], bColor);
            }
        }

        output.SetPixels(pixels);
        string outPath = $"{folder}/{name}.png";
        File.WriteAllBytes(outPath, output.EncodeToPNG());

        Object.DestroyImmediate(a);
        if (b != null) Object.DestroyImmediate(b);
        Object.DestroyImmediate(output);

        AssetDatabase.ImportAsset(outPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(outPath);
        importer.sRGBTexture = sRGB;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = sRGB;
        importer.SaveAndReimport();
        return outPath;
    }

    // 임포트 설정(Read/Write)과 무관하게 원본 파일 바이트로 읽는다. PNG/JPG만 지원.
    static Texture2D LoadRaw(string assetPath)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!tex.LoadImage(File.ReadAllBytes(assetPath)))
            Debug.LogError($"[AutoAssign] 읽기 실패 (PNG/JPG만 지원): {assetPath}");
        return tex;
    }
}
