using System.Linq;
using UnityEditor;
using UnityEngine;

// 손에 든 칼 겉모습 바꾸기: Samurai 팩 카타나 ↔ 원래 ZenitsuKatana (메뉴 두 개로 언제든 되돌림)
//  - 판정 박스(Hitbox)·궤적·번개 연출은 건드리지 않음 → 사거리·판정 구간 그대로
//  - 팩 카타나는 날밑(코등이) 위치와 칼날 방향·길이를 원래 칼에 맞춰 둠 (처음 한 번만 계산, 이후 직접 조정한 값 보존)
//  - 칼날 면이 돌아가 있으면 SamuraiKatana/Mesh를 칼날 축(Y) 기준으로 돌리면 됨
public static class SamuraiKatanaSetup
{
    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";
    const string WeaponFbxPath = "Assets/ThirdParty/Samurai/Mesh/SM_Samurai_weapon.fbx";
    const string WeaponPrefabPath = "Assets/ThirdParty/Samurai/Prefabs/SM_Samurai_weapon.prefab";
    const string MeshName = "katana";
    const string OriginalName = "ZenitsuKatana";
    const string PackName = "SamuraiKatana";

    [MenuItem("Tools/Setup/Katana/Use Samurai Pack Katana")]
    static void UsePack() => Apply(true);

    [MenuItem("Tools/Setup/Katana/Use Original Katana (Zenitsu)")]
    static void UseOriginal() => Apply(false);

    static void Apply(bool usePack)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Katana] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform original = SamuraiSkinSetup.FindDeep(root.transform, OriginalName);
            if (original == null)
            {
                Debug.LogError($"[Katana] Player 프리팹에서 {OriginalName}을 찾지 못했습니다.");
                return;
            }

            Transform pack = original.parent.parent != null ? original.parent.parent.Find(PackName) : null;
            if (usePack && pack == null)
            {
                pack = CreatePackKatana(original);
                if (pack == null) return;
            }

            original.gameObject.SetActive(!usePack);
            if (pack != null) pack.gameObject.SetActive(usePack);

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log(usePack ? "[Katana] Samurai 팩 카타나 사용 (되돌리기: Tools/Setup/Katana/Use Original Katana)" : "[Katana] 원래 카타나(Zenitsu) 사용");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 원래 칼은 원점 = 날밑, +Y = 칼날 방향 (소켓 스케일 포함). 팩 칼을 그 날밑·방향·칼날 길이에 맞춤
    static Transform CreatePackKatana(Transform original)
    {
        Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(WeaponFbxPath).OfType<Mesh>().FirstOrDefault(m => m.name == MeshName);
        var weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPrefabPath);
        Transform blade = original.Find("Blade");
        MeshFilter bladeFilter = blade != null ? blade.GetComponent<MeshFilter>() : null;
        if (mesh == null || bladeFilter == null || bladeFilter.sharedMesh == null)
        {
            Debug.LogError("[Katana] 팩 카타나 메시 또는 원래 칼의 Blade 메시를 찾지 못했습니다.");
            return null;
        }

        // 원래 칼: 월드 기준 날밑 위치·칼날 방향·칼날 길이
        Vector3 guard = original.position;
        Vector3 dir = original.TransformDirection(Vector3.up).normalized;
        float targetLength = 0f;
        foreach (Vector3 v in bladeFilter.sharedMesh.vertices)
            targetLength = Mathf.Max(targetLength, Vector3.Dot(blade.TransformPoint(v) - guard, dir));

        if (!AnalyzeKatana(mesh, out Vector3 meshGuard, out Vector3 meshDir, out float meshLength) || targetLength <= 0.01f)
        {
            Debug.LogError("[Katana] 팩 카타나의 칼날 방향을 계산하지 못했습니다.");
            return null;
        }

        // 피벗: 원래 칼과 같은 자리·회전, 스케일 1 (소켓의 비균일 스케일을 받지 않게 소켓 옆에 둠)
        Transform parent = original.parent.parent;
        var pivot = new GameObject(PackName).transform;
        pivot.SetParent(parent, false);
        pivot.position = guard;
        pivot.rotation = original.rotation;

        float scale = targetLength / meshLength;
        Quaternion align = Quaternion.FromToRotation(meshDir, Vector3.up);

        var meshGo = new GameObject("Mesh");
        meshGo.transform.SetParent(pivot, false);
        meshGo.transform.localRotation = align;
        meshGo.transform.localScale = Vector3.one * scale;
        meshGo.transform.localPosition = -(align * meshGuard) * scale; // 날밑이 피벗 원점에 오도록
        meshGo.AddComponent<MeshFilter>().sharedMesh = mesh;

        Material source = FindSourceMaterial(weaponPrefab);
        var renderer = meshGo.AddComponent<MeshRenderer>();
        if (source != null) renderer.sharedMaterial = SamuraiSkinSetup.GetOrCreateLit(source);

        Debug.Log($"[Katana] 팩 카타나 배치 · 길이 {meshLength:0.00}m → {targetLength:0.00}m (×{scale:0.00})");
        return pivot;
    }

    static Material FindSourceMaterial(GameObject weaponPrefab)
    {
        if (weaponPrefab == null) return null;
        foreach (MeshFilter mf in weaponPrefab.GetComponentsInChildren<MeshFilter>(true))
            if (mf.name == MeshName && mf.TryGetComponent(out MeshRenderer r)) return r.sharedMaterial;
        return null;
    }

    // 핵심: 메시의 가장 긴 축(주성분)을 칼 축으로 보고, 축을 따라 가장 굵은 곳을 날밑으로, 날밑에서 더 긴 쪽을 칼날로 판단
    static bool AnalyzeKatana(Mesh mesh, out Vector3 guard, out Vector3 bladeDir, out float bladeLength)
    {
        Vector3[] verts = mesh.vertices;
        guard = Vector3.zero;
        bladeDir = Vector3.up;
        bladeLength = 0f;
        if (verts.Length < 10) return false;

        Vector3 mean = Vector3.zero;
        foreach (Vector3 v in verts) mean += v;
        mean /= verts.Length;

        // 공분산 행렬의 최대 고유벡터 (거듭제곱법)
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (Vector3 v in verts)
        {
            Vector3 d = v - mean;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
            yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
        }
        Vector3 axis = new Vector3(1f, 1f, 1f).normalized;
        for (int i = 0; i < 32; i++)
        {
            axis = new Vector3(xx * axis.x + xy * axis.y + xz * axis.z,
                               xy * axis.x + yy * axis.y + yz * axis.z,
                               xz * axis.x + yz * axis.y + zz * axis.z).normalized;
        }

        float tMin = float.MaxValue, tMax = float.MinValue;
        foreach (Vector3 v in verts)
        {
            float t = Vector3.Dot(v - mean, axis);
            tMin = Mathf.Min(tMin, t);
            tMax = Mathf.Max(tMax, t);
        }

        // 축을 40칸으로 나눠 칸마다 축에서 가장 먼 거리 → 가장 굵은 칸 = 날밑
        const int Bins = 40;
        var radius = new float[Bins];
        float span = Mathf.Max(tMax - tMin, 0.0001f);
        foreach (Vector3 v in verts)
        {
            Vector3 d = v - mean;
            float t = Vector3.Dot(d, axis);
            int bin = Mathf.Clamp((int)((t - tMin) / span * Bins), 0, Bins - 1);
            radius[bin] = Mathf.Max(radius[bin], (d - axis * t).magnitude);
        }
        int guardBin = 0;
        for (int i = 1; i < Bins; i++) if (radius[i] > radius[guardBin]) guardBin = i;
        float tGuard = tMin + (guardBin + 0.5f) * span / Bins;

        bool positive = tMax - tGuard >= tGuard - tMin;
        bladeDir = positive ? axis : -axis;
        bladeLength = positive ? tMax - tGuard : tGuard - tMin;
        guard = mean + axis * tGuard;
        return bladeLength > 0.01f;
    }
}
