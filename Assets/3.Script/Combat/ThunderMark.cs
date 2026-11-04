using System;
using UnityEngine;

// P4 뇌인(雷印): 적에게 쌓이는 표식 3칸. 가득 차면 벽력일섬 즉사 대상
// 일정 시간 안 맞으면 한 칸씩 사라짐. 머리 위 마름모 3개로 표시 (0칸이면 숨김)
public class ThunderMark : MonoBehaviour
{
    [SerializeField] int maxMarks = 3;
    [SerializeField] float decayDelay = 5f;     // 마지막으로 맞은 뒤 이 시간이 지나면
    [SerializeField] float decayInterval = 1.5f; // 이 간격으로 한 칸씩 감소

    [Header("Icon")]
    [SerializeField] float iconHeight = 2.4f;
    [SerializeField] float iconSize = 0.2f;
    [SerializeField] float iconSpacing = 0.28f;
    [SerializeField] Color emptyColor = new Color(0.15f, 0.17f, 0.22f);
    [SerializeField] Color filledColor = new Color(0.45f, 0.85f, 1f);
    [SerializeField] Color fullColor = new Color(1f, 0.95f, 0.55f);

    static Material iconMaterial;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    IDamageable owner;
    Transform iconRoot;
    Renderer[] icons;
    MaterialPropertyBlock block;
    int marks;
    float lastHitTime;
    float decayTimer;
    Camera cam;

    public int Marks => marks;
    public int MaxMarks => maxMarks;
    public bool IsFull => marks >= maxMarks;
    public event Action<ThunderMark> Completed; // 가득 찬 순간 (P5 전이·뇌기 보너스용)

    public static ThunderMark Of(Component c) => c != null ? c.GetComponentInParent<ThunderMark>() : null;

    void Awake()
    {
        owner = GetComponent<IDamageable>();
        block = new MaterialPropertyBlock();
        BuildIcons();
        Refresh();
    }

    // 반환값: 이번에 가득 찼으면 true
    public bool Add(int amount)
    {
        if (amount <= 0 || !Alive()) return false;

        bool wasFull = IsFull;
        marks = Mathf.Min(maxMarks, marks + amount);
        lastHitTime = Time.time;
        decayTimer = 0f;
        Refresh();

        if (wasFull || !IsFull) return false;
        Completed?.Invoke(this);
        return true;
    }

    public bool Fill() => Add(maxMarks);

    public void Clear()
    {
        marks = 0;
        Refresh();
    }

    void Update()
    {
        if (marks == 0) return;

        if (!Alive())
        {
            Clear();
            return;
        }

        // 맞은 지 decayDelay가 지나면 decayInterval마다 한 칸씩
        if (Time.time - lastHitTime < decayDelay) return;
        decayTimer += Time.deltaTime;
        if (decayTimer < decayInterval) return;
        decayTimer = 0f;
        marks--;
        Refresh();
    }

    // 카메라를 바라보게 하고, 가득 찼으면 맥박처럼 커졌다 작아짐
    void LateUpdate()
    {
        if (iconRoot == null || !iconRoot.gameObject.activeSelf) return;
        if (cam == null) cam = Camera.main;
        if (cam != null) iconRoot.rotation = cam.transform.rotation;

        float pulse = IsFull ? 1f + Mathf.Sin(Time.unscaledTime * 12f) * 0.15f : 1f;
        iconRoot.localScale = Vector3.one * pulse;
    }

    bool Alive() => owner == null || owner.CanBeTargeted;

    void Refresh()
    {
        if (iconRoot == null) return;
        iconRoot.gameObject.SetActive(marks > 0);
        for (int i = 0; i < icons.Length; i++)
        {
            Color c = i >= marks ? emptyColor : IsFull ? fullColor : filledColor;
            block.SetColor(BaseColorId, c);
            icons[i].SetPropertyBlock(block);
        }
    }

    // 마름모 = 45도 돌린 사각 Quad. 그림자·충돌 없음
    void BuildIcons()
    {
        if (iconMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;
            iconMaterial = new Material(shader) { name = "ThunderMarkIcon" };
        }

        iconRoot = new GameObject("ThunderMarkIcons").transform;
        iconRoot.SetParent(transform, false);
        iconRoot.localPosition = Vector3.up * iconHeight;

        icons = new Renderer[maxMarks];
        for (int i = 0; i < maxMarks; i++)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = $"Mark_{i + 1}";
            quad.transform.SetParent(iconRoot, false);
            quad.transform.localPosition = new Vector3((i - (maxMarks - 1) * 0.5f) * iconSpacing, 0f, 0f);
            quad.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            quad.transform.localScale = Vector3.one * iconSize;

            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = iconMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            icons[i] = r;
        }
    }
}
