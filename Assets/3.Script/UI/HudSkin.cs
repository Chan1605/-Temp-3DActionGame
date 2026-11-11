using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// HUD 개선 스킨. 전/후 전환: ESC > GAMEPLAY > Enhanced UI (GameSettings.EnhancedUi)
// 기존 HUD 오브젝트 위에 겉모습만 덧입힘: 둥근 막대 · 광택 · 테두리, 체력·뇌기 상징, Q·R 슬롯 아이콘
// GameHUD가 움직이는 값(막대 길이 · 색 · 쿨타임)은 건드리지 않아서 끄면 예전 모습 그대로 돌아감
// GameHUD가 있는 씬이면 실행 중 자동으로 붙음 (셋업 불필요)
public class HudSkin : MonoBehaviour
{
    const string Prefix = "Skin_"; // 스킨이 만든 오브젝트 이름 머리말

    static readonly Color FrameLight = new Color(1f, 1f, 1f, 0.3f);
    static readonly Color FrameThunder = new Color(0.45f, 0.85f, 1f, 0.65f);
    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);
    static readonly Color Gloss = new Color(1f, 1f, 1f, 0.22f);
    static readonly Color KeyCapColor = new Color(0.05f, 0.06f, 0.08f, 0.95f);

    struct ImageState { public Sprite Sprite; public Image.Type Type; }
    struct TextState { public Vector2 Position; public int FontSize; }

    readonly Dictionary<Image, ImageState> imageStates = new Dictionary<Image, ImageState>();
    readonly Dictionary<Text, TextState> textStates = new Dictionary<Text, TextState>();
    readonly HashSet<Sprite> skinSprites = new HashSet<Sprite>();

    Sprite barRound, barOutline, cellRound, cellOutline, slotRound, slotFull, slotOutline, circle, circleOutline;
    bool built, applied;

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

    void OnEnable()
    {
        GameSettings.Changed += Sync;
        if (built) Sync();
    }

    void OnDisable() => GameSettings.Changed -= Sync;

    void Start()
    {
        barRound = Make(UIKit.RoundRect(24, 24, 7f, 0f));
        barOutline = Make(UIKit.RoundRect(24, 24, 8f, 1.5f));
        cellRound = Make(UIKit.RoundRect(16, 16, 5f, 0f));
        cellOutline = Make(UIKit.RoundRect(16, 16, 6f, 1.5f));
        slotRound = Make(UIKit.RoundRect(64, 64, 20f, 0f));
        slotFull = Make(UIKit.RoundRect(108, 108, 20f, 0f));
        slotOutline = Make(UIKit.RoundRect(64, 64, 22f, 2.5f));
        circle = Make(UIKit.RoundRect(48, 48, 24f, 0f));
        circleOutline = Make(UIKit.RoundRect(48, 48, 24f, 2f));
        built = true;
        Apply(GameSettings.EnhancedUi);
    }

    Sprite Make(Sprite sprite)
    {
        skinSprites.Add(sprite);
        return sprite;
    }

    void Sync()
    {
        if (built && applied != GameSettings.EnhancedUi) Apply(GameSettings.EnhancedUi);
    }

    // 핵심: 켤 때마다 HUD 계층을 이름으로 다시 훑어 입힘 → 중간에 늘어난 뇌기 칸도 같이 처리, 여러 번 불러도 안전
    void Apply(bool on)
    {
        applied = on;

        Transform status = transform.Find("PlayerStatus");
        if (status != null)
        {
            Transform hpBar = status.Find("HpBar");
            if (hpBar != null)
            {
                Bar(hpBar, on, barRound, barOutline, FrameLight);
                Emblem(hpBar, on, "HudHeart", 30f);
            }

            Transform firstCell = null;
            foreach (Transform child in status)
            {
                if (!child.name.StartsWith("Thunder_")) continue;
                Bar(child, on, cellRound, cellOutline, FrameThunder);
                if (firstCell == null || child.localPosition.x < firstCell.localPosition.x) firstCell = child;
            }
            foreach (Transform child in status) // 칸을 복제하면 상징도 따라 복제되므로 맨 왼쪽 칸에만 남김
                if (child.name.StartsWith("Thunder_")) Emblem(child, on && child == firstCell, "HudBolt", 26f);
        }

        Transform chargeBar = transform.Find("ChargeGauge/Bar");
        if (chargeBar != null) Bar(chargeBar, on, cellRound, cellOutline, FrameLight);

        Transform slot = transform.Find("SkillSlot");
        if (slot != null) Slot(slot, on);

        EnchantRing ring = GetComponentInChildren<EnchantRing>(true);
        if (ring != null) Ring(ring.transform, on);
    }

    // 막대 한 줄: 바탕 · 채움(Fill/Trail)을 둥글게, 채움 위 절반에 광택, 바깥 테두리
    void Bar(Transform bar, bool on, Sprite round, Sprite outline, Color frameColor)
    {
        SetSprite(bar.GetComponent<Image>(), on, round, true);
        var oldOutline = bar.GetComponent<Outline>();
        if (oldOutline != null) oldOutline.enabled = !on;

        foreach (Transform child in bar)
        {
            if (child.name != "Fill" && child.name != "Trail") continue;
            SetSprite(child.GetComponent<Image>(), on, round, true);
            if (child.name != "Fill") continue;

            Image gloss = Child(child, "Gloss", out bool created);
            if (created)
            {
                RectTransform rt = gloss.rectTransform;
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(2f, 0f);
                rt.offsetMax = new Vector2(-2f, -1.5f);
                gloss.sprite = round;
                gloss.type = Image.Type.Sliced;
                gloss.color = Gloss;
            }
            gloss.gameObject.SetActive(on);
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
        frame.gameObject.SetActive(on);
    }

    // 막대 왼쪽 바깥의 작은 상징 (화면 가장자리 여백 안)
    void Emblem(Transform bar, bool on, string iconName, float size)
    {
        Sprite sprite = on ? UiIcons.Get(iconName) : null;
        Transform existing = bar.Find(Prefix + "Emblem");
        if (existing == null && sprite == null) return;

        Image emblem = Child(bar, "Emblem", out bool created);
        if (created)
        {
            RectTransform rt = emblem.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-8f, 0f);
            rt.sizeDelta = new Vector2(size, size);
            emblem.preserveAspect = true;
        }
        if (sprite != null) emblem.sprite = sprite;
        emblem.gameObject.SetActive(on && sprite != null);
    }

    // Q 슬롯: 둥근 바탕 + 스킬 아이콘 + 금색 테두리. 글자 "일섬"은 숨기고, 쿨타임 가림막도 같은 모양으로
    void Slot(Transform slot, bool on)
    {
        SetSprite(slot.GetComponent<Image>(), on, slotRound, true);
        var oldOutline = slot.GetComponent<Outline>();
        if (oldOutline != null) oldOutline.enabled = !on;

        Sprite iconSprite = UiIcons.Get("ThunderFlash");
        bool showIcon = on && iconSprite != null;
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
        icon.gameObject.SetActive(showIcon);

        Transform label = slot.Find("Label");
        if (label != null)
        {
            var group = label.GetComponent<CanvasGroup>();
            if (group == null) group = label.gameObject.AddComponent<CanvasGroup>();
            group.alpha = showIcon ? 0f : 1f; // GameHUD가 글자 색을 계속 바꾸므로 색 대신 그룹 투명도로 숨김
        }

        Transform cooldown = slot.Find("Cooldown");
        if (cooldown != null) SetSprite(cooldown.GetComponent<Image>(), on, slotFull, false);

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
        frame.gameObject.SetActive(on);

        if (key != null) Bar(key, on, cellRound, cellOutline, Gold);
    }

    // R 링: 가운데에 각성 아이콘, R 글자는 왼쪽 아래 작은 키 표시로, 남은 초는 가운데 크게
    void Ring(Transform ring, bool on)
    {
        float size = ((RectTransform)ring).sizeDelta.x;
        Sprite iconSprite = UiIcons.Get("Enchant");
        bool showIcon = on && iconSprite != null;

        Image icon = Child(ring, "Icon", out bool iconCreated);
        if (iconCreated)
        {
            RectTransform rt = icon.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.one * size * 0.58f;
            icon.sprite = iconSprite;
            icon.transform.SetSiblingIndex(Mathf.Min(1, ring.childCount - 1)); // 바탕 고리 바로 위, 남은 시간 고리·글자 아래
        }
        icon.gameObject.SetActive(showIcon);

        Transform key = ring.Find("Key");
        Vector2 corner = new Vector2(-size * 0.36f, -size * 0.36f);
        Image cap = Child(ring, "KeyCap", out bool capCreated);
        if (capCreated)
        {
            RectTransform rt = cap.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.one * size * 0.32f;
            rt.anchoredPosition = corner;
            cap.sprite = circle;
            cap.color = KeyCapColor;

            Image capFrame = Child(cap.transform, "Frame", out bool capFrameCreated);
            RectTransform frameRt = capFrame.rectTransform;
            frameRt.anchorMin = Vector2.zero;
            frameRt.anchorMax = Vector2.one;
            frameRt.offsetMin = frameRt.offsetMax = Vector2.zero;
            capFrame.sprite = circleOutline;
            capFrame.color = Gold;
        }
        if (key != null) cap.transform.SetSiblingIndex(key.GetSiblingIndex());
        cap.gameObject.SetActive(showIcon);

        MoveText(key, showIcon, corner, Mathf.RoundToInt(size * 0.2f));
        MoveText(ring.Find("Seconds"), showIcon, Vector2.zero, Mathf.RoundToInt(size * 0.3f));
    }

    void MoveText(Transform target, bool on, Vector2 position, int fontSize)
    {
        Text text = target != null ? target.GetComponent<Text>() : null;
        if (text == null) return;
        if (!textStates.ContainsKey(text))
            textStates[text] = new TextState { Position = text.rectTransform.anchoredPosition, FontSize = text.fontSize };

        TextState original = textStates[text];
        text.rectTransform.anchoredPosition = on ? position : original.Position;
        text.fontSize = on ? fontSize : original.FontSize;
    }

    // 스프라이트만 갈아 끼움. 끌 땐 원래 스프라이트로 (칸이 복제돼 기록이 없으면 HUD 기본값 = 스프라이트 없음)
    void SetSprite(Image image, bool on, Sprite skin, bool sliced)
    {
        if (image == null) return;
        if (on)
        {
            if (!imageStates.ContainsKey(image))
                imageStates[image] = skinSprites.Contains(image.sprite)
                    ? new ImageState { Sprite = null, Type = sliced ? Image.Type.Simple : image.type }
                    : new ImageState { Sprite = image.sprite, Type = image.type };
            image.sprite = skin;
            if (sliced) image.type = Image.Type.Sliced;
        }
        else if (imageStates.TryGetValue(image, out ImageState original))
        {
            image.sprite = original.Sprite;
            image.type = original.Type;
        }
        else if (skinSprites.Contains(image.sprite))
        {
            image.sprite = null;
            if (sliced) image.type = Image.Type.Simple;
        }
    }

    Image Child(Transform parent, string name, out bool created)
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
