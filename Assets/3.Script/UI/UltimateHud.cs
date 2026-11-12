using UnityEngine;
using UnityEngine.UI;

// 궁극기 게이지 HUD (오른쪽 아래, Q 슬롯 왼쪽): 마름모 3칸이 아래부터 차오르고, 꽉 찬 칸은 은은하게 빛남 + V 키 표시
// PlayerUltimate가 실행 중에 만듦. 칸이 새로 차면 번쩍, 게이지가 없는데 V를 누르면 흔들림
public class UltimateHud : MonoBehaviour
{
    static readonly Color Empty = new Color(0.05f, 0.06f, 0.09f, 0.75f);
    static readonly Color Fill = new Color(0.55f, 0.72f, 1f, 1f);
    static readonly Color Full = new Color(0.95f, 0.85f, 0.5f, 1f);

    PlayerUltimate ultimate;
    RectTransform root;
    readonly Image[] fills = new Image[PlayerUltimate.MaxBars];
    readonly Image[] glows = new Image[PlayerUltimate.MaxBars];
    Text key;
    float shake, punch;
    Vector2 basePos;

    public static UltimateHud Create(PlayerUltimate owner)
    {
        Canvas canvas = UIKit.CreateCanvas(null, "UltimateHud", 12);
        var hud = canvas.gameObject.AddComponent<UltimateHud>();
        hud.ultimate = owner;
        hud.Build(canvas.transform);
        return hud;
    }

    void Build(Transform canvas)
    {
        var go = new GameObject("Ultimate", typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        root = (RectTransform)go.transform;
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
        basePos = new Vector2(-180f, 56f); // Q 슬롯(108px, 오른쪽 아래 여백 56) 왼쪽
        root.anchoredPosition = basePos;
        root.sizeDelta = new Vector2(60f, 130f);

        Sprite square = UIKit.RoundRect(32, 32, 5f, 0f);
        for (int i = 0; i < PlayerUltimate.MaxBars; i++)
        {
            float y = 42f + i * 32f;
            Image glow = Cell(square, y, 34f, new Color(Full.r, Full.g, Full.b, 0f));
            Cell(square, y, 24f, Empty);
            Image fill = Cell(square, y, 24f, Fill);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Vertical;
            fill.fillOrigin = (int)Image.OriginVertical.Bottom;
            fills[i] = fill;
            glows[i] = glow;
        }

        SettingsMenu menu = FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include);
        key = UIKit.Text(root, "V", menu != null ? menu.TitleFont : null, 22, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);
        RectTransform k = key.rectTransform;
        k.anchorMin = k.anchorMax = new Vector2(0.5f, 0f);
        k.anchoredPosition = new Vector2(0f, 12f);
        k.sizeDelta = new Vector2(40f, 26f);
    }

    Image Cell(Sprite sprite, float y, float size, Color color)
    {
        Image img = UIKit.Image(root, sprite, color);
        img.type = Image.Type.Simple;
        img.raycastTarget = false;
        RectTransform r = img.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
        r.anchoredPosition = new Vector2(0f, y);
        r.sizeDelta = new Vector2(size, size);
        r.localRotation = Quaternion.Euler(0f, 0f, 45f); // 마름모
        return img;
    }

    public void Ready(int bars) => punch = 1f;
    public void Deny() => shake = 0.35f;

    void Update()
    {
        if (ultimate == null)
        {
            Destroy(gameObject);
            return;
        }
        float dt = Time.unscaledDeltaTime;
        float gauge = ultimate.Gauge;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
        for (int i = 0; i < fills.Length; i++)
        {
            float amount = Mathf.Clamp01(gauge - i);
            bool full = amount >= 1f;
            fills[i].fillAmount = amount;
            fills[i].color = full ? Full : Fill;
            float g = full ? 0.25f + 0.35f * pulse + 0.6f * punch : 0f;
            glows[i].color = new Color(Full.r, Full.g, Full.b, ultimate.Active ? 0f : g);
        }
        key.color = new Color(1f, 1f, 1f, ultimate.Bars > 0 ? 0.95f : 0.45f);

        punch = Mathf.MoveTowards(punch, 0f, dt * 2.5f);
        root.localScale = Vector3.one * (1f + 0.15f * punch * punch);
        shake = Mathf.MoveTowards(shake, 0f, dt);
        root.anchoredPosition = basePos + new Vector2(Mathf.Sin(Time.unscaledTime * 70f) * 8f * shake, 0f);
    }
}
