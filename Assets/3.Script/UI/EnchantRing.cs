using UnityEngine;
using UnityEngine.UI;

// 뇌검 각성(R) 슬롯: Q 슬롯 옆 원형 링. GameHUD가 실행 중에 만들어 붙임 (HUD 셋업 재실행 불필요)
// 평소: 뇌기가 최소 칸 이상이면 R 글자가 금색으로 켜짐 / 각성 중: 청백 링이 남은 시간만큼 줄어들고 가운데 남은 초, 마지막 2초는 깜빡임
public class EnchantRing : MonoBehaviour
{
    static readonly Color Track = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color RingColor = new Color(0.55f, 0.85f, 1f);
    static readonly Color Ready = new Color(1f, 0.82f, 0.3f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.35f);
    static Sprite ringSprite;

    PlayerEnchant enchant;
    PlayerThunder thunder;
    Image fill;
    Text key, seconds;
    bool wasActive, wasReady;

    public void Build(Font font, PlayerEnchant source, PlayerThunder gauge)
    {
        enchant = source;
        thunder = gauge;
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Image track = NewImage("Track", Track);
        track.type = Image.Type.Simple;
        fill = NewImage("Fill", RingColor);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = false; // 시계 반대로 줄어듦 = 시곗바늘 방향으로 비워짐
        fill.fillAmount = 0f;

        float size = ((RectTransform)transform).sizeDelta.x;
        key = NewText("Key", font, Mathf.RoundToInt(size * 0.38f), "R", new Vector2(0f, size * 0.06f));
        seconds = NewText("Seconds", font, Mathf.RoundToInt(size * 0.2f), "", new Vector2(0f, -size * 0.24f));
    }

    void LateUpdate()
    {
        if (enchant == null || fill == null) return;

        bool active = enchant.IsActive;
        bool ready = !active && thunder != null && thunder.Gauge >= enchant.MinCells - 0.001f;

        fill.fillAmount = active ? enchant.Remaining01 : 0f;
        // 마지막 2초는 링이 깜빡여 곧 방전된다는 신호
        float blink = active && enchant.Remaining < 2f ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 18f) : 0f;
        fill.color = Color.Lerp(RingColor, Color.white, blink);
        seconds.text = active ? enchant.Remaining.ToString("0.0") : "";
        key.color = active ? Color.white : ready ? Ready : Dim;

        if (active && !wasActive) UITween.Punch(transform, 0.3f, 0.4f);
        if (ready && !wasReady) UITween.Punch(key.transform, 0.35f, 0.4f);
        wasActive = active;
        wasReady = ready;
    }

    Image NewImage(string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var image = go.AddComponent<Image>();
        image.sprite = RingSprite();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text NewText(string name, Font font, int size, string value, Vector2 offset)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size * 3f, size * 1.4f);
        rt.anchoredPosition = offset;
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.text = value;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    // 두께 있는 고리 텍스처를 실행 중에 한 번 만듦 (가장자리 1px 부드럽게)
    static Sprite RingSprite()
    {
        if (ringSprite != null) return ringSprite;
        const int n = 128;
        const float outer = 63f, inner = 52f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "EnchantRing" };
        var pixels = new Color32[n * n];
        Vector2 c = new Vector2((n - 1) * 0.5f, (n - 1) * 0.5f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return ringSprite;
    }
}
