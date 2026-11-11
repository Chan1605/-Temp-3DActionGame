using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// HUD 스킨: 기존 HUD 오브젝트 위에 겉모습만 덧입힘 (둥근 막대 · 광택 · 테두리, 체력·뇌기 상징, Q·R 슬롯 아이콘)
// GameHUD가 움직이는 값(막대 길이 · 색 · 쿨타임)은 건드리지 않음. GameHUD가 있는 씬이면 실행 중 자동으로 붙음 (셋업 불필요)
public class HudSkin : MonoBehaviour
{
    const string Prefix = "Skin_"; // 스킨이 만든 오브젝트 이름 머리말

    static readonly Color FrameLight = new Color(1f, 1f, 1f, 0.3f);
    static readonly Color FrameThunder = new Color(0.45f, 0.85f, 1f, 0.65f);
    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);
    static readonly Color Gloss = new Color(1f, 1f, 1f, 0.22f);
    static readonly Color KeyCapColor = new Color(0.05f, 0.06f, 0.08f, 0.95f);

    Sprite barRound, barOutline, cellRound, cellOutline, slotRound, slotFull, slotOutline, circle, circleOutline;
    PlayerThunder thunder;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AttachOnStart()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Attach();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

    static void Attach()
    {
        foreach (GameHUD hud in FindObjectsByType<GameHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (hud.GetComponent<HudSkin>() == null) hud.gameObject.AddComponent<HudSkin>();
    }

    void Start()
    {
        barRound = UIKit.RoundRect(24, 24, 7f, 0f);
        barOutline = UIKit.RoundRect(24, 24, 8f, 1.5f);
        cellRound = UIKit.RoundRect(16, 16, 5f, 0f);
        cellOutline = UIKit.RoundRect(16, 16, 6f, 1.5f);
        slotRound = UIKit.RoundRect(64, 64, 20f, 0f);
        slotFull = UIKit.RoundRect(108, 108, 20f, 0f);
        slotOutline = UIKit.RoundRect(64, 64, 22f, 2.5f);
        circle = UIKit.RoundRect(48, 48, 24f, 0f);
        circleOutline = UIKit.RoundRect(48, 48, 24f, 2f);

        Apply();
        thunder = FindAnyObjectByType<PlayerThunder>();
        if (thunder != null) thunder.MaxGaugeChanged += OnMaxGaugeChanged;
    }

    void OnDestroy()
    {
        if (thunder != null) thunder.MaxGaugeChanged -= OnMaxGaugeChanged;
    }

    // 뇌기 칸이 늘면 GameHUD가 칸을 복제한 다음 프레임에 다시 입힘 (같은 이벤트라 순서 보장이 없어서)
    void OnMaxGaugeChanged(float max) => Invoke(nameof(Apply), 0f);

    // 핵심: HUD 계층을 이름으로 훑어 입힘. 이미 만든 스킨 오브젝트는 다시 쓰므로 여러 번 불러도 안전
    void Apply()
    {
        Transform status = transform.Find("PlayerStatus");
        if (status != null)
        {
            Transform hpBar = status.Find("HpBar");
            if (hpBar != null)
            {
                Bar(hpBar, barRound, barOutline, FrameLight);
                Emblem(hpBar, true, "HudHeart", 30f);
            }

            Transform firstCell = null;
            foreach (Transform child in status)
            {
                if (!child.name.StartsWith("Thunder_")) continue;
                Bar(child, cellRound, cellOutline, FrameThunder);
                if (firstCell == null || child.localPosition.x < firstCell.localPosition.x) firstCell = child;
            }
            foreach (Transform child in status) // 칸을 복제하면 상징도 따라 복제되므로 맨 왼쪽 칸에만 남김
                if (child.name.StartsWith("Thunder_")) Emblem(child, child == firstCell, "HudBolt", 26f);
        }

        Transform chargeBar = transform.Find("ChargeGauge/Bar");
        if (chargeBar != null) Bar(chargeBar, cellRound, cellOutline, FrameLight);

        Transform slot = transform.Find("SkillSlot");
        if (slot != null) Slot(slot);

        EnchantRing ring = GetComponentInChildren<EnchantRing>(true);
        if (ring != null) Ring(ring.transform);
    }

    // 막대 한 줄: 바탕 · 채움(Fill/Trail)을 둥글게, 채움 위 절반에 광택, 바깥 테두리
    void Bar(Transform bar, Sprite round, Sprite outline, Color frameColor)
    {
        SetSprite(bar.GetComponent<Image>(), round);
        var oldOutline = bar.GetComponent<Outline>();
        if (oldOutline != null) oldOutline.enabled = false;

        foreach (Transform child in bar)
        {
            if (child.name != "Fill" && child.name != "Trail") continue;
            SetSprite(child.GetComponent<Image>(), round);
            if (child.name != "Fill") continue;

            Image gloss = Child(child, "Gloss", out bool created);
            if (!created) continue;
            RectTransform rt = gloss.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(2f, 0f);
            rt.offsetMax = new Vector2(-2f, -1.5f);
            gloss.sprite = round;
            gloss.type = Image.Type.Sliced;
            gloss.color = Gloss;
        }

        Image frame = Child(bar, "Frame", out bool frameCreated);
        if (frameCreated)
        {
            RectTransform rt = frame.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-1.5f, -1.5f);
            rt.offsetMax = new Vector2(1.5f, 1.5f);
            frame.sprite = outline;
            frame.type = Image.Type.Sliced;
            frame.color = frameColor;
        }
        frame.transform.SetAsLastSibling();
    }

    // 막대 왼쪽 바깥의 작은 상징 (화면 가장자리 여백 안)
    void Emblem(Transform bar, bool show, string iconName, float size)
    {
        Sprite sprite = UiIcons.Get(iconName);
        Transform existing = bar.Find(Prefix + "Emblem");
        if (existing == null && (!show || sprite == null)) return;

        Image emblem = Child(bar, "Emblem", out bool created);
        if (created)
        {
            RectTransform rt = emblem.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-8f, 0f);
            rt.sizeDelta = new Vector2(size, size);
            emblem.preserveAspect = true;
            emblem.sprite = sprite;
        }
        emblem.gameObject.SetActive(show && sprite != null);
    }

    // Q 슬롯: 둥근 바탕 + 스킬 아이콘 + 금색 테두리. 글자 "일섬"은 숨기고, 쿨타임 가림막도 같은 모양으로
    void Slot(Transform slot)
    {
        SetSprite(slot.GetComponent<Image>(), slotRound);
        var oldOutline = slot.GetComponent<Outline>();
        if (oldOutline != null) oldOutline.enabled = false;

        Sprite iconSprite = UiIcons.Get("ThunderFlash");
        if (iconSprite != null)
        {
            Image icon = Child(slot, "Icon", out bool iconCreated);
            if (iconCreated)
            {
                RectTransform rt = icon.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(7f, 7f);
                rt.offsetMax = new Vector2(-7f, -7f);
                icon.sprite = iconSprite;
                icon.transform.SetAsFirstSibling();
            }

            Transform label = slot.Find("Label");
            if (label != null)
            {
                var group = label.GetComponent<CanvasGroup>();
                if (group == null) group = label.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f; // GameHUD가 글자 색을 계속 바꾸므로 색 대신 그룹 투명도로 숨김
            }
        }

        Transform cooldown = slot.Find("Cooldown");
        if (cooldown != null) cooldown.GetComponent<Image>().sprite = slotFull;

        Transform key = slot.Find("Key");
        Image frame = Child(slot, "Frame", out bool frameCreated);
        if (frameCreated)
        {
            RectTransform rt = frame.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-2f, -2f);
            rt.offsetMax = new Vector2(2f, 2f);
            frame.sprite = slotOutline;
            frame.type = Image.Type.Sliced;
            frame.color = Gold;
            if (key != null) frame.transform.SetSiblingIndex(key.GetSiblingIndex());
        }

        if (key != null) Bar(key, cellRound, cellOutline, Gold);
    }

    // R 링: 가운데에 각성 아이콘, R 글자는 왼쪽 아래 작은 키 표시로, 남은 초는 가운데 크게
    void Ring(Transform ring)
    {
        Sprite iconSprite = UiIcons.Get("Enchant");
        if (iconSprite == null || ring.Find(Prefix + "Icon") != null) return;
        float size = ((RectTransform)ring).sizeDelta.x;

        Image icon = Child(ring, "Icon", out _);
        RectTransform iconRt = icon.rectTransform;
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = Vector2.one * size * 0.58f;
        icon.sprite = iconSprite;
        icon.transform.SetSiblingIndex(Mathf.Min(1, ring.childCount - 1)); // 바탕 고리 바로 위, 남은 시간 고리·글자 아래

        Transform key = ring.Find("Key");
        Vector2 corner = new Vector2(-size * 0.36f, -size * 0.36f);
        Image cap = Child(ring, "KeyCap", out _);
        RectTransform capRt = cap.rectTransform;
        capRt.anchorMin = capRt.anchorMax = new Vector2(0.5f, 0.5f);
        capRt.sizeDelta = Vector2.one * size * 0.32f;
        capRt.anchoredPosition = corner;
        cap.sprite = circle;
        cap.color = KeyCapColor;
        if (key != null) cap.transform.SetSiblingIndex(key.GetSiblingIndex());

        Image capFrame = Child(cap.transform, "Frame", out _);
        RectTransform frameRt = capFrame.rectTransform;
        frameRt.anchorMin = Vector2.zero;
        frameRt.anchorMax = Vector2.one;
        frameRt.offsetMin = frameRt.offsetMax = Vector2.zero;
        capFrame.sprite = circleOutline;
        capFrame.color = Gold;

        MoveText(key, corner, Mathf.RoundToInt(size * 0.2f));
        MoveText(ring.Find("Seconds"), Vector2.zero, Mathf.RoundToInt(size * 0.3f));
    }

    static void MoveText(Transform target, Vector2 position, int fontSize)
    {
        Text text = target != null ? target.GetComponent<Text>() : null;
        if (text == null) return;
        text.rectTransform.anchoredPosition = position;
        text.fontSize = fontSize;
    }

    static void SetSprite(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
    }

    static Image Child(Transform parent, string name, out bool created)
    {
        Transform existing = parent.Find(Prefix + name);
        created = existing == null;
        if (!created) return existing.GetComponent<Image>();

        var go = new GameObject(Prefix + name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }
}
