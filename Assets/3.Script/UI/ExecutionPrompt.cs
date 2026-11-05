using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 처형 표시: 처형 가능한 적의 몸 중앙에 [F] 키캡 + 처형 기회 남은 시간 고리 + 붉은 광채, 아래에 "처형"
// 나타날 때 튀어나오고, 떠 있는 동안 광채가 숨쉬듯 밝아졌다 어두워짐. F로 처형이 시작되면 크게 번지며 사라짐
// 키캡·고리·광채 이미지는 실행 중에 만들어 씀 (텍스처 에셋 불필요)
public class ExecutionPrompt : MonoBehaviour
{
    [SerializeField] PlayerExecution execution;
    [SerializeField] Font font;
    [SerializeField] string key = "F";
    [SerializeField] string caption = "처형"; // 예전 필드 label("좌클릭 처형")이 씬에 남아 있어 이름을 바꿈
    [SerializeField] Color accent = new Color(1f, 0.22f, 0.18f, 1f);
    [SerializeField] float bodyHeight = 1.1f; // 몸 중앙 (발 기준)
    [SerializeField] float size = 64f;

    RectTransform area, root;
    Image glow, ring, keycap;
    Text keyText, labelText;
    CanvasGroup group;
    Camera cam;
    EnemySwordsman shown;
    Vector3 lastWorld;
    Sequence showSeq;
    Tween glowTween;
    bool wasActive;

    void Awake()
    {
        area = (RectTransform)transform;
        if (execution == null) execution = FindAnyObjectByType<PlayerExecution>();
        Build();
    }

    void LateUpdate()
    {
        if (execution == null) return;

        // 처형이 시작된 순간: 마지막 자리에서 크게 번지며 사라짐
        if (execution.IsActive && !wasActive && shown != null) Burst();
        wasActive = execution.IsActive;

        EnemySwordsman candidate = execution.IsActive ? null : execution.Candidate;
        if (candidate != shown && !execution.IsActive) Show(candidate);
        if (shown != null)
        {
            lastWorld = shown.transform.position + Vector3.up * bodyHeight;
            ring.fillAmount = shown.ExecutionRemaining01;
        }
        if (root.gameObject.activeSelf) Place(lastWorld);
    }

    void Show(EnemySwordsman candidate)
    {
        shown = candidate;
        showSeq?.Kill();
        glowTween?.Kill();
        root.gameObject.SetActive(candidate != null);
        if (candidate == null) return;

        group.alpha = 0f;
        root.localScale = Vector3.one * 1.8f;
        showSeq = DOTween.Sequence().SetUpdate(true).SetLink(root.gameObject)
            .Append(root.DOScale(1f, 0.22f).SetEase(Ease.OutBack, 2f))
            .Join(DOTween.To(() => group.alpha, a => group.alpha = a, 1f, 0.12f));
        glow.color = WithAlpha(accent, 0.35f);
        glowTween = DOTween.To(() => glow.color.a, a => glow.color = WithAlpha(accent, a), 0.8f, 0.45f)
            .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(root.gameObject);
    }

    void Burst()
    {
        shown = null;
        showSeq?.Kill();
        glowTween?.Kill();
        keycap.color = Color.white;
        showSeq = DOTween.Sequence().SetUpdate(true).SetLink(root.gameObject)
            .Append(root.DOScale(1.7f, 0.22f).SetEase(Ease.OutQuad))
            .Join(DOTween.To(() => group.alpha, a => group.alpha = a, 0f, 0.22f))
            .OnComplete(() =>
            {
                root.gameObject.SetActive(false);
                keycap.color = new Color(1f, 1f, 1f, 0.95f);
            });
    }

    void Place(Vector3 world)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(world);
        group.alpha = screen.z > 0f ? group.alpha : 0f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, null, out Vector2 local);
        root.anchoredPosition = local;
    }

    // ===== 생성 =====
    void Build()
    {
        root = NewRect("ExecutionMark", transform, new Vector2(size * 2.4f, size * 2.4f));
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        glow = NewImage("Glow", root, size * 2.3f, MakeGlow(64), WithAlpha(accent, 0.5f));
        ring = NewImage("Ring", root, size * 1.55f, MakeRing(128, 0.11f), accent);
        ring.type = Image.Type.Filled;
        ring.fillMethod = Image.FillMethod.Radial360;
        ring.fillOrigin = (int)Image.Origin360.Top;
        ring.fillClockwise = false; // 시계 반대로 줄어듦
        keycap = NewImage("Keycap", root, size, MakeKeycap(64, 12, 3), new Color(1f, 1f, 1f, 0.95f));

        keyText = NewText("Key", keycap.rectTransform, key, Mathf.RoundToInt(size * 0.55f), Color.white);
        labelText = NewText("Label", root, caption, Mathf.RoundToInt(size * 0.32f), accent);
        labelText.rectTransform.anchoredPosition = new Vector2(0f, -size * 1.05f);

        root.gameObject.SetActive(false);
    }

    RectTransform NewRect(string name, Transform parent, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = sizeDelta;
        return rt;
    }

    Image NewImage(string name, Transform parent, float side, Sprite sprite, Color color)
    {
        var image = NewRect(name, parent, new Vector2(side, side)).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text NewText(string name, Transform parent, string content, int fontSize, Color color)
    {
        var text = NewRect(name, parent, new Vector2(200f, fontSize * 1.6f)).gameObject.AddComponent<Text>();
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // 둥근 사각형 키캡: 반투명 검은 바탕 + 흰 테두리 (가장자리 1px 부드럽게)
    static Sprite MakeKeycap(int res, int radius, int border)
    {
        var tex = NewTexture(res);
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float d = RoundedRectDistance(x + 0.5f, y + 0.5f, res, radius); // 안쪽이 음수
            float inside = Mathf.Clamp01(0.5f - d);
            float edge = Mathf.Clamp01(0.5f - Mathf.Abs(d + border * 0.5f) + border * 0.5f);
            Color c = Color.Lerp(new Color(0.05f, 0.05f, 0.06f, 0.78f), Color.white, edge);
            c.a *= inside;
            tex.SetPixel(x, y, c);
        }
        return ToSprite(tex);
    }

    static Sprite MakeRing(int res, float thickness)
    {
        var tex = NewTexture(res);
        float c = res * 0.5f, outer = c - 1f, inner = outer - res * thickness;
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
            float a = Mathf.Clamp01(outer - r + 0.5f) * Mathf.Clamp01(r - inner + 0.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ToSprite(tex);
    }

    static Sprite MakeGlow(int res)
    {
        var tex = NewTexture(res);
        float c = res * 0.5f;
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float k = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c)) / c);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, k * k));
        }
        return ToSprite(tex);
    }

    static float RoundedRectDistance(float x, float y, int res, int radius)
    {
        float half = res * 0.5f;
        float qx = Mathf.Abs(x - half) - (half - radius - 1f);
        float qy = Mathf.Abs(y - half) - (half - radius - 1f);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    static Texture2D NewTexture(int res) =>
        new Texture2D(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

    static Sprite ToSprite(Texture2D tex)
    {
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }
}
