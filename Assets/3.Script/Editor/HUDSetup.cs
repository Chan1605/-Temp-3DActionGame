using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 1차 HUD 배치. 기준 해상도 1920x1080, 요소는 화면 모서리·가장자리에 앵커를 걸어 비율이 달라도(21:9, 16:10) 제자리에 붙음
// 다시 실행하면 HUD를 통째로 새로 만듦 (직접 고친 배치는 사라지니 확정 전까지만 사용)
public static class HUDSetup
{
    const string FontBold = "Assets/9.Font/public/static/Pretendard-Bold.otf";
    const string FontRegular = "Assets/9.Font/public/static/Pretendard-Medium.otf";
    const string HudName = "HUD";
    const float Margin = 56f;

    static readonly Color PanelDark = new Color(0.04f, 0.05f, 0.07f, 0.6f);
    static readonly Color HpColor = new Color(0.86f, 0.22f, 0.16f);
    static readonly Color HpTrailColor = new Color(1f, 0.86f, 0.62f, 0.9f);
    static readonly Color ThunderColor = new Color(0.45f, 0.85f, 1f);
    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);
    static readonly Color SubText = new Color(0.82f, 0.85f, 0.9f, 0.85f);

    static Font bold, regular;
    static Sprite uiSprite;

    [MenuItem("Tools/Setup/HUD")]
    static void Run()
    {
        bold = AssetDatabase.LoadAssetAtPath<Font>(FontBold);
        regular = AssetDatabase.LoadAssetAtPath<Font>(FontRegular);
        if (bold == null || regular == null) Debug.LogWarning("[HUDSetup] Pretendard 폰트를 찾지 못해 기본 폰트로 만듭니다.");
        uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        GameObject old = GameObject.Find(HudName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        // Canvas: 화면 오버레이 + 1920x1080 기준 스케일 (가로·세로 중간 비율로 맞춤)
        var canvasGo = new GameObject(HudName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create HUD");
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        Transform root = canvasGo.transform;
        var hud = canvasGo.AddComponent<GameHUD>();
        var so = new SerializedObject(hud);

        BuildPlayerStatus(root, so);
        BuildSkill(root, so);
        BuildChargeGauge(root, so); // [변경]
        BuildControls(root);
        BuildZoneBanner(root, so);
        BuildWavePanel(root, so);
        BuildDeathPanel(root, so);
        BuildClearPanel(root, so);

        so.FindProperty("health").objectReferenceValue = Object.FindAnyObjectByType<PlayerHealth>();
        so.FindProperty("skill").objectReferenceValue = Object.FindAnyObjectByType<PlayerSkill>();
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
        Selection.activeGameObject = canvasGo;
        Debug.Log("[HUDSetup] 완료 · 기준 1920x1080, Game 뷰 해상도를 바꿔 모서리 배치를 확인하세요");
    }

    // ===== 좌하단: 이름 · 체력 · 뇌기 4칸 =====
    static void BuildPlayerStatus(Transform root, SerializedObject so)
    {
        RectTransform group = Rect("PlayerStatus", root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Margin, Margin - 4f), new Vector2(460f, 84f));

        Label("Name", group, "뇌문류 · 막내 제자", 18, SubText, TextAnchor.LowerLeft, regular, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 58f), new Vector2(300f, 24f));
        Text hpText = Label("HpText", group, "100 / 100", 18, SubText, TextAnchor.LowerRight, regular, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 58f), new Vector2(160f, 24f));

        RectTransform hpBack = Panel("HpBar", group, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 26f), new Vector2(460f, 22f), PanelDark);
        AddOutline(hpBack.gameObject, new Color(0f, 0f, 0f, 0.8f), 2f);
        RectTransform trail = FillBar("Trail", hpBack, HpTrailColor, 3f);
        RectTransform fill = FillBar("Fill", hpBack, HpColor, 3f);

        var fills = new RectTransform[4];
        const float gap = 8f;
        float cellWidth = (460f - gap * 3f) / 4f;
        for (int i = 0; i < 4; i++)
        {
            RectTransform cell = Panel($"Thunder_{i + 1}", group, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(i * (cellWidth + gap), 0f), new Vector2(cellWidth, 14f), PanelDark);
            AddOutline(cell.gameObject, new Color(0.2f, 0.5f, 0.8f, 0.6f), 1.5f);
            fills[i] = FillBar("Fill", cell, ThunderColor, 2f);
            fills[i].anchorMax = new Vector2(0f, 1f);
        }

        so.FindProperty("hpFill").objectReferenceValue = fill;
        so.FindProperty("hpTrail").objectReferenceValue = trail;
        so.FindProperty("hpText").objectReferenceValue = hpText;
        SerializedProperty list = so.FindProperty("thunderFills");
        list.arraySize = fills.Length;
        for (int i = 0; i < fills.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = fills[i];
    }

    // ===== 우하단: 벽력일섬 슬롯 (원형 쿨타임 + Q 키 표시) =====
    static void BuildSkill(Transform root, SerializedObject so)
    {
        RectTransform slot = Panel("SkillSlot", root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, Margin), new Vector2(108f, 108f), PanelDark);
        AddOutline(slot.gameObject, Gold, 2f);

        Text label = Label("Label", slot, "일섬", 30, Color.white, TextAnchor.MiddleCenter, bold, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        RectTransform cool = Panel("Cooldown", slot, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.65f));
        var coolImage = cool.GetComponent<Image>();
        coolImage.sprite = uiSprite;
        coolImage.type = Image.Type.Filled;
        coolImage.fillMethod = Image.FillMethod.Radial360;
        coolImage.fillOrigin = (int)Image.Origin360.Top;
        coolImage.fillClockwise = false;
        coolImage.fillAmount = 0f;

        RectTransform key = Panel("Key", slot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(4f, 4f), new Vector2(34f, 34f), new Color(0.08f, 0.08f, 0.1f, 0.95f));
        AddOutline(key.gameObject, Gold, 1.5f);
        Label("Text", key, "Q", 20, Gold, TextAnchor.MiddleCenter, bold, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        so.FindProperty("skillCooldown").objectReferenceValue = coolImage;
        so.FindProperty("skillLabel").objectReferenceValue = label;
    }

    // [변경] ===== 화면 중앙 아래: 차지 게이지 (차지 중에만 보임, 3단계 눈금) =====
    static void BuildChargeGauge(Transform root, SerializedObject so)
    {
        RectTransform panel = Rect("ChargeGauge", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(260f, 40f));
        var group = panel.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.alpha = 0f;

        RectTransform back = Panel("Bar", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(260f, 10f), PanelDark);
        AddOutline(back.gameObject, new Color(0f, 0f, 0f, 0.8f), 1.5f);
        RectTransform fill = FillBar("Fill", back, ThunderColor, 2f);
        fill.anchorMax = new Vector2(0f, 1f);

        // 2·3단계 도달 지점 눈금 (PlayerChargeAttack 기본 levelTimes 0.55 / 1.15 기준)
        foreach (float t in new[] { 0.55f / 1.15f, 1f })
            Panel("Tick", back, new Vector2(t, 0f), new Vector2(t, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, 6f), new Color(1f, 1f, 1f, 0.6f));

        Text label = Label("Level", panel, "차지 1", 18, Color.white, TextAnchor.LowerCenter, bold, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(200f, 24f));

        so.FindProperty("chargeGauge").objectReferenceValue = group;
        so.FindProperty("chargeFill").objectReferenceValue = fill;
        so.FindProperty("chargeLabel").objectReferenceValue = label;
    }

    // ===== 우하단 슬롯 왼쪽: 조작 안내 (시연용, 필요 없으면 오브젝트를 끄면 됨) =====
    static void BuildControls(Transform root)
    {
        const string text = "좌클릭  약공격\n우클릭  강공격 · 간파\n우클릭 홀드  차지 강타\nShift  회피\nSpace  점프"; // [변경]
        Text t = Label("Controls", root, text, 16, new Color(0.85f, 0.88f, 0.92f, 0.7f), TextAnchor.LowerRight, regular,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin - 132f, Margin), new Vector2(280f, 120f));
        t.lineSpacing = 1.15f;
    }

    // ===== 상단 중앙: 구역 이름 배너 =====
    static void BuildZoneBanner(Transform root, SerializedObject so)
    {
        // 나침반 바(HUD Navigation, 상단 중앙) 아래로 내림
        RectTransform banner = Rect("ZoneBanner", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(800f, 120f));
        var group = banner.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        Text number = Label("Number", banner, "구역 1", 22, Gold, TextAnchor.MiddleCenter, regular, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(300f, 30f));
        Panel("Line_L", banner, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1f, 0.5f), new Vector2(-90f, -15f), new Vector2(140f, 2f), new Color(Gold.r, Gold.g, Gold.b, 0.7f));
        Panel("Line_R", banner, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f), new Vector2(90f, -15f), new Vector2(140f, 2f), new Color(Gold.r, Gold.g, Gold.b, 0.7f));
        Text title = Label("Title", banner, "산문", 56, Color.white, TextAnchor.MiddleCenter, bold, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(800f, 76f));

        so.FindProperty("zoneBanner").objectReferenceValue = group;
        so.FindProperty("zoneNumber").objectReferenceValue = number;
        so.FindProperty("zoneTitle").objectReferenceValue = title;
    }

    // ===== 우상단: 웨이브 · 남은 적 =====
    static void BuildWavePanel(Transform root, SerializedObject so)
    {
        RectTransform panel = Rect("WavePanel", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Margin, -48f), new Vector2(280f, 72f));
        var group = panel.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        Text wave = Label("Wave", panel, "웨이브 1 / 2", 28, Color.white, TextAnchor.UpperRight, bold, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(280f, 38f));
        Text enemy = Label("Enemies", panel, "남은 적 0", 18, SubText, TextAnchor.UpperRight, regular, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -40f), new Vector2(280f, 26f));

        so.FindProperty("wavePanel").objectReferenceValue = group;
        so.FindProperty("waveText").objectReferenceValue = wave;
        so.FindProperty("enemyText").objectReferenceValue = enemy;
    }

    // ===== 중앙: 사망 (화면 전체를 어둡게) =====
    static void BuildDeathPanel(Transform root, SerializedObject so)
    {
        RectTransform panel = Panel("DeathPanel", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f));
        var group = panel.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.alpha = 0f; // 편집 화면을 가리지 않게 (확인할 땐 Alpha를 1로)

        Label("Title", panel, "쓰러졌다", 72, new Color(0.88f, 0.22f, 0.16f), TextAnchor.MiddleCenter, bold, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(900f, 100f));
        Label("Sub", panel, "구역 입구에서 다시 시작합니다", 22, SubText, TextAnchor.MiddleCenter, regular, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -44f), new Vector2(900f, 34f));

        so.FindProperty("deathPanel").objectReferenceValue = group;
    }

    // ===== 중앙 위: 구역 돌파 =====
    static void BuildClearPanel(Transform root, SerializedObject so)
    {
        RectTransform panel = Rect("ClearPanel", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(900f, 120f));
        var group = panel.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.alpha = 0f;

        Label("Title", panel, "구역 돌파", 52, new Color(0.75f, 0.92f, 1f), TextAnchor.MiddleCenter, bold, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 16f), new Vector2(900f, 70f));
        Label("Sub", panel, "출구가 열렸다", 20, SubText, TextAnchor.MiddleCenter, regular, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -32f), new Vector2(900f, 30f));

        so.FindProperty("clearPanel").objectReferenceValue = group;
    }

    // ===== 공용 =====
    static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
    {
        RectTransform rt = Rect(name, parent, anchorMin, anchorMax, pivot, pos, size);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rt;
    }

    // 부모를 꽉 채우고 오른쪽 앵커(anchorMax.x)로 길이를 조절하는 막대. inset만큼 안쪽으로 들여 테두리를 남김
    static RectTransform FillBar(string name, Transform parent, Color color, float inset)
    {
        RectTransform rt = Panel(name, parent, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, color);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        return rt;
    }

    static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align, Font font,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 sizeDelta)
    {
        RectTransform rt = Rect(name, parent, anchorMin, anchorMax, pivot, pos, sizeDelta);
        var t = rt.gameObject.AddComponent<Text>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        if (font != null) t.font = font;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;

        var shadow = rt.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    static void AddOutline(GameObject go, Color color, float width)
    {
        var outline = go.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(width, -width);
    }
}
