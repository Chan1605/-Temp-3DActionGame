using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 보스 체력 막대 (화면 아래 가운데): 이름 · 체력(잔상) · 2페이즈 눈금 · 체간 막대(가운데서 양옆으로 차오름)
// GameHUD가 실행 중에 만들어 붙임 (HUD 셋업 재실행 불필요). 보스(EnemySwordsman.ActiveBoss)가 등장하면 나타나고 쓰러지면 사라짐
public class BossHealthBar : MonoBehaviour
{
    const float Width = 880f;
    static readonly Color HpColor = new Color(0.78f, 0.12f, 0.1f);
    static readonly Color TrailColor = new Color(1f, 0.85f, 0.6f, 0.85f);
    static readonly Color PostureLow = new Color(1f, 0.8f, 0.3f);
    static readonly Color PostureHigh = new Color(1f, 0.35f, 0.12f);

    RectTransform root, hpFill, hpTrail, postureFill;
    Image postureImage;
    Text nameText, phaseText;
    CanvasGroup group;
    EnemySwordsman shown;
    float trail = 1f, trailHoldUntil, lastHp = 1f;
    bool phaseShown, hiding, showing;

    public void Build(Font font)
    {
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        root = Rect("Root", transform, new Vector2(0.5f, 0f), new Vector2(Width, 70f));
        root.anchoredPosition = new Vector2(0f, 92f);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        nameText = Label("Name", font, 28, Color.white, TextAnchor.LowerLeft);
        nameText.rectTransform.anchoredPosition = new Vector2(-Width * 0.5f + 4f, 30f);
        phaseText = Label("Phase", font, 22, new Color(1f, 0.3f, 0.25f), TextAnchor.LowerRight);
        phaseText.rectTransform.anchoredPosition = new Vector2(Width * 0.5f - 4f, 30f);
        phaseText.text = "";

        RectTransform hpBack = Bar("HpBack", root, new Vector2(0f, 16f), 16f, new Color(0f, 0f, 0f, 0.65f));
        hpTrail = Bar("HpTrail", hpBack, Vector2.zero, 16f, TrailColor);
        hpFill = Bar("HpFill", hpBack, Vector2.zero, 16f, HpColor);
        Stretch(hpTrail);
        Stretch(hpFill);

        RectTransform postureBack = Bar("PostureBack", root, new Vector2(0f, 2f), 7f, new Color(0f, 0f, 0f, 0.5f));
        postureFill = Rect("PostureFill", postureBack, new Vector2(0.5f, 0.5f), new Vector2(0f, 7f));
        postureFill.pivot = new Vector2(0.5f, 0.5f);
        postureImage = postureFill.gameObject.AddComponent<Image>();
        postureImage.raycastTarget = false;
        postureImage.color = PostureLow;
        root.gameObject.SetActive(false);
    }

    // 2페이즈 시작 위치 눈금 (보스마다 비율이 달라 등장 때 맞춤)
    void PlaceTick(float ratio)
    {
        Transform old = root.Find("PhaseTick");
        if (old != null) Destroy(old.gameObject);
        if (ratio <= 0f) return; // [변경] 2페이즈 없는 중간보스
        RectTransform tick = Rect("PhaseTick", root, new Vector2(0.5f, 0f), new Vector2(3f, 22f));
        tick.anchoredPosition = new Vector2(-Width * 0.5f + Width * ratio, 13f);
        var image = tick.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = new Color(1f, 1f, 1f, 0.8f);
    }

    void LateUpdate()
    {
        if (root == null) return;
        EnemySwordsman boss = EnemySwordsman.ActiveBoss;
        if (boss != null && boss != shown && boss.CanBeTargeted) Show(boss);
        if (shown == null) // 구역 재시작으로 보스가 사라짐
        {
            if (showing && !hiding) Hide(0f);
            return;
        }

        float hp = shown.Health01;
        SetFill(hpFill, hp);
        if (hp < lastHp) trailHoldUntil = Time.unscaledTime + 0.4f;
        lastHp = hp;
        // 잔상: 맞은 뒤 잠깐 멈췄다가 천천히 따라 내려옴
        if (hp < trail && Time.unscaledTime >= trailHoldUntil) trail = Mathf.MoveTowards(trail, hp, Time.unscaledDeltaTime * 0.45f);
        else if (hp > trail) trail = hp;
        SetFill(hpTrail, trail);

        float posture = shown.IsPostureBroken ? 1f : shown.Posture01;
        postureFill.sizeDelta = new Vector2(Width * posture, postureFill.sizeDelta.y);
        float blink = shown.IsPostureBroken ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 16f) : 0f;
        postureImage.color = Color.Lerp(Color.Lerp(PostureLow, PostureHigh, posture * posture), Color.white, blink);

        if (shown.IsPhase2 && !phaseShown)
        {
            phaseShown = true;
            phaseText.text = "분노";
            UITween.Punch(phaseText.transform, 0.4f, 0.4f);
        }

        if (!shown.CanBeTargeted && !hiding) Hide(1.5f);
    }

    void Show(EnemySwordsman boss)
    {
        shown = boss;
        showing = true;
        hiding = false;
        lastHp = 1f;
        phaseShown = false;
        trail = 1f;
        nameText.text = boss.BossName;
        phaseText.text = "";
        PlaceTick(boss.Phase2Ratio);
        root.gameObject.SetActive(true);
        group.DOKill();
        root.DOKill();
        group.alpha = 0f;
        root.anchoredPosition = new Vector2(0f, 70f);
        DOTween.To(() => group.alpha, a => group.alpha = a, 1f, 0.5f).SetUpdate(true).SetTarget(group).SetLink(root.gameObject);
        UITween.AnchorPos(root, new Vector2(0f, 92f), 0.6f, Ease.OutCubic);
    }

    void Hide(float delay)
    {
        hiding = true;
        group.DOKill();
        DOTween.To(() => group.alpha, a => group.alpha = a, 0f, 0.8f).SetDelay(delay).SetUpdate(true).SetTarget(group).SetLink(root.gameObject)
            .OnComplete(() =>
            {
                root.gameObject.SetActive(false);
                shown = null;
                showing = false;
            });
    }

    static void SetFill(RectTransform fill, float value) => fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    RectTransform Bar(string name, Transform parent, Vector2 pos, float height, Color color)
    {
        RectTransform rt = Rect(name, parent, new Vector2(0.5f, 0f), new Vector2(Width, height));
        rt.anchoredPosition = pos;
        var image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rt;
    }

    Text Label(string name, Font font, int size, Color color, TextAnchor anchor)
    {
        RectTransform rt = Rect(name, root, new Vector2(0.5f, 0f), new Vector2(Width * 0.5f, size * 1.4f));
        rt.pivot = anchor == TextAnchor.LowerLeft ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        var outline = rt.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = size;
        return rt;
    }
}
