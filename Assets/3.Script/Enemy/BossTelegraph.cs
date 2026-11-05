using UnityEngine;

// 보스 도약 범위 예고: 바닥에 붉은 원판 + 안쪽에서 바깥으로 차오르는 원 (가득 차는 순간 착지)
// 재질이 없으면 URP Unlit 반투명 재질을 실행 중에 만듦
public class BossTelegraph : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static Material runtimeMaterial;
    const float Thickness = 0.005f; // 원기둥 높이 2 × 0.005 = 1cm 두께

    Transform fill;
    Material areaMat, fillMat;

    public static BossTelegraph Create(Material source, float radius, Vector3 position)
    {
        var go = new GameObject("BossTelegraph");
        var t = go.AddComponent<BossTelegraph>();
        Material baseMat = source != null ? source : RuntimeMaterial();

        t.areaMat = new Material(baseMat);
        t.fillMat = new Material(baseMat);
        Disc("Area", go.transform, t.areaMat, new Color(1f, 0.12f, 0.08f, 0.22f), 0f);
        t.fill = Disc("Fill", go.transform, t.fillMat, new Color(1f, 0.25f, 0.1f, 0.45f), 0.02f);
        t.fill.localScale = new Vector3(0f, Thickness, 0f);

        go.transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
        t.SetPosition(position);
        return t;
    }

    public void SetPosition(Vector3 position)
    {
        // 지형 높이에 붙임 (플레이어가 공중이어도 바닥에 표시)
        if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<IDamageable>() == null)
            position.y = hit.point.y;
        transform.position = position + Vector3.up * 0.05f;
    }

    public void SetProgress(float k)
    {
        k = Mathf.Clamp01(k);
        fill.localScale = new Vector3(k, Thickness, k);
    }

    void OnDestroy()
    {
        if (areaMat != null) Destroy(areaMat);
        if (fillMat != null) Destroy(fillMat);
    }

    static Transform Disc(string name, Transform parent, Material mat, Color color, float lift)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.name = name;
        disc.transform.SetParent(parent, false);
        disc.transform.localPosition = Vector3.up * lift;
        disc.transform.localScale = new Vector3(1f, Thickness, 1f);
        var r = disc.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        mat.SetColor(BaseColorId, color);
        r.sharedMaterial = mat;
        return disc.transform;
    }

    // URP Unlit을 반투명(알파 블렌드)으로
    public static Material RuntimeMaterial()
    {
        if (runtimeMaterial != null) return runtimeMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        runtimeMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "BossTelegraph (Runtime)" };
        MakeTransparent(runtimeMaterial);
        return runtimeMaterial;
    }

    public static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }
}
