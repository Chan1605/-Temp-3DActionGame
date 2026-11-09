using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// 발도 자세 연출 (PlayerSkill이 실행 중 자동으로 붙임, 셋업 불필요)
//  - 화면 흑백 + 비네트 (UI는 후처리를 안 받으므로 표식만 색이 남음)
//  - 화면 중앙 조준 링 + 남은 시간 링
//  - 조준 중인 적: 차오르는 원형 게이지 / 표식이 붙은 적: 순서 번호
// 전투 코드는 이 연출을 모르고, 여기서 PlayerSkill의 상태를 읽기만 함
public class DrawStanceFx : MonoBehaviour
{
    [SerializeField] float fadeIn = 0.12f;
    [SerializeField] float fadeOut = 0.2f;
    [SerializeField] float vignette = 0.35f;
    [SerializeField] Color markColor = new Color(1f, 0.22f, 0.18f, 1f); // [변경] 표식·조준 게이지 빨강 (흑백 화면에서 가장 잘 보임)
    [SerializeField] Color doneColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] Color reticleColor = new Color(1f, 1f, 1f, 0.8f);
    [SerializeField] float markHeight = 1.1f;
    [SerializeField] float markSize = 54f;

    PlayerSkill skill;
    Camera cam;
    Volume volume;

    RectTransform canvasRect;
    Image reticle, timerRing, markingRing;
    Image[] markRings;
    Text[] markNumbers;
    Text hint;
    static Sprite ringSprite, discSprite;

    void Awake()
    {
        skill = GetComponent<PlayerSkill>();
        BuildVolume();
        BuildCanvas();
    }

    void OnDestroy()
    {
        if (volume != null) Destroy(volume.gameObject);
        if (canvasRect != null) Destroy(canvasRect.gameObject);
    }

    void LateUpdate()
    {
        if (skill == null) return;
        if (cam == null) cam = Camera.main;

        bool stance = skill.InDrawStance;
        float dt = Time.unscaledDeltaTime;
        if (volume != null)
        {
            float target = stance ? 1f : 0f;
            float speed = 1f / Mathf.Max(stance ? fadeIn : fadeOut, 0.01f);
            volume.weight = Mathf.MoveTowards(volume.weight, target, speed * dt);
        }

        bool aiming = stance && !skill.StanceAutoDraw; // 회피 직후 자동 발도는 흑백·표식만 (조준 UI 없음)
        reticle.enabled = timerRing.enabled = hint.enabled = aiming;
        if (aiming)
        {
            hint.text = skill.StanceByHold ? "조준점을 적에게 올려 표식 · 떼면 발도" : "조준점을 적에게 올려 표식 · Q로 발도";
            if (skill.EnchantStanceWindow > 0f) hint.text += $"   ·   발도 시간 {skill.EnchantStanceWindow:0.0}"; // 각성 중 발도 시간
            float size = skill.StanceReticleRadius * Screen.height * 2f;
            reticle.rectTransform.sizeDelta = Vector2.one * size;
            timerRing.rectTransform.sizeDelta = Vector2.one * (size + 14f);
            timerRing.fillAmount = skill.StanceRemaining01;
        }

        // 조준 중인 적: 표식까지 차오르는 링
        Transform marking = skill.StanceMarkingTarget;
        markingRing.enabled = marking != null && Place(markingRing.rectTransform, marking);
        if (markingRing.enabled) markingRing.fillAmount = skill.StanceMarkProgress;

        // 표식: 순서 번호 (이미 벤 표식은 흐리게)
        bool showMarks = skill.ShowStanceMarks;
        for (int i = 0; i < markRings.Length; i++)
        {
            Transform t = showMarks && i < skill.StanceMarkCount ? skill.StanceMark(i) : null;
            bool visible = t != null && Place(markRings[i].rectTransform, t);
            markRings[i].enabled = markNumbers[i].enabled = visible;
            if (!visible) continue;
            bool done = skill.StanceMarkDone(i);
            markRings[i].color = done ? doneColor : markColor;
            markNumbers[i].color = done ? doneColor : Color.white; // [변경] 숫자는 흰색 유지
        }
    }

    bool Place(RectTransform rect, Transform target)
    {
        if (cam == null) return false;
        Vector3 screen = cam.WorldToScreenPoint(target.position + Vector3.up * markHeight);
        if (screen.z <= 0f) return false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
        rect.anchoredPosition = local;
        return true;
    }

    void BuildVolume()
    {
        var go = new GameObject("DrawStanceVolume");
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 101f; // 패링 펄스(100)보다 위
        volume.weight = 0f;

        // 지정한 값만 Override → 씬의 기존 색 보정은 그대로
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;
        ColorAdjustments color = profile.Add<ColorAdjustments>();
        color.saturation.Override(-100f);
        Vignette vig = profile.Add<Vignette>();
        vig.intensity.Override(vignette);
        vig.smoothness.Override(0.5f);

        Camera main = Camera.main;
        UniversalAdditionalCameraData data = main != null ? main.GetUniversalAdditionalCameraData() : null;
        if (data != null && !data.renderPostProcessing) data.renderPostProcessing = true;
    }

    void BuildCanvas()
    {
        var go = new GameObject("DrawStanceCanvas", typeof(RectTransform));
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // 조준 반경을 화면 픽셀로 맞추기 위해
        canvasRect = (RectTransform)go.transform;

        if (ringSprite == null) ringSprite = MakeCircleSprite(0.82f);
        if (discSprite == null) discSprite = MakeCircleSprite(0f);
        Font font = FindFont();

        reticle = NewImage("Reticle", ringSprite, reticleColor);
        timerRing = NewImage("Timer", ringSprite, new Color(markColor.r, markColor.g, markColor.b, 0.6f));
        SetRadial(timerRing);
        markingRing = NewImage("Marking", ringSprite, markColor);
        markingRing.rectTransform.sizeDelta = Vector2.one * markSize;
        SetRadial(markingRing);

        hint = NewText("Hint", font, 20, new Color(1f, 1f, 1f, 0.85f));
        hint.rectTransform.anchoredPosition = new Vector2(0f, -120f);
        hint.rectTransform.sizeDelta = new Vector2(600f, 40f);

        int count = skill != null ? Mathf.Max(1, skill.StanceMaxMarks) : 6;
        markRings = new Image[count];
        markNumbers = new Text[count];
        for (int i = 0; i < count; i++)
        {
            markRings[i] = NewImage("Mark" + (i + 1), discSprite, markColor);
            markRings[i].rectTransform.sizeDelta = Vector2.one * (markSize * 0.6f);
            markNumbers[i] = NewText("No", font, 22, Color.white); // [변경] 빨간 바탕이라 흰 숫자
            markNumbers[i].transform.SetParent(markRings[i].transform, false);
            markNumbers[i].rectTransform.anchoredPosition = Vector2.zero;
            markNumbers[i].text = (i + 1).ToString();
        }

        reticle.enabled = timerRing.enabled = markingRing.enabled = hint.enabled = false;
        foreach (Image img in markRings) img.enabled = false;
        foreach (Text t in markNumbers) t.enabled = false;
    }

    Image NewImage(string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(canvasRect, false);
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    Text NewText(string name, Font font, int size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(canvasRect, false);
        Text text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return text;
    }

    static void SetRadial(Image img)
    {
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = (int)Image.Origin360.Top;
        img.fillClockwise = false;
    }

    // HUD와 같은 글꼴(한글)을 쓰도록 씬의 Text에서 빌려옴
    static Font FindFont()
    {
        foreach (Text t in FindObjectsByType<Text>(FindObjectsSortMode.None))
            if (t.font != null) return t.font;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // inner = 0이면 꽉 찬 원, 0~1이면 그 비율만큼 속이 빈 링
    static Sprite MakeCircleSprite(float inner)
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
            float outer = Mathf.Clamp01((1f - d) * r * 0.5f);
            float hole = inner > 0f ? Mathf.Clamp01((d - inner) * r * 0.5f) : 1f;
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * outer * hole));
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
