using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 타이틀: 3D 무대(TitleStage) + "PRESS ANY KEY" 대문 + 키보드로 고르는 메뉴
// 흐름: 검은 화면에서 번개와 함께 로고가 베이듯 나타남 → 아무 키 → 카메라가 검객에게 다가가고 로고는 왼쪽 위, 메뉴는 왼쪽
//  - NEW GAME: 수련을 끝낸 적이 없으면 수련장, 있으면 "수련장부터 / 바로 산문" 선택
//  - TRAINING: 자유 연습장 (씬이 빌드에 있으면 보임)
//  - SETTINGS: 같은 씬의 SettingsMenu를 열고, 닫히면 메뉴로 돌아옴
//  - 메뉴: ↑↓/W·S/마우스로 고르고 Enter·Space·클릭으로 결정, ESC는 한 단계 뒤로 (맨 위에서는 대문 화면으로)
//  - 시작하면 검객이 발도 베기 → 화면을 가르는 섬광 → 장면 전환
// UI는 실행할 때 코드로 만듦. 씬·연결은 Tools/Setup/Title Scene
public class TitleScreen : MonoBehaviour
{
    public const string SceneName = "Title";

    [SerializeField] Font titleFont;
    [SerializeField] Font bodyFont;
    [SerializeField] Font logoFont; // 로고 · 메뉴용 세리프 (Cinzel)
    [SerializeField] Font subtitleFont; // 부제 "섬뢰" (Song Myung)
    [SerializeField] SettingsMenu settings;
    [SerializeField] GameObject characterSource; // Player 프리팹 (모델만 떼어 무대에 세움)
    [SerializeField] float characterYaw = 215f;
    [SerializeField] string idleState = "Skill_Stance";
    [SerializeField] string releaseState = "Skill_Release";
    [SerializeField] AudioClip bgm;
    [SerializeField] string tutorialScene = "Tutorial";
    [SerializeField] string mainScene = "InGame";
    [SerializeField] string trainingScene = "Training";
    [SerializeField] Color accent = new Color(0.95f, 0.8f, 0.45f);
    [SerializeField] Color bolt = new Color(0.62f, 0.82f, 1f);
    [SerializeField] Vector2 flashInterval = new Vector2(5f, 10f);

    const string Logo = "SEOMROE: Flash of Thunder";
    const string Subtitle = "섬  뢰";
    const float LogoWidth = 1600f;
    static readonly Vector2 LogoWide = new Vector2(960f, 360f);  // 대문: 화면 위쪽 가운데
    static readonly Vector2 LogoMenu = new Vector2(590f, 205f);  // 메뉴: 왼쪽 위 (줄어든 로고의 왼쪽 끝이 x≈150)
    const float LogoMenuScale = 0.6f;
    const float MenuX = 200f;

    enum Phase { Intro, Press, Menu, Leaving }

    class Item
    {
        public Text label, caption;
        public System.Action action;
        public float y, offset;
    }

    TitleStage stage;
    Phase phase;
    CanvasGroup group;
    RectTransform logoGroup, logoMask, slashEdge;
    Text logo, logoGlow, press, hint;
    Image flash, cover, side, cut, brush, diamond;
    GameObject menuRoot, choiceRoot;
    CanvasGroup menuGroup, choiceGroup;
    Image slashImage;
    int settingsClosedFrame = -1;
    readonly List<Item> menuItems = new List<Item>();
    readonly List<Item> choiceItems = new List<Item>();
    List<Item> items;
    int selected;
    float menuBlend, flashTimer, flashLevel, introTime, brushPop;
    bool settingsOpen;
    AudioSource music;

    void Start()
    {
        Time.timeScale = 1f;
        if (GamePause.IsPaused) GamePause.Set(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        UIKit.EnsureEventSystem();
        stage = gameObject.AddComponent<TitleStage>(); // 영상 대신 3D 무대
        stage.Build(characterSource, idleState, releaseState, characterYaw);
        Build();
        StartMusic();
        if (settings != null) settings.Closed += ShowMenu;
        items = menuItems;
        flashTimer = 99f; // 첫 번개는 인트로에서 직접 침
    }

    void OnDestroy()
    {
        AudioOptions.Changed -= ApplyVolume;
        if (settings != null) settings.Closed -= ShowMenu;
    }

    // ===== UI 만들기 =====
    void Build()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "TitleCanvas", 10);
        Transform root = canvas.transform;
        Font serif = logoFont != null ? logoFont : titleFont;

        // 메뉴 화면에서 글자가 잘 읽히게 왼쪽을 어둡게 (대문에서는 안 보임)
        side = UIKit.Image(root, UIKit.Gradient(256), new Color(0.01f, 0.012f, 0.02f, 0f));
        side.raycastTarget = false;
        side.rectTransform.anchorMin = Vector2.zero;
        side.rectTransform.anchorMax = new Vector2(0.58f, 1f);
        side.rectTransform.offsetMin = side.rectTransform.offsetMax = Vector2.zero;

        flash = FullImage(root, new Color(bolt.r, bolt.g, bolt.b, 0f));

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(root, false);
        UIKit.Stretch((RectTransform)content.transform);
        group = content.AddComponent<CanvasGroup>();
        Transform c = content.transform;

        BuildLogo(c, serif);

        press = UIKit.Text(c, "PRESS  ANY  KEY", serif, 26, new Color(1f, 1f, 1f, 0f), TextAnchor.MiddleCenter);
        Center(press.rectTransform, 960f, 965f, 800f, 40f);

        // 고른 항목 뒤 붓질 + 왼쪽 마름모
        brush = UIKit.Image(c, UIKit.BrushStroke(512, 96), new Color(accent.r, accent.g, accent.b, 0f)); // [변경] 붓질 생성은 설정 메뉴와 같이 쓰려고 UIKit으로 옮김
        brush.raycastTarget = false;
        UIKit.Place(brush.rectTransform, MenuX - 60f, 0f, 520f, 74f);
        diamond = UIKit.Image(c, null, new Color(accent.r, accent.g, accent.b, 0f));
        diamond.raycastTarget = false;
        UIKit.Place(diamond.rectTransform, MenuX - 34f, 0f, 10f, 10f);
        diamond.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        menuRoot = NewRoot(c, "Menu");
        menuGroup = menuRoot.AddComponent<CanvasGroup>();
        float y = 470f;
        AddItem(menuItems, menuRoot.transform, serif, "NEW GAME", "새로 시작", ref y, NewGame);
        if (Application.CanStreamedLevelBeLoaded(trainingScene)) AddItem(menuItems, menuRoot.transform, serif, "TRAINING", "자유 연습", ref y, () => Go(trainingScene));
        AddItem(menuItems, menuRoot.transform, serif, "SETTINGS", "환경 설정", ref y, OpenSettings);
        AddItem(menuItems, menuRoot.transform, serif, "QUIT", "종료", ref y, Quit);

        // 시작 위치 선택 (메뉴와 같은 자리에 바꿔 보여 줌)
        choiceRoot = NewRoot(c, "StartChoice");
        choiceGroup = choiceRoot.AddComponent<CanvasGroup>();
        Text ask = UIKit.Text(choiceRoot.transform, "어디서 시작할까요?", bodyFont, 22, accent, TextAnchor.MiddleLeft);
        UIKit.Place(ask.rectTransform, MenuX, 425f, 500f, 30f);
        y = 490f;
        AddItem(choiceItems, choiceRoot.transform, serif, "Tutorial", "수련장부터", ref y, () => Go(tutorialScene));
        AddItem(choiceItems, choiceRoot.transform, serif, "InGame", "바로 산문", ref y, () => Go(mainScene));
        y += 16f;
        AddItem(choiceItems, choiceRoot.transform, serif, "BACK", "뒤로", ref y, ShowMenu);
        choiceRoot.SetActive(false);

        hint = UIKit.Text(c, "↑↓  선택     ENTER  결정     ESC  뒤로", bodyFont, 18, new Color(1f, 1f, 1f, 0f), TextAnchor.MiddleRight);
        Corner(hint.rectTransform, -48f, 66f, 700f);
        Text version = UIKit.Text(c, "Portfolio Build · " + Application.version, bodyFont, 16, new Color(1f, 1f, 1f, 0.3f), TextAnchor.MiddleRight);
        Corner(version.rectTransform, -48f, 34f, 600f);

        // 시작할 때 화면을 가르는 섬광 + 맨 앞 검은 막(인트로)
        cut = FullImage(root, new Color(1f, 1f, 1f, 0f));
        cover = FullImage(root, Color.black);
    }

    // 로고 묶음: 가로로 넓어지는 마스크 안에 로고 · 장식선 · 부제 → 베이듯 왼쪽부터 드러남
    void BuildLogo(Transform parent, Font serif)
    {
        logoGroup = new GameObject("Logo", typeof(RectTransform)).GetComponent<RectTransform>();
        logoGroup.SetParent(parent, false);
        logoGroup.anchorMin = logoGroup.anchorMax = new Vector2(0f, 1f);
        logoGroup.pivot = new Vector2(0.5f, 0.5f);
        logoGroup.sizeDelta = new Vector2(LogoWidth, 300f);

        logoMask = new GameObject("Reveal", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        logoMask.SetParent(logoGroup, false);
        logoMask.anchorMin = new Vector2(0f, 0f);
        logoMask.anchorMax = new Vector2(0f, 1f);
        logoMask.pivot = new Vector2(0f, 0.5f);
        logoMask.anchoredPosition = Vector2.zero;
        logoMask.sizeDelta = new Vector2(0f, 0f);

        var inner = new GameObject("Inner", typeof(RectTransform)).GetComponent<RectTransform>();
        inner.SetParent(logoMask, false);
        inner.anchorMin = inner.anchorMax = new Vector2(0f, 0.5f);
        inner.pivot = new Vector2(0f, 0.5f);
        inner.anchoredPosition = Vector2.zero;
        inner.sizeDelta = new Vector2(LogoWidth, 300f);

        // 뒤에 같은 글자를 하나 더 깔아 번개 칠 때 번쩍이는 빛으로 사용
        logoGlow = UIKit.Text(inner, Logo, serif, 96, new Color(bolt.r, bolt.g, bolt.b, 0f), TextAnchor.MiddleCenter);
        InnerPlace(logoGlow.rectTransform, 40f, LogoWidth - 40f, 130f);
        FitOneLine(logoGlow);
        var glowOutline = logoGlow.gameObject.AddComponent<Outline>();
        glowOutline.effectDistance = new Vector2(5f, -5f);
        glowOutline.effectColor = new Color(bolt.r, bolt.g, bolt.b, 0.5f);

        logo = UIKit.Text(inner, Logo, serif, 96, new Color(0.95f, 0.95f, 0.97f), TextAnchor.MiddleCenter);
        InnerPlace(logo.rectTransform, 40f, LogoWidth - 40f, 130f);
        FitOneLine(logo);
        var shadow = logo.gameObject.AddComponent<Shadow>();
        shadow.effectDistance = new Vector2(0f, -4f);
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);

        // ── ◆ ── 장식선과 부제
        Color line = new Color(accent.r, accent.g, accent.b, 0.75f);
        InnerBar(inner, -150f, -45f, 260f, 2f, line, true);
        InnerBar(inner, 150f, -45f, 260f, 2f, line, false);
        Image gem = UIKit.Image(inner, null, accent);
        gem.raycastTarget = false;
        InnerPlace(gem.rectTransform, -45f, 10f, 10f);
        gem.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        Text sub = UIKit.Text(inner, Subtitle, subtitleFont != null ? subtitleFont : bodyFont, 44, new Color(0.92f, 0.92f, 0.95f), TextAnchor.MiddleCenter);
        InnerPlace(sub.rectTransform, -105f, 400f, 60f);
        sub.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -3f);

        // 드러나는 경계에 붙는 번개빛 칼자국
        slashImage = UIKit.Image(logoGroup, null, new Color(0.9f, 0.96f, 1f, 0f));
        slashImage.raycastTarget = false;
        slashEdge = slashImage.rectTransform;
        slashEdge.anchorMin = slashEdge.anchorMax = new Vector2(0f, 0.5f);
        slashEdge.pivot = new Vector2(0.5f, 0.5f);
        slashEdge.sizeDelta = new Vector2(5f, 280f);
        slashEdge.localRotation = Quaternion.Euler(0f, 0f, -18f);
    }

    void AddItem(List<Item> list, Transform parent, Font font, string label, string caption, ref float y, System.Action onSubmit)
    {
        var item = new Item { action = onSubmit, y = y };
        int index = list.Count;

        // 줄 전체가 마우스 판정 (투명 이미지)
        Image hit = UIKit.Image(parent, null, new Color(0f, 0f, 0f, 0f));
        UIKit.Place(hit.rectTransform, MenuX - 60f, y, 560f, 64f);
        var trigger = hit.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerEnter, () => Select(list, index));
        AddTrigger(trigger, EventTriggerType.PointerClick, () => { Select(list, index); Submit(); });

        item.label = UIKit.Text(parent, label, font, 32, new Color(1f, 1f, 1f, 0f), TextAnchor.MiddleLeft);
        UIKit.Place(item.label.rectTransform, MenuX, y, 300f, 56f);
        item.caption = UIKit.Text(parent, caption, bodyFont, 17, new Color(1f, 1f, 1f, 0f), TextAnchor.MiddleLeft);
        UIKit.Place(item.caption.rectTransform, MenuX + 290f, y + 2f, 200f, 30f);
        list.Add(item);
        y += 70f;
    }

    static void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    // ===== 메뉴 동작 =====
    void Select(List<Item> list, int index)
    {
        if (list != items || phase != Phase.Menu || index == selected) return;
        selected = index;
        brushPop = 0f;
    }

    void Submit()
    {
        if (phase != Phase.Menu || settingsOpen || selected < 0 || selected >= items.Count) return;
        items[selected].action();
    }

    void NewGame()
    {
        if (!TutorialDirector.CompletedBefore)
        {
            Go(tutorialScene);
            return;
        }
        menuRoot.SetActive(false); // 수련을 끝낸 적이 있으면 시작 위치를 고름
        choiceRoot.SetActive(true);
        items = choiceItems;
        selected = 1; // 기본은 바로 산문
        brushPop = 0f;
    }

    void ShowMenu()
    {
        if (phase == Phase.Leaving) return;
        if (settingsOpen) settingsClosedFrame = Time.frameCount; // 설정을 닫은 ESC가 메뉴에서 한 번 더 처리되지 않게
        settingsOpen = false;
        group.gameObject.SetActive(true); // 설정에서 돌아오면 로고·메뉴 다시 표시
        if (items == choiceItems) selected = 0;
        choiceRoot.SetActive(false);
        menuRoot.SetActive(true);
        items = menuItems;
        brushPop = 0f;
    }

    void OpenSettings()
    {
        if (settings == null) return;
        settingsOpen = true;
        group.gameObject.SetActive(false); // 설정 화면과 겹치지 않게 로고·메뉴(Content)를 숨김
        settings.Open();
    }

    void Go(string scene)
    {
        if (phase == Phase.Leaving) return;
        phase = Phase.Leaving;
        StartCoroutine(Leave(scene));
    }

// 시작 연출: 발도 베기 → 화면을 가르는 섬광 → 장면 전환(검게 닫힘)
    IEnumerator Leave(string scene)
    {
        stage.PlayRelease();
        StartCoroutine(FadeMusic());
        for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / 0.3f;
            yield return null;
        }
        group.alpha = 0f;
        flashLevel = 1f;
        stage.Strike();
        for (float t = 0f; t < 0.18f; t += Time.unscaledDeltaTime)
        {
            cut.color = new Color(1f, 1f, 1f, Mathf.Lerp(0f, 0.85f, t / 0.18f));
            yield return null;
        }
        SceneTransition.Load(scene, false); // 새로 시작 → 이전 판의 스킬·레벨을 이어받지 않음
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            cut.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.85f, 0f, t / 0.5f));
            yield return null;
        }
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ===== 매 프레임 =====
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        HandleInput();

        // 인트로: 검은 막이 걷히고 1.4초에 번개와 함께 로고가 베이듯 드러남
        introTime += dt;
        cover.color = new Color(0f, 0f, 0f, 1f - Mathf.SmoothStep(0f, 1f, introTime / 2.2f));
        if (phase == Phase.Intro && introTime >= 1.4f && flashTimer > 50f) StrikeNow();
        float reveal = Mathf.Clamp01((introTime - 1.4f) / 0.5f);
        float eased = 1f - Mathf.Pow(1f - reveal, 3f);
        logoMask.sizeDelta = new Vector2(LogoWidth * eased, 0f);
        slashEdge.anchoredPosition = new Vector2(LogoWidth * eased, 0f);
        slashImage.color = new Color(0.9f, 0.96f, 1f, reveal > 0f && reveal < 1f ? 1f : Mathf.MoveTowards(slashImage.color.a, 0f, dt * 4f));
        if (phase == Phase.Intro && introTime >= 2.6f) phase = Phase.Press;

        // 번개: 몇 초마다 화면 · 로고가 번쩍 (두 번 깜빡이는 느낌으로 한 번 더 얹음)
        flashTimer -= dt;
        if (flashTimer <= 0f) StrikeNow();
        flashLevel = Mathf.MoveTowards(flashLevel, 0f, dt * 2.4f);
        float f = flashLevel * flashLevel;
        stage.FlashLevel = f;
        flash.color = new Color(bolt.r, bolt.g, bolt.b, 0.1f * f);
        logoGlow.color = new Color(bolt.r, bolt.g, bolt.b, 0.85f * f);
        logo.color = Color.Lerp(new Color(0.95f, 0.95f, 0.97f), new Color(0.85f, 0.93f, 1f), f);

        // 대문 ↔ 메뉴: 하나의 값(menuBlend)으로 로고 위치 · 카메라 · 메뉴 투명도를 함께 움직임
        float target = phase == Phase.Menu || phase == Phase.Leaving ? 1f : 0f;
        menuBlend = Mathf.MoveTowards(menuBlend, target, dt / 0.9f);
        float k = Smoother(menuBlend);
        stage.MenuBlend = menuBlend;
        Vector2 pos = Vector2.Lerp(LogoWide, LogoMenu, k);
        logoGroup.anchoredPosition = new Vector2(pos.x, -pos.y);
        logoGroup.localScale = Vector3.one * Mathf.Lerp(1f, LogoMenuScale, k);
        side.color = new Color(side.color.r, side.color.g, side.color.b, 0.88f * k);

        float pressAlpha = phase == Phase.Press ? 0.45f + 0.4f * Mathf.Sin(Time.unscaledTime * 2.4f) : 0f;
        press.color = new Color(1f, 1f, 1f, Mathf.MoveTowards(press.color.a, Mathf.Max(0f, pressAlpha), dt * 3f));
        hint.color = new Color(1f, 1f, 1f, 0.45f * Mathf.Clamp01(menuBlend * 2f - 1f));
        bool clickable = phase == Phase.Menu && menuBlend > 0.6f; // 대문에서 누른 클릭이 보이지 않는 메뉴 항목을 누르지 않게
        menuGroup.blocksRaycasts = choiceGroup.blocksRaycasts = clickable;

        UpdateItems(dt);
    }

    void StrikeNow()
    {
        flashLevel = 1f;
        flashTimer = Random.Range(flashInterval.x, flashInterval.y);
        stage.Strike();
        StartCoroutine(SecondFlash());
    }

    // 항목이 위에서부터 차례로 나타나고, 고른 항목은 오른쪽으로 살짝 밀리며 밝아짐. 붓질 · 마름모가 따라감
    void UpdateItems(float dt)
    {
        List<Item> list = items;
        for (int i = 0; i < list.Count; i++)
        {
            Item it = list[i];
            float appear = Mathf.Clamp01(menuBlend * 1.6f - 0.4f - i * 0.12f);
            bool on = i == selected;
            it.offset = Mathf.Lerp(it.offset, on ? 16f : 0f, dt * 12f);
            float slide = (1f - Smoother(appear)) * -30f;
            UIKit.Place(it.label.rectTransform, MenuX + it.offset + slide, it.y, 300f, 56f);
            UIKit.Place(it.caption.rectTransform, MenuX + 290f + it.offset * 0.5f + slide, it.y + 2f, 200f, 30f);
            it.label.color = new Color(on ? 1f : 0.62f, on ? 1f : 0.64f, on ? 1f : 0.7f, (on ? 1f : 0.55f) * appear);
            it.caption.color = new Color(accent.r, accent.g, accent.b, (on ? 0.85f : 0.3f) * appear);
        }

        bool show = menuBlend > 0.5f && selected >= 0 && selected < list.Count;
        float markerAlpha = show ? Mathf.Clamp01(menuBlend * 2f - 1f) : 0f;
        if (show)
        {
            float y = list[selected].y;
            Vector2 bp = brush.rectTransform.anchoredPosition;
            brush.rectTransform.anchoredPosition = new Vector2(bp.x, Mathf.Lerp(bp.y, -y, dt * 14f));
            Vector2 dp = diamond.rectTransform.anchoredPosition;
            diamond.rectTransform.anchoredPosition = new Vector2(dp.x, Mathf.Lerp(dp.y, -y, dt * 18f));
        }
        brushPop = Mathf.MoveTowards(brushPop, 1f, dt * 4f);
        float pop = 1f - Mathf.Pow(1f - brushPop, 3f);
        brush.rectTransform.localScale = new Vector3(Mathf.Lerp(0.55f, 1f, pop), 1f, 1f); // 고를 때마다 붓질을 새로 긋는 느낌
        brush.color = new Color(accent.r, accent.g, accent.b, 0.26f * markerAlpha);
        diamond.color = new Color(accent.r, accent.g, accent.b, markerAlpha);
    }

    void HandleInput()
    {
        if (phase == Phase.Leaving || settingsOpen || Time.frameCount == settingsClosedFrame) return;
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;

        if (phase == Phase.Intro || phase == Phase.Press)
        {
            bool any = (kb != null && kb.anyKey.wasPressedThisFrame)
                || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
            if (!any) return;
            if (phase == Phase.Intro) SkipIntro();
            else EnterMenu();
            return;
        }

        bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (pad != null && pad.dpad.up.wasPressedThisFrame);
        bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (pad != null && pad.dpad.down.wasPressedThisFrame);
        bool submit = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);

        if (up || down)
        {
            int count = items.Count;
            selected = ((selected < 0 ? 0 : selected) + (down ? 1 : -1) + count) % count;
            brushPop = 0f;
        }
        else if (submit) Submit();
        else if (back)
        {
            if (items == choiceItems) ShowMenu();
            else phase = Phase.Press; // 맨 위 메뉴에서 ESC → 대문 화면
        }
    }

    void SkipIntro()
    {
        introTime = Mathf.Max(introTime, 2.6f);
        if (flashTimer > 50f) StrikeNow();
        phase = Phase.Press;
    }

    void EnterMenu()
    {
        phase = Phase.Menu;
        if (selected < 0 || selected >= items.Count) selected = 0;
        brushPop = 0f;
        float y = -items[selected].y; // 표시가 화면 위에서 미끄러져 내려오지 않게 바로 그 자리로
        brush.rectTransform.anchoredPosition = new Vector2(brush.rectTransform.anchoredPosition.x, y);
        diamond.rectTransform.anchoredPosition = new Vector2(diamond.rectTransform.anchoredPosition.x, y);
    }

    IEnumerator SecondFlash()
    {
        yield return new WaitForSecondsRealtime(0.12f);
        flashLevel = Mathf.Max(flashLevel, 0.75f);
    }

    void StartMusic()
    {
        if (bgm == null) return;
        music = gameObject.AddComponent<AudioSource>();
        music.clip = bgm;
        music.loop = true;
        music.spatialBlend = 0f;
        ApplyVolume();
        AudioOptions.Changed += ApplyVolume;
        music.Play();
    }

    void ApplyVolume()
    {
        if (music != null) music.volume = AudioOptions.BgmVolume;
    }

    IEnumerator FadeMusic()
    {
        if (music == null) yield break;
        float start = music.volume;
        for (float t = 0f; t < 0.7f; t += Time.unscaledDeltaTime)
        {
            music.volume = Mathf.Lerp(start, 0f, t / 0.7f);
            yield return null;
        }
    }

    // ===== 배치 도우미 =====
    static float Smoother(float x) => x * x * x * (x * (x * 6f - 15f) + 10f);

    static GameObject NewRoot(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        UIKit.Stretch((RectTransform)go.transform);
        return go;
    }

    static Image FullImage(Transform parent, Color color)
    {
        Image image = UIKit.Image(parent, null, color);
        image.raycastTarget = false;
        UIKit.Stretch(image.rectTransform);
        return image;
    }

    static void Center(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void Corner(RectTransform rect, float x, float y, float width)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, 30f);
    }

    // 로고 안쪽 좌표: 가로 가운데 기준, y는 위로 +
    static void InnerPlace(RectTransform rect, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(LogoWidth * 0.5f, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void InnerBar(Transform parent, float x, float y, float width, float height, Color color, bool fadeLeft)
    {
        Image bar = UIKit.Image(parent, UIKit.Gradient(128), color);
        bar.raycastTarget = false;
        RectTransform r = bar.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(LogoWidth * 0.5f + x, y);
        r.sizeDelta = new Vector2(width, height);
        if (!fadeLeft) r.localScale = new Vector3(-1f, 1f, 1f); // 그라데이션이 가운데(마름모) 쪽이 진하게
    }

    // 줄바꿈을 끄고, 글자 폭이 칸보다 넓으면 들어갈 때까지 크기를 줄임 (Best Fit은 줄바꿈부터 해 버려서 안 씀)
    static void FitOneLine(Text t)
    {
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        float width = t.rectTransform.rect.width;
        while (t.fontSize > 40 && t.preferredWidth > width) t.fontSize -= 2;
    }
}
