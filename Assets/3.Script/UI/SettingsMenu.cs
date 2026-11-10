using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// ESC 설정 패널 (라이어스 바 모작 레이아웃): 왼쪽 분류(DISPLAY·GAMEPLAY·GRAPHICS·AUDIO), 오른쪽 항목, 아래 Back · Apply/Restore Defaults
//  - 인게임: ESC로 열면 일시정지(GamePause) + 커서 표시, Back/ESC로 한 단계씩 닫고 마지막에 재개
//  - 화면 항목은 Apply를 눌러야 적용, 나머지는 바꾸는 즉시 적용 (GameSettings · AudioOptions)
//  - UI는 실행할 때 코드로 생성 (1920x1080 기준 좌표). 배치·연결은 Tools/Setup/Settings Menu
// 타이틀 화면에서도 같은 컴포넌트를 쓰도록 inGame=false면 일시정지·ESC 열기를 하지 않음
[DefaultExecutionOrder(10000)]
public class SettingsMenu : MonoBehaviour
{
    [SerializeField] Font titleFont;   // 분류·버튼 (굵게)
    [SerializeField] Font bodyFont;    // 항목 이름·값
    [SerializeField] bool inGame = true;
    [SerializeField] Color accent = new Color(0.91f, 0.78f, 0.55f, 1f);
    [SerializeField] Color dimText = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField, Range(0f, 1f)] float backdropAlpha = 0.72f;

    public bool IsOpen => root != null && root.activeSelf;
    public event Action Closed; // 타이틀 화면에서 돌아갈 때 사용

    enum Page { Root, Display, Gameplay, Graphics, Audio, Quit }

    static readonly string[] CategoryNames = { "DISPLAY", "GAMEPLAY", "GRAPHICS", "AUDIO" };

    GameObject root;
    GameObject menuGroup;
    RectTransform content;
    Text headerSection;
    Image headerLine;
    Text rightButtonText;
    Button rightButton;
    readonly List<Image> categoryLines = new List<Image>();
    readonly List<Action> refreshers = new List<Action>();
    Page page = Page.Root;
    bool resumeRequested;

    // Display 페이지에서 Apply 전까지 들고 있는 값
    int pendingResolution, pendingFps;
    bool pendingWindowed, pendingVSync;

    Sprite circleSprite, pillSprite, pillOutlineSprite, gradientSprite;
    Text settingsTitle, backText;
    Image bottomLine;
    readonly List<Button> categoryButtons = new List<Button>();

    void Awake()
    {
        circleSprite = MakeCircle(64);
        pillSprite = MakePill(64, 32, 0f);
        pillOutlineSprite = MakePill(64, 32, 2f);
        gradientSprite = MakeGradient(256);
        EnsureEventSystem();
        Build();
        root.SetActive(false);
    }

    void OnDisable()
    {
        if (inGame && GamePause.IsPaused) GamePause.Set(false);
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
        if (!IsOpen)
        {
            // [변경] 스킬트리가 열려 있으면 ESC는 스킬트리가 처리
            if (inGame && (SkillTreeMenu.Instance == null || !SkillTreeMenu.Instance.IsOpen)) Open();
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
    }

    public void Open()
    {
        root.SetActive(true);
        ShowPage(Page.Root);
        if (inGame) GamePause.Set(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        root.SetActive(false);
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
        menuGroup.SetActive(!quit);
        headerSection.gameObject.SetActive(next != Page.Root && !quit);
        headerSection.text = next != Page.Root && !quit ? CategoryNames[(int)next - 1] : "";
        for (int i = 0; i < categoryButtons.Count; i++)
        {
            bool selected = (int)next - 1 == i;
            categoryLines[i].enabled = selected;
            SetBright(categoryButtons[i], selected); // 고른 분류는 계속 밝게
        }
        settingsTitle.gameObject.SetActive(!quit);
        backText.gameObject.SetActive(!quit);
        bottomLine.gameObject.SetActive(!quit);

        rightButton.gameObject.SetActive(next == Page.Root || next == Page.Display);
        rightButtonText.text = next == Page.Display ? "Apply" : "Restore Defaults";

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
        Text question = MakeText(content, "Do you want to leave the game?", bodyFont, 32, Color.white, TextAnchor.MiddleLeft);
        Place(question.rectTransform, 170f, 500f, 900f, 50f);
        AddTextButton(content, "Leave", 34, 170f, 575f, 300f, () =>
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });
        AddTextButton(content, "Back", 34, 170f, 640f, 300f, () => ShowPage(Page.Root));
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
    const float LabelX = 622f, ValueX = 1243f, RowY0 = 390f, RowStep = 60f;

    Text AddLabel(int row, string label)
    {
        Text text = MakeText(content, label, bodyFont, 28, dimText, TextAnchor.MiddleLeft);
        Place(text.rectTransform, LabelX, RowY0 + row * RowStep, 480f, 50f);
        return text;
    }

    // < 값 > 선택
    void AddSelector(int row, string label, Func<string> value, Action<int> change)
    {
        Text name = AddLabel(row, label);
        float y = RowY0 + row * RowStep;
        Text valueText = MakeText(content, "", bodyFont, 26, Color.white, TextAnchor.MiddleCenter);
        Place(valueText.rectTransform, ValueX - 90f, y, 180f, 50f);
        AddTextButton(content, "<", 34, ValueX - 118f, y, 40f, () => { change(-1); RefreshAll(); }, TextAnchor.MiddleCenter);
        AddTextButton(content, ">", 34, ValueX + 80f, y, 40f, () => { change(1); RefreshAll(); }, TextAnchor.MiddleCenter);
        refreshers.Add(() => valueText.text = value());
        AddRowHover(name, y);
    }

    // ON/OFF 알약 토글
    void AddToggle(int row, string label, Func<bool> value, Action<bool> change)
    {
        Text name = AddLabel(row, label);
        float y = RowY0 + row * RowStep;
        Text state = MakeText(content, "", bodyFont, 24, Color.white, TextAnchor.MiddleRight);
        Place(state.rectTransform, ValueX - 140f, y, 90f, 50f);

        Image pill = MakeImage(content, pillSprite, new Color(1f, 1f, 1f, 0.15f));
        pill.type = Image.Type.Sliced;
        Place(pill.rectTransform, ValueX + 5f, y, 60f, 34f);
        Image outline = MakeImage(pill.rectTransform, pillOutlineSprite, new Color(1f, 1f, 1f, 0.7f));
        outline.type = Image.Type.Sliced;
        outline.raycastTarget = false;
        Stretch(outline.rectTransform);
        Image knob = MakeImage(pill.rectTransform, circleSprite, Color.white);
        var knobRect = knob.rectTransform;
        knobRect.anchorMin = knobRect.anchorMax = new Vector2(0f, 0.5f);
        knobRect.sizeDelta = new Vector2(26f, 26f);

        Button button = pill.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { change(!value()); RefreshAll(); });

        refreshers.Add(() =>
        {
            bool on = value();
            state.text = on ? "ON" : "OFF";
            knobRect.anchoredPosition = new Vector2(on ? 44f : 16f, 0f);
            pill.color = on ? new Color(1f, 1f, 1f, 0.15f) : new Color(1f, 1f, 1f, 0.04f);
            knob.color = on ? Color.white : new Color(1f, 1f, 1f, 0.5f);
        });
        AddRowHover(name, y);
    }

    // 숫자 + 가는 막대 슬라이더
    void AddSlider(int row, string label, float min, float max, Func<float> value, Action<float> change, Func<float, string> format)
    {
        Text name = AddLabel(row, label);
        float y = RowY0 + row * RowStep;

        Text number = null;
        if (format != null)
        {
            number = MakeText(content, "", bodyFont, 24, Color.white, TextAnchor.MiddleRight);
            Place(number.rectTransform, ValueX - 150f, y, 80f, 50f);
        }

        var go = new GameObject(label + " Slider", typeof(RectTransform));
        go.transform.SetParent(content, false);
        Place((RectTransform)go.transform, ValueX - 40f, y, 156f, 24f);
        Stretch(MakeImage(go.transform, null, new Color(0f, 0f, 0f, 0f)).rectTransform); // 클릭 영역 (가는 막대만으로는 잡기 어려움)
        Slider slider = go.AddComponent<Slider>();

        Image track = MakeImage(go.transform, null, new Color(1f, 1f, 1f, 0.2f));
        var trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0.5f);
        trackRect.anchorMax = new Vector2(1f, 0.5f);
        trackRect.sizeDelta = new Vector2(0f, 4f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.sizeDelta = new Vector2(0f, 4f);
        Image fill = MakeImage(fillArea.transform, null, new Color(1f, 1f, 1f, 0.85f));
        Stretch(fill.rectTransform); // 가로는 Slider가 값에 맞춰 바꿈

        var handleArea = new GameObject("Handle Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        Stretch((RectTransform)handleArea.transform);
        Image handle = MakeImage(handleArea.transform, circleSprite, Color.white);
        handle.rectTransform.sizeDelta = new Vector2(20f, 20f);

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

    // 줄 위에 마우스가 오면 항목 이름이 밝아짐
    void AddRowHover(Text name, float y)
    {
        Image hit = MakeImage(content, null, new Color(0f, 0f, 0f, 0f));
        hit.transform.SetAsFirstSibling();
        Place(hit.rectTransform, LabelX - 20f, y, ValueX + 160f - LabelX + 20f, RowStep);
        var trigger = hit.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerEnter, () => name.color = Color.white);
        AddTrigger(trigger, EventTriggerType.PointerExit, () => name.color = dimText);
    }

    // ===== 고정 레이아웃 =====
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
        var rootRect = (RectTransform)canvasGo.transform;

        // 배경: 전체 어둡게 + 왼쪽은 더 어둡게(글자 대비)
        Image dim = MakeImage(rootRect, null, new Color(0f, 0f, 0f, backdropAlpha));
        Stretch(dim.rectTransform);
        Image side = MakeImage(rootRect, gradientSprite, new Color(0f, 0f, 0f, 0.6f));
        side.rectTransform.anchorMin = new Vector2(0f, 0f);
        side.rectTransform.anchorMax = new Vector2(0f, 1f);
        side.rectTransform.pivot = new Vector2(0f, 0.5f);
        side.rectTransform.sizeDelta = new Vector2(1000f, 0f);

        // 머리글: SETTINGS  분류  ─────
        settingsTitle = MakeText(rootRect, "SETTINGS", titleFont, 30, dimText, TextAnchor.MiddleLeft);
        Place(settingsTitle.rectTransform, 100f, 97f, 200f, 50f);
        headerSection = MakeText(rootRect, "", titleFont, 30, Color.white, TextAnchor.MiddleLeft);
        Place(headerSection.rectTransform, 300f, 97f, 300f, 50f);
        headerLine = MakeImage(rootRect, null, new Color(1f, 1f, 1f, 0.35f));

        // 왼쪽 분류 + 선택 밑줄
        var menuGo = new GameObject("Categories", typeof(RectTransform));
        menuGo.transform.SetParent(rootRect, false);
        Stretch((RectTransform)menuGo.transform);
        menuGroup = menuGo;
        for (int i = 0; i < CategoryNames.Length; i++)
        {
            int index = i;
            Text t = AddTextButton((RectTransform)menuGo.transform, CategoryNames[i], 34, 170f, 390f + i * 75f, 260f, () => ShowPage((Page)(index + 1)));
            categoryButtons.Add(t.GetComponent<Button>());
            Image line = MakeImage((RectTransform)menuGo.transform, null, accent);
            Place(line.rectTransform, 170f, 425f + i * 75f, 200f, 2f);
            categoryLines.Add(line);
        }
        if (inGame)
        {
            AddTextButton((RectTransform)menuGo.transform, "QUIT GAME", 26, 170f, 390f + 4 * 75f + 30f, 260f, () => ShowPage(Page.Quit));
        }

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(rootRect, false);
        content = (RectTransform)contentGo.transform;
        Stretch(content);

        // 아래: 구분선 + Back · 오른쪽 버튼
        bottomLine = MakeImage(rootRect, null, new Color(1f, 1f, 1f, 0.35f));
        Place(bottomLine.rectTransform, 70f, 962f, 1780f, 1f);
        backText = AddTextButton(rootRect, "Back", 32, 100f, 1002f, 200f, Back);
        rightButtonText = AddTextButton(rootRect, "Apply", 32, 1420f, 1002f, 400f, OnRightButton, TextAnchor.MiddleRight);
        rightButton = rightButtonText.GetComponent<Button>();
    }

    void LayoutHeaderLine()
    {
        // 머리글 글자 길이만큼 띄우고 오른쪽 끝까지
        float start = headerSection.gameObject.activeSelf ? 300f + headerSection.preferredWidth + 45f : 300f;
        Place(headerLine.rectTransform, start, 100f, 1842f - start, 1f);
        headerLine.gameObject.SetActive(headerSection.gameObject.activeSelf); // 분류를 열었을 때만
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

    // 글자 버튼: 평소 흐리게, 마우스를 올리면 하얗게
    Text AddTextButton(Transform parent, string label, int size, float x, float y, float width, Action onClick, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        Text t = MakeText(parent, label, titleFont, size, Color.white, anchor);
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
