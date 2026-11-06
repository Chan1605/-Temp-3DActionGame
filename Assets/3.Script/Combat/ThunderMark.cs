using UnityEngine;

// 뇌인(雷印) = 흐트러짐 표시. 예전 3칸 스택은 없앰
// 대상(IStaggerable)이 흐트러져 있는 동안 머리 위에 번개색 마름모가 맥박치듯 뜸 → "지금 Q로 벨 수 있다"
// 판정은 하지 않음 (일섬은 IStaggerable을 직접 봄). 적 프리팹에 이미 붙어 있어 클래스 이름은 유지
public class ThunderMark : MonoBehaviour
{
    [Header("Icon")]
    [SerializeField] float iconHeight = 2.4f;
    [SerializeField] float iconSize = 0.3f;
    [SerializeField] Color litColor = new Color(0.55f, 0.9f, 1f);
    [SerializeField] Color flashColor = new Color(1f, 0.95f, 0.6f);

    static Material iconMaterial;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    IDamageable owner;
    IStaggerable staggerable;
    Transform iconRoot;
    Renderer icon;
    MaterialPropertyBlock block;
    Camera cam;
    bool lit;
    float litAt;

    public bool IsLit => lit;
    public static bool ShowIcons = true; // [변경] PlayerSkill이 흐트러짐 모드일 때만 켬

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ShowIcons = true;

    void Awake()
    {
        owner = GetComponent<IDamageable>();
        staggerable = GetComponent<IStaggerable>();
        block = new MaterialPropertyBlock();
        BuildIcon();
        SetLit(false);
    }

    void LateUpdate()
    {
        bool now = ShowIcons && staggerable != null && staggerable.IsStaggered && (owner == null || owner.CanBeTargeted); // [변경]
        if (now != lit) SetLit(now);
        if (!lit || iconRoot == null) return;

        if (cam == null) cam = Camera.main;
        if (cam != null) iconRoot.rotation = cam.transform.rotation;

        // 켜진 직후 크게 튀었다가 맥박
        float since = Time.time - litAt;
        float pop = since < 0.15f ? Mathf.Lerp(1.8f, 1f, since / 0.15f) : 1f;
        iconRoot.localScale = Vector3.one * (pop + Mathf.Sin(Time.unscaledTime * 12f) * 0.12f);
        block.SetColor(BaseColorId, Color.Lerp(litColor, flashColor, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 18f)));
        icon.SetPropertyBlock(block);
    }

    void SetLit(bool value)
    {
        lit = value;
        litAt = Time.time;
        if (iconRoot != null) iconRoot.gameObject.SetActive(value);
    }

    // 마름모 = 45도 돌린 Quad. 그림자·충돌 없음
    void BuildIcon()
    {
        if (iconMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;
            iconMaterial = new Material(shader) { name = "ThunderMarkIcon" };
        }

        iconRoot = new GameObject("ThunderMarkIcon").transform;
        iconRoot.SetParent(transform, false);
        iconRoot.localPosition = Vector3.up * iconHeight;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(iconRoot, false);
        quad.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        quad.transform.localScale = Vector3.one * iconSize;
        icon = quad.GetComponent<Renderer>();
        icon.sharedMaterial = iconMaterial;
        icon.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        icon.receiveShadows = false;
    }
}
