using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// ESC 설정 패널: 왼쪽 분류(DISPLAY·GAMEPLAY·GRAPHICS·AUDIO), 오른쪽 항목, 아래 Back · Apply/Restore Defaults
// [변경] 타이틀과 같은 모양으로: 세리프 글꼴(Cinzel) · 금빛 장식선과 마름모 · 고른 분류 뒤 붓질 · 한글 캡션 · 열릴 때 페이드
//  - 인게임: ESC로 열면 일시정지(GamePause) + 커서 표시, Back/ESC로 한 단계씩 닫고 마지막에 재개
//  - 화면 항목은 Apply를 눌러야 적용, 나머지는 바꾸는 즉시 적용 (GameSettings · AudioOptions)
//  - UI는 실행할 때 코드로 생성 (1920x1080 기준 좌표). 배치·연결은 Tools/Setup/Settings Menu
// 타이틀 화면에서도 같은 컴포넌트를 쓰도록 inGame=false면 일시정지·ESC 열기를 하지 않음
[DefaultExecutionOrder(10000)]
public class SettingsMenu : MonoBehaviour
{
    [SerializeField] Font titleFont;   // 예전 분류·버튼 글꼴 (displayFont가 없을 때 대신 씀)
    [SerializeField] Font bodyFont;    // 항목 이름·값 · 한글 캡션
    [SerializeField] Font displayFont; // [변경] 머리글·분류·버튼 (타이틀 로고와 같은 Cinzel)
    [SerializeField] bool inGame = true;
    [SerializeField] Color accent = new Color(0.95f, 0.8f, 0.45f, 1f);
    [SerializeField] Color dimText = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField, Range(0f, 1f)] float backdropAlpha = 0.72f;

    public bool IsOpen => root != null && root.activeSelf;
    public Font TitleFont => titleFont; // 결과 화면이 같은 글꼴을 씀
    public Font BodyFont => bodyFont;
    public event Action Closed; // 타이틀 화면에서 돌아갈 때 사용

    enum Page { Root, Display, Gameplay, Graphics, Audio, Quit }

    static readonly string[] CategoryNames = { "DISPLAY", "GAMEPLAY", "GRAPHICS", "AUDIO" };
    static readonly string[] CategoryCaptions = { "화면", "조작", "그래픽", "소리" };

    GameObject root;
    CanvasGroup rootGroup;
    GameObject menuGroup;
    RectTransform content;
    Text headerSection, headerCaption;
    Image headerLine;
    Text rightButtonText;
    Button rightButton;
    readonly List<Action> refreshers = new List<Action>();
    Page page = Page.Root;
    bool resumeRequested;

    // Display 페이지에서 Apply 전까지 들고 있는 값
    int pendingResolution, pendingFps;
    bool pendingWindowed, pendingVSync;

    Sprite circleSprite, pillSprite, pillOutlineSprite, gradientSprite, brushSprite;
    Text settingsTitle, backText, backKey;
    Text skipTutorialText; // 튜토리얼 씬에서만 보임
    Image bottomLine, categoryBrush, categoryDiamond;
    readonly List<Button> categoryButtons = new List<Button>();
    readonly List<Text> categoryCaptionTexts = new List<Text>();

    Font Display => displayFont != null ? displayFont : titleFont;

    void Awake()
    {
        circleSprite = MakeCircle(64);
        pillSprite = MakePill(64, 32, 0f);
        pillOutlineSprite = MakePill(64, 32, 2f);
        gradientSprite = MakeGradient(256);
        brushSprite = UIKit.BrushStroke(512, 96);
        EnsureEventSystem();
        Build();
        root.SetActive(false);
    }

    void OnDisable()
    {
        if (inGame && GamePause.IsPaused) GamePause.Set(false);
        MenuOverlay.Hide(this);
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
        if (!IsOpen)
        {
            // 스킬트리가 열려 있으면 ESC는 스킬트리가 처리. 궁극기 연출 중엔 열지 않음 (연출은 멈추지 않고 진행돼서)
            if (inGame && !ResultScreen.IsShowing && !PlayerUltimate.AnyActive && (SkillTreeMenu.Instance == null || !SkillTreeMenu.Instance.IsOpen)) Open();
        }
        else Back();
    }

    void LateUpdate()
    {
        // 재개는 프레임 끝에서 → 버튼을 누른 클릭이 같은 프레임에 공격 입력으로 들어가지 않음
        if (resumeRequested)
        {
            resumeRequested = false;
            if (inGame)
            {
                GamePause.Set(false);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        if (inGame && IsOpen) Time.timeScale = 0f; // 멈춘 사이 다른 스크립트가 timeScale을 바꿔도 다시 멈춤
        if (IsOpen && rootGroup.alpha < 1f) rootGroup.alpha = Mathf.MoveTowards(rootGroup.alpha, 1f, Time.unscaledDeltaTime / 0.18f); // [변경] 열릴 때 페이드 인
    }

    public void Open()
    {
        root.SetActive(true);
        rootGroup.alpha = 0f;
        ShowPage(Page.Root);
        if (skipTutorialText != null) // 진행 중인 튜토리얼이 있을 때만
            skipTutorialText.gameObject.SetActive(TutorialDirector.Instance != null && !TutorialDirector.Instance.Finished);
        if (inGame) GamePause.Set(true);
        if (inGame) MenuOverlay.Show(this, root.GetComponent<Canvas>()); // 열린 동안 HUD 등 다른 UI를 숨김
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void SkipTutorial() // 남은 단계를 건너뛰고 모든 결계를 엶
    {
        if (TutorialDirector.Instance != null) TutorialDirector.Instance.SkipAll();
        Close();
    }

    public void Close()
    {
        root.SetActive(false);
        MenuOverlay.Hide(this);
        resumeRequested = true;
        Closed?.Invoke();
    }

    void Back()
    {
        if (page == Page.Root) Close();
        else ShowPage(Page.Root);
    }

    // ===== 페이지 =====
    void ShowPage(Page next)
    {
        page = next;
        refreshers.Clear();
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);

        bool quit = next == Page.Quit;
        bool section = next != Page.Root && !quit;
        menuGroup.SetActive(!quit);
        headerSection.gameObject.SetActive(section);
        headerCaption.gameObject.SetActive(section);
        headerSection.text = section ? CategoryNames[(int)next - 1] : "";
        headerCaption.text = section ? CategoryCaptions[(int)next - 1] : "";

        // [변경] 고른 분류: 밑줄 대신 타이틀 메뉴처럼 붓질 + 마름모, 캡션도 밝게
        int selected = (int)next - 1;
        for (int i = 0; i < categoryButtons.Count; i++)
        {
            bool on = selected == i;
            SetBright(categoryButtons[i], on);
            categoryCaptionTexts[i].color = new Color(accent.r, accent.g, accent.b, on ? 0.9f : 0.35f);
        }
        categoryBrush.gameObject.SetActive(section);
        categoryDiamond.gameObject.SetActive(section);
        if (section)
        {
            float y = CategoryY(selected);
            Place(categoryBrush.rectTransform, 110f, y, 470f, 70f);
            SetCenter(categoryDiamond.rectTransform, 140f, y);
        }

        settingsTitle.gameObject.SetActive(!quit);
        backText.gameObject.SetActive(!quit);
        backKey.gameObject.SetActive(!quit);
        bottomLine.gameObject.SetActive(!quit);

        rightButton.gameObject.SetActive(next == Page.Root || next == Page.Display);
        rightButtonText.text = next == Page.Display ? "APPLY" : "RESTORE DEFAULTS";

        switch (next)
        {
            case Page.Display: BuildDisplay(); break;
            case Page.Gameplay: BuildGameplay(); break;
            case Page.Graphics: BuildGraphics(); break;
            case Page.Audio: BuildAudio(); break;
            case Page.Quit: BuildQuit(); break;
        }
        RefreshAll();
        LayoutHeaderLine();
    }

    void BuildDisplay()
    {
        pendingResolution = GameSettings.ResolutionIndex;
        pendingWindowed = GameSettings.Windowed;
        pendingVSync = GameSettings.VSync;
        pendingFps = GameSettings.FpsIndex;

        int row = 0;
        AddSelector(row++, "Resolution", () => { Vector2Int r = GameSettings.Resolutions[pendingResolution]; return $"{r.x}x{r.y}"; },
            d => pendingResolution = Mathf.Clamp(pendingResolution + d, 0, GameSettings.Resolutions.Count - 1));
        AddSelector(row++, "VSync", () => pendingVSync ? "On" : "Off", d => pendingVSync = !pendingVSync);
        AddSelector(row++, "Max Fps", () => GameSettings.FpsOptions[pendingFps] < 0 ? "Unlimited" : GameSettings.FpsOptions[pendingFps].ToString(),
            d => pendingFps = Mathf.Clamp(pendingFps + d, 0, GameSettings.FpsOptions.Length - 1));
        AddToggle(row++, "Window Mode", () => pendingWindowed, v => pendingWindowed = v);
        AddSlider(row++, "Brightness", 0.5f, 1.5f, () => GameSettings.Brightness, v => GameSettings.Brightness = v, v => v.ToString("0.0"));
    }

    void BuildGameplay()
    {
        int row = 0;
        AddSlider(row++, "Mouse Sensitivity", 1f, 100f, () => GameSettings.Sensitivity, v => GameSettings.Sensitivity = Mathf.Round(v), v => Mathf.RoundToInt(v).ToString());
        AddToggle(row++, "Damage Numbers", () => GameSettings.ShowDamageNumbers, v => GameSettings.ShowDamageNumbers = v);
    }

    void BuildGraphics()
    {
        int row = 0;
        AddSelector(row++, "Anti-Aliasing", () => GameSettings.AAOptions[GameSettings.AntiAliasing],
            d => GameSettings.AntiAliasing = (GameSettings.AntiAliasing + d + GameSettings.AAOptions.Length) % GameSettings.AAOptions.Length);
        AddToggle(row++, "Shadows", () => GameSettings.Shadows, v => GameSettings.Shadows = v);
    }

    void BuildAudio()
    {
        int row = 0;
        AddSlider(row++, "Main Volume", 0f, 1f, () => AudioOptions.Master, v => AudioOptions.Master = v, null);
        AddSlider(row++, "Music Volume", 0f, 1f, () => AudioOptions.Bgm, v => AudioOptions.Bgm = v, null);
        AddSlider(row++, "Effects Volume", 0f, 1f, () => AudioOptions.Sfx, v => AudioOptions.Sfx = v, null);
    }

    void BuildQuit()
    {
        // [변경] 타이틀 로고 묶음처럼: 세리프 큰 글씨 + 금빛 장식선 + 한글 캡션
        Text question = MakeText(content, "LEAVE THE GAME?", Display, 44, Color.white, TextAnchor.MiddleLeft);
        Place(question.rectTransform, 170f, 430f, 900f, 60f);
        Ornament(content, 170f, 478f, 360f);
        Text caption = MakeText(content, "게임을 떠나시겠습니까?", bodyFont, 20, new Color(accent.r, accent.g, accent.b, 0.85f), TextAnchor.MiddleLeft);
        Place(caption.rectTransform, 172f, 512f, 600f, 30f);

        float y = 590f;
        // 게임 중이고 타이틀 씬이 빌드에 있으면 타이틀로 돌아가기 (진행 상태는 버림)
        if (inGame && Application.CanStreamedLevelBeLoaded(TitleScreen.SceneName))
        {
            AddTextButton(content, "MAIN MENU", 30, 170f, y, 340f, () => SceneTransition.Load(TitleScreen.SceneName, false));
            y += 62f;
        }
        AddTextButton(content, "LEAVE", 30, 170f, y, 340f, () =>
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });
        AddTextButton(content, "BACK", 30, 170f, y + 62f, 340f, () => ShowPage(Page.Root));
    }

    void OnRightButton()
    {
        if (page == Page.Display)
        {
            GameSettings.SetDisplay(pendingResolution, pendingWindowed, pendingVSync, pendingFps);
            RefreshAll();
        }
        else if (page == Page.Root) GameSettings.RestoreDefaults();
    }

    void RefreshAll()
    {
        foreach (Action refresh in refreshers) refresh();
    }

    // ===== 항목 =====
    const float LabelX = 662f, ValueX = 1283f, RowY0 = 390f, RowStep = 64f;

    Text AddLabel(int row, string label)
    {
        Text text = MakeText(content, label, bodyFont, 26, dimText, TextAnchor.MiddleLeft);
        Place(text.rectTransform, LabelX, RowY0 + row * RowStep, 480f, 50f);
        return text;
    }

    // ‹ 값 › 선택
    void AddSelector(int row, string label, Func<string> value, Action<int> change)
    {
        Text name = AddLabel(row, label);
        float y = RowY0 + row * RowStep;
        Text valueText = MakeText(content, "", bodyFont, 24, Color.white, TextAnchor.MiddleCenter);
        Place(valueText.rectTransform, ValueX - 90f, y, 180f, 50f);
        Text left = AddTextButton(content, "‹", 40, ValueX - 118f, y - 3f, 40f, () => { change(-1); RefreshAll(); }, TextAnchor.MiddleCenter);
        Text right = AddTextButton(content, "›", 40, ValueX + 80f, y - 3f, 40f, () => { change(1); RefreshAll(); }, TextAnchor.MiddleCenter);
        left.font = right.font = bodyFont; // Cinzel에는 ‹ › 글자가 없음
        refreshers.Add(() => valueText.text = value());
        AddRowHover(name, y);
    }

    // ON/OFF 알약 토글 (켜지면 금빛)
    void AddToggle(int row, string label, Func<bool> value, Action<bool> change)
    {
        Text name = AddLabel(row, label);
        float y = RowY0 + row * RowStep;
        Text state = MakeText(content, "", Display, 20, Color.white, TextAnchor.MiddleRight);
        Place(state.rectTransform, ValueX - 140f, y, 90f, 50f);

        Image pill = MakeImage(content, pillSprite, new Color(1f, 1f, 1f, 0.15f));
        pill.type = Image.Type.Sliced;
        Place(pill.rectTransform, ValueX + 5f, y, 60f, 30f);
        Image outline = MakeImage(pill.rectTransform, pillOutlineSprite, new Color(1f, 1f, 1f, 0.7f));
        outline.type = Image.Type.Sliced;
        outline.raycastTarget = false;
        Stretch(outline.rectTransform);
        Image knob = MakeImage(pill.rectTransform, circleSprite, Color.white);
        var knobRect = knob.rectTransform;
        knobRect.anchorMin = knobRect.anchorMax = new Vector2(0f, 0.5f);
        knobRect.sizeDelta = new Vector2(22f, 22f);

        Button button = pill.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { change(!value()); RefreshAll(); });

        refreshers.Add(() =>
        {
            bool on = value();
            state.text = on ? "ON" : "OFF";
            state.color = on ? accent : dimText;
            knobRect.anchoredPosition = new Vector2(on ? 45f : 15f, 0f);
            pill.color = on ? new Color(accent.r, accent.g, accent.b, 0.35f) : new Color(1f, 1f, 1f, 0.04f);
            outline.color = on ? accent : new Color(1f, 1f, 1f, 0.45f);
            knob.color = on ? Color.white : new Color(1f, 1f, 1f, 0.5f);
        });
        AddRowHover(name, y);
    }

    // 숫자 + 가는 막대 슬라이더 (금빛 채움 + 마름모 손잡이)
    void AddSlider(int row, string label, float min, float max, Func<float> value, Action<float> change, Func<float, string> format)
    {
        Text name = AddLabel(row, label);
        float y = RowY0 + row * RowStep;

        Text number = null;
        if (format != null)
        {
            number = MakeText(content, "", bodyFont, 22, Color.white, TextAnchor.MiddleRight);
            Place(number.rectTransform, ValueX - 150f, y, 80f, 50f);
        }

        var go = new GameObject(label + " Slider", typeof(RectTransform));
        go.transform.SetParent(content, false);
        Place((RectTransform)go.transform, ValueX - 40f, y, 156f, 24f);
        Stretch(MakeImage(go.transform, null, new Color(0f, 0f, 0f, 0f)).rectTransform); // 클릭 영역 (가는 막대만으로는 잡기 어려움)
        Slider slider = go.AddComponent<Slider>();

        Image track = MakeImage(go.transform, null, new Color(1f, 1f, 1f, 0.16f));
        var trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0.5f);
        trackRect.anchorMax = new Vector2(1f, 0.5f);
        trackRect.sizeDelta = new Vector2(0f, 2f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.sizeDelta = new Vector2(0f, 3f);
        Image fill = MakeImage(fillArea.transform, null, new Color(accent.r, accent.g, accent.b, 0.9f));
        Stretch(fill.rectTransform); // 가로는 Slider가 값에 맞춰 바꿈

        var handleArea = new GameObject("Handle Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        Stretch((RectTransform)handleArea.transform);
        Image handle = MakeImage(handleArea.transform, null, Color.white);
        handle.rectTransform.sizeDelta = new Vector2(13f, 13f);
        handle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = min;
        slider.maxValue = max;
        slider.transition = Selectable.Transition.None;
        slider.SetValueWithoutNotify(value());
        slider.onValueChanged.AddListener(v =>
        {
            change(v);
            if (number != null) number.text = format(value());
        });

        refreshers.Add(() =>
        {
            slider.SetValueWithoutNotify(value());
            if (number != null) number.text = format(value());
        });
        AddRowHover(name, y);
    }

    // 줄 위에 마우스가 오면 항목 이름이 밝아지고 뒤에 옅은 붓질
    void AddRowHover(Text name, float y)
    {
        Image hit = MakeImage(content, null, new Color(0f, 0f, 0f, 0f));
        hit.transform.SetAsFirstSibling();
        Place(hit.rectTransform, LabelX - 20f, y, ValueX + 160f - LabelX + 20f, RowStep);
        Image brush = MakeImage(content, brushSprite, new Color(accent.r, accent.g, accent.b, 0.12f));
        brush.raycastTarget = false;
        brush.transform.SetSiblingIndex(1);
        Place(brush.rectTransform, LabelX - 40f, y, 560f, 58f);
        brush.enabled = false;
        var trigger = hit.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerEnter, () => { name.color = Color.white; brush.enabled = true; });
        AddTrigger(trigger, EventTriggerType.PointerExit, () => { name.color = dimText; brush.enabled = false; });
    }

    // ===== 고정 레이아웃 =====
    static float CategoryY(int index) => 390f + index * 78f;

    void Build()
    {
        var canvasGo = new GameObject("SettingsCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        root = canvasGo;
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        rootGroup = canvasGo.AddComponent<CanvasGroup>();
        var rootRect = (RectTransform)canvasGo.transform;

        // 배경: 전체 어둡게 + 왼쪽은 남색 기운으로 더 어둡게 (타이틀 메뉴 화면과 같은 톤)
        Image dim = MakeImage(rootRect, null, new Color(0f, 0f, 0f, backdropAlpha));
        Stretch(dim.rectTransform);
        Image side = MakeImage(rootRect, gradientSprite, new Color(0.01f, 0.012f, 0.02f, 0.85f));
        side.rectTransform.anchorMin = new Vector2(0f, 0f);
        side.rectTransform.anchorMax = new Vector2(0f, 1f);
        side.rectTransform.pivot = new Vector2(0f, 0.5f);
        side.rectTransform.sizeDelta = new Vector2(1150f, 0f);

        // [변경] 머리글: SETTINGS (세리프) + ◆── 장식선 + 한글 캡션
        settingsTitle = MakeText(rootRect, "SETTINGS", Display, 50, Color.white, TextAnchor.MiddleLeft);
        Place(settingsTitle.rectTransform, 100f, 110f, 500f, 70f);
        settingsTitle.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -3f);
        Transform titleDeco = settingsTitle.transform;
        Ornament(titleDeco, 0f, 48f, 380f, true);
        Text titleCaption = MakeText(titleDeco, "환경 설정", bodyFont, 18, new Color(accent.r, accent.g, accent.b, 0.85f), TextAnchor.MiddleLeft);
        PlaceLocal(titleCaption.rectTransform, 2f, 80f, 300f, 28f);

        // 고른 분류 이름: 항목 위에 금빛 세리프 + 한글 캡션 + 오른쪽으로 사라지는 선
        headerSection = MakeText(rootRect, "", Display, 30, accent, TextAnchor.MiddleLeft);
        Place(headerSection.rectTransform, LabelX, 300f, 400f, 50f);
        headerCaption = MakeText(rootRect, "", bodyFont, 18, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);
        headerLine = MakeImage(rootRect, gradientSprite, new Color(accent.r, accent.g, accent.b, 0.55f));

        // 왼쪽 분류 + 고른 분류 뒤 붓질 · 마름모
        var menuGo = new GameObject("Categories", typeof(RectTransform));
        menuGo.transform.SetParent(rootRect, false);
        Stretch((RectTransform)menuGo.transform);
        menuGroup = menuGo;
        categoryBrush = MakeImage(menuGo.transform, brushSprite, new Color(accent.r, accent.g, accent.b, 0.26f));
        categoryBrush.raycastTarget = false;
        categoryDiamond = MakeImage(menuGo.transform, null, accent);
        categoryDiamond.raycastTarget = false;
        categoryDiamond.rectTransform.sizeDelta = new Vector2(10f, 10f);
        categoryDiamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        for (int i = 0; i < CategoryNames.Length; i++)
        {
            int index = i;
            Text t = AddTextButton((RectTransform)menuGo.transform, CategoryNames[i], 32, 170f, CategoryY(i), 260f, () => ShowPage((Page)(index + 1)));
            categoryButtons.Add(t.GetComponent<Button>());
            Text caption = MakeText(menuGo.transform, CategoryCaptions[i], bodyFont, 17, accent, TextAnchor.MiddleLeft);
            Place(caption.rectTransform, 440f, CategoryY(i) + 2f, 140f, 30f);
            categoryCaptionTexts.Add(caption);
        }
        if (inGame)
        {
            float y = CategoryY(4) + 20f;
            AddTextButton((RectTransform)menuGo.transform, "QUIT GAME", 22, 170f, y, 300f, () => ShowPage(Page.Quit));
            skipTutorialText = AddTextButton((RectTransform)menuGo.transform, "SKIP TUTORIAL", 22, 170f, y + 48f, 300f, SkipTutorial);
        }

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(rootRect, false);
        content = (RectTransform)contentGo.transform;
        Stretch(content);

        // 아래: 금빛 구분선 + ESC BACK · 오른쪽 버튼
        bottomLine = MakeImage(rootRect, gradientSprite, new Color(accent.r, accent.g, accent.b, 0.4f));
        Place(bottomLine.rectTransform, 70f, 962f, 1780f, 1f);
        backKey = MakeText(rootRect, "ESC", bodyFont, 16, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleLeft);
        Place(backKey.rectTransform, 100f, 1004f, 60f, 30f);
        backText = AddTextButton(rootRect, "BACK", 26, 150f, 1002f, 200f, Back);
        rightButtonText = AddTextButton(rootRect, "APPLY", 26, 1420f, 1002f, 400f, OnRightButton, TextAnchor.MiddleRight);
        rightButton = rightButtonText.GetComponent<Button>();
    }

    void LayoutHeaderLine()
    {
        // 분류 이름 · 캡션 뒤에서 시작해 오른쪽으로 사라지는 선 (분류를 열었을 때만)
        bool on = headerSection.gameObject.activeSelf;
        float captionX = LabelX + headerSection.preferredWidth + 18f;
        Place(headerCaption.rectTransform, captionX, 302f, 200f, 30f);
        float start = captionX + headerCaption.preferredWidth + 30f;
        Place(headerLine.rectTransform, start, 302f, 1842f - start, 1f);
        headerLine.gameObject.SetActive(on);
    }

    // ◆── 장식선: 마름모에서 시작해 오른쪽으로 사라지는 금빛 선
    void Ornament(Transform parent, float x, float y, float width, bool local = false)
    {
        Image gem = MakeImage(parent, null, accent);
        gem.raycastTarget = false;
        Image line = MakeImage(parent, gradientSprite, new Color(accent.r, accent.g, accent.b, 0.75f));
        line.raycastTarget = false;
        if (local)
        {
            PlaceLocal(gem.rectTransform, x, y, 9f, 9f);
            PlaceLocal(line.rectTransform, x + 16f, y, width, 2f);
        }
        else
        {
            Place(gem.rectTransform, x, y, 9f, 9f);
            Place(line.rectTransform, x + 16f, y, width, 2f);
        }
        gem.rectTransform.pivot = new Vector2(0.5f, 0.5f); // 제자리에서 45도 돌려 마름모로
        gem.rectTransform.anchoredPosition += new Vector2(4.5f, 0f);
        gem.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    // ===== 만들기 도우미 =====
    // 좌상단 기준 픽셀 좌표(1920x1080, y는 아래로) → 앵커 배치. y는 줄의 세로 중앙
    static void Place(RectTransform rect, float x, float centerY, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -centerY);
        rect.sizeDelta = new Vector2(width, height);
    }

    // 부모(글자) 왼쪽 가운데 기준, y는 아래로
    static void PlaceLocal(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void SetCenter(RectTransform rect, float x, float y)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    Text MakeText(Transform parent, string text, Font font, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject(text.Length > 0 ? text : "Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static Image MakeImage(Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject("Image", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        return image;
    }

    // 글자 버튼: 평소 흐리게, 마우스를 올리면 하얗게, 누르면 금빛
    Text AddTextButton(Transform parent, string label, int size, float x, float y, float width, Action onClick, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        Text t = MakeText(parent, label, Display, size, Color.white, anchor);
        t.raycastTarget = true;
        Place(t.rectTransform, x, y, width, 56f);
        Button button = t.gameObject.AddComponent<Button>();
        button.targetGraphic = t;
        ColorBlock colors = button.colors;
        colors.normalColor = dimText;
        colors.highlightedColor = Color.white;
        colors.selectedColor = dimText;
        colors.pressedColor = accent;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None; // 클릭 뒤 선택 상태로 남아 계속 밝게 보이지 않게
        button.navigation = nav;
        button.onClick.AddListener(() => onClick());
        return t;
    }

    void SetBright(Button button, bool bright)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = bright ? Color.white : dimText;
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
    }

    static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(go);
    }

    // ===== 코드로 만드는 스프라이트 (기본 UI 스프라이트는 빌드에서 못 불러와서) =====
    static Sprite MakeCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // 둥근 알약 (가로로 늘려 쓰도록 9-slice 테두리 = 반지름). thickness > 0이면 테두리만
    static Sprite MakePill(int width, int height, float thickness)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = height * 0.5f;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, width - r);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, r));
                float a = Mathf.Clamp01(r - d);
                if (thickness > 0f) a *= Mathf.Clamp01(d - (r - thickness));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }

    // 왼쪽 진하고 오른쪽으로 사라지는 가로 그라데이션
    static Sprite MakeGradient(int width)
    {
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < width; x++) tex.SetPixel(x, 0, new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0f, 1f, x / (float)(width - 1))));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, 1), new Vector2(0f, 0.5f), 100f);
    }
}
