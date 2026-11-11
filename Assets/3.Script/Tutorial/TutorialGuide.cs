using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 튜토리얼 안내 패널 (화면 위 가운데) + 수련터 배너 (가운데). TutorialDirector가 코드로 만들어 씀
// 메뉴 창보다 아래에 그려지고, 녹화 모드(F10)에선 다른 HUD와 같이 숨겨짐
public class TutorialGuide : MonoBehaviour
{
    const float PanelW = 860f, PanelH = 178f;

    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);
    static readonly Color Body = new Color(0.86f, 0.88f, 0.92f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.5f);

    RectTransform panel;
    CanvasGroup panelGroup;
    Image frame;
    Text header, counter, title, description, keys, progress, skipHint;
    Text banner;
    CanvasGroup bannerGroup;

    float panelAlpha, panelTarget;
    float punch;
    float bannerTimer;
    float finishTimer = -1f;

    public static TutorialGuide Create(Transform parent, Font titleFont, Font bodyFont)
    {
        Canvas canvas = UIKit.CreateCanvas(parent, "TutorialGuide", 30);
        TutorialGuide guide = canvas.gameObject.AddComponent<TutorialGuide>();
        guide.Build(canvas.transform, titleFont, bodyFont != null ? bodyFont : titleFont);
        return guide;
    }

    void Build(Transform root, Font titleFont, Font bodyFont)
    {
        Image bg = UIKit.Image(root, UIKit.RoundRect(64, 64, 18f, 0f), new Color(0.04f, 0.05f, 0.07f, 0.78f));
        bg.raycastTarget = false;
        panel = bg.rectTransform;
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0f, -36f);
        panel.sizeDelta = new Vector2(PanelW, PanelH);
        panelGroup = bg.gameObject.AddComponent<CanvasGroup>();
        panelGroup.alpha = 0f;

        frame = UIKit.Image(panel, UIKit.RoundRect(64, 64, 19f, 2f), new Color(Gold.r, Gold.g, Gold.b, 0.35f));
        frame.raycastTarget = false;
        UIKit.Stretch(frame.rectTransform);
        frame.rectTransform.offsetMin = new Vector2(-1f, -1f);
        frame.rectTransform.offsetMax = new Vector2(1f, 1f);

        const float pad = 34f, inner = PanelW - pad * 2f;
        header = UIKit.Text(panel, "", bodyFont, 19, Gold, TextAnchor.MiddleLeft);
        UIKit.Place(header.rectTransform, pad, 26f, inner * 0.7f, 26f);
        counter = UIKit.Text(panel, "", bodyFont, 19, Dim, TextAnchor.MiddleRight);
        UIKit.Place(counter.rectTransform, pad + inner * 0.3f, 26f, inner * 0.7f, 26f);

        title = UIKit.Text(panel, "", titleFont, 34, Color.white, TextAnchor.MiddleLeft);
        UIKit.Place(title.rectTransform, pad, 64f, inner * 0.72f, 44f);
        progress = UIKit.Text(panel, "", titleFont, 30, Gold, TextAnchor.MiddleRight);
        UIKit.Place(progress.rectTransform, pad + inner * 0.62f, 64f, inner * 0.38f, 44f);

        description = UIKit.Text(panel, "", bodyFont, 21, Body, TextAnchor.UpperLeft);
        description.horizontalOverflow = HorizontalWrapMode.Wrap;
        UIKit.Place(description.rectTransform, pad, 112f, inner, 52f);

        keys = UIKit.Text(panel, "", titleFont, 20, Gold, TextAnchor.MiddleLeft);
        UIKit.Place(keys.rectTransform, pad, 154f, inner * 0.65f, 28f);
        skipHint = UIKit.Text(panel, "", bodyFont, 18, Dim, TextAnchor.MiddleRight);
        UIKit.Place(skipHint.rectTransform, pad + inner * 0.45f, 154f, inner * 0.55f, 28f);

        banner = UIKit.Text(root, "", titleFont, 50, Color.white, TextAnchor.MiddleCenter);
        RectTransform b = banner.rectTransform;
        b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.62f);
        b.sizeDelta = new Vector2(1400f, 80f);
        banner.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
        bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 0f;
    }

    public void ShowStep(string stationLabel, int number, int total, string stepTitle, string text, string keyText, int count)
    {
        header.text = stationLabel;
        counter.text = $"{number} / {total}";
        title.text = stepTitle;
        title.color = Color.white;
        description.text = text;
        keys.text = string.IsNullOrEmpty(keyText) ? "" : $"[ {keyText} ]";
        skipHint.text = "";
        SetProgress(0, count);
        panelTarget = 1f;
        finishTimer = -1f;
        punch = 0.6f;
    }

    public void SetProgress(int value, int count) => progress.text = count > 1 ? $"{value} / {count}" : "";

    public void ShowDone()
    {
        title.color = Gold;
        progress.text = "완료";
        skipHint.text = "";
        punch = 1f;
    }

    public void ShowSkipHint(Key key)
    {
        if (string.IsNullOrEmpty(skipHint.text)) skipHint.text = $"{key}: 건너뛰기";
    }

    public void ShowBanner(string text)
    {
        banner.text = text;
        bannerTimer = 2.2f;
    }

    public void ShowFinish(string finishTitle, string text)
    {
        header.text = "수련장";
        counter.text = "";
        title.text = finishTitle;
        title.color = Gold;
        description.text = text;
        keys.text = "";
        progress.text = "";
        skipHint.text = "";
        panelTarget = 1f;
        punch = 1f;
        finishTimer = 6f;
        ShowBanner(finishTitle);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (finishTimer >= 0f)
        {
            finishTimer -= dt;
            if (finishTimer < 0f) panelTarget = 0f;
        }
        panelAlpha = Mathf.MoveTowards(panelAlpha, panelTarget, dt * 4f);
        panelGroup.alpha = panelAlpha;

        // 새 단계·완료 때 살짝 커졌다 돌아오는 펀치
        punch = Mathf.MoveTowards(punch, 0f, dt * 3.5f);
        panel.localScale = Vector3.one * (1f + 0.04f * punch * punch);
        frame.color = new Color(Gold.r, Gold.g, Gold.b, 0.35f + 0.5f * punch);

        if (bannerTimer > 0f) bannerTimer -= dt;
        float t = bannerTimer;
        bannerGroup.alpha = t <= 0f ? 0f : Mathf.Clamp01(Mathf.Min(t / 0.5f, (2.2f - t) / 0.25f));
    }
}
