using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// 궁극기 화면 연출 (PlayerUltimate가 만들어 씀). 모든 값은 실제 시간으로 움직이고 EndAll로 한꺼번에 원래대로
//  레터박스(위아래 검은 띠) · 기술 이름(한자 + 읽기) · 번쩍임 · 흑백/어둡게(볼륨) · 하늘 어둡게(안개)
//  화면 베기(Cut): 지금 화면을 그대로 찍어 두 조각으로 나눠 들고, 실처럼 가는 선이 그어진 뒤 두 조각이 선을 따라 어긋남
public class UltimateFx : MonoBehaviour
{
    const float BarHeight = 120f;

    Volume volume;
    ColorAdjustments color;
    Vignette vignette;
    RectTransform barTop, barBottom;
    Image flash;
    // 화면 베기
    GameObject cutRoot;
    RectTransform cutTop, cutBottom, cutLine, cutGlow;
    RawImage cutTopImage, cutBottomImage;
    CanvasGroup cutGroup;
    Texture2D cutShot;
    RectTransform canvasRect;
    Text title, reading;
    CanvasGroup titleGroup;
    Font font;

    float barTarget, bar;
    float desatTarget, desat;
    float darkTarget, dark;
    float flashAmount;
    Color flashColor = Color.white;
    float titleTimer;

    // 하늘(안개) 원래 값
    bool skySaved;
    Color fogColor;
    float fogDensity, ambient;
    Color darkFog;
    float darkFogDensity;

    void Awake()
    {
        BuildVolume();
        BuildOverlay();
    }

    void OnDestroy()
    {
        RestoreSky();
        if (cutShot != null) Destroy(cutShot);
        if (volume != null) Destroy(volume.gameObject);
    }

    // ===== 명령 =====
    public Canvas Overlay { get; private set; } // [변경]

    public void Letterbox(bool on) => barTarget = on ? 1f : 0f;
    public void Desaturate(float amount) => desatTarget = Mathf.Clamp01(amount);

    public void Flash(Color c, float amount)
    {
        flashColor = c;
        flashAmount = Mathf.Max(flashAmount, amount);
    }

    // 하늘을 어둡게: 안개를 짙고 어두운 색으로 (원래 값은 처음 한 번만 저장)
    public void DarkenSky(float amount, Color fog, float density)
    {
        if (!skySaved)
        {
            skySaved = true;
            fogColor = RenderSettings.fogColor;
            fogDensity = RenderSettings.fogDensity;
            ambient = RenderSettings.ambientIntensity;
        }
        darkFog = fog;
        darkFogDensity = density;
        darkTarget = Mathf.Clamp01(amount);
    }

    public void ShowTitle(string kanji, string read, float seconds)
    {
        title.text = kanji;
        reading.text = read;
        titleTimer = seconds;
        title.transform.localScale = Vector3.one * 1.25f;
    }

    // 화면 베기. onSplit: 조각이 어긋나기 시작하는 순간 (피해 · 낙뢰를 여기서)
    // splitWhen: 이 조건이 참이 되는 순간 어긋남 (납도해서 칼이 들어가는 순간 등). hold는 최소 정지, maxHold가 지나면 무조건
    public void Cut(float angle, float hold, System.Action onSplit, System.Func<bool> splitWhen = null, float maxHold = 1.5f) =>
        StartCoroutine(CutRoutine(angle, hold, onSplit, splitWhen, maxHold));

    public void EndAll()
    {
        Letterbox(false);
        Desaturate(0f);
        darkTarget = 0f;
        titleTimer = Mathf.Min(titleTimer, 0.3f);
    }

    // ===== 진행 =====
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        bar = Mathf.MoveTowards(bar, barTarget, dt * 4f);
        float h = BarHeight * Mathf.SmoothStep(0f, 1f, bar);
        barTop.sizeDelta = new Vector2(0f, h);
        barBottom.sizeDelta = new Vector2(0f, h);

        desat = Mathf.MoveTowards(desat, desatTarget, dt * 3f);
        dark = Mathf.MoveTowards(dark, darkTarget, dt * 1.5f);
        volume.weight = Mathf.Max(desat, dark * 0.6f);
        color.saturation.value = -100f * desat;
        color.postExposure.value = -0.35f * dark; // -0.8 → -0.35 (어두운 영역 색 단계가 깨져 지글거리던 문제)
        vignette.intensity.value = 0.25f + 0.25f * Mathf.Max(desat, dark);

        if (skySaved)
        {
            RenderSettings.fogColor = Color.Lerp(fogColor, darkFog, dark);
            RenderSettings.fogDensity = Mathf.Lerp(fogDensity, darkFogDensity, dark);
            RenderSettings.ambientIntensity = Mathf.Lerp(ambient, ambient * 0.55f, dark); // 덜 어둡게
            if (dark <= 0f && darkTarget <= 0f) RestoreSky();
        }

        flashAmount = Mathf.MoveTowards(flashAmount, 0f, dt * 3.5f);
        flash.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashAmount * flashAmount);

        if (titleTimer > 0f) titleTimer -= dt;
        float ta = titleTimer > 0f ? Mathf.Clamp01(titleTimer / 0.3f) : 0f;
        titleGroup.alpha = Mathf.MoveTowards(titleGroup.alpha, ta, dt * 5f);
        title.transform.localScale = Vector3.Lerp(title.transform.localScale, Vector3.one, dt * 6f);
    }

    void RestoreSky()
    {
        if (!skySaved) return;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.ambientIntensity = ambient;
        skySaved = false;
    }

    // 핵심: 프레임 끝에 화면을 찍어 위 · 아래 조각(선 기준으로 자른 마스크)에 입힘
    //  ① 실선이 0.05초에 화면 끝까지 그어짐 → ② 잠깐 정지 → ③ 두 조각이 선을 따라 반대로 미끄러지며 살짝 벌어짐 → ④ 찍어 둔 화면이 걷히며 실제 화면
    IEnumerator CutRoutine(float angle, float hold, System.Action onSplit, System.Func<bool> splitWhen, float maxHold)
    {
        yield return new WaitForEndOfFrame();
        if (cutShot != null) Destroy(cutShot);
        cutShot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        cutShot.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0);
        cutShot.Apply(false);

        Vector2 size = canvasRect.rect.size;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);
        float rad = angle * Mathf.Deg2Rad;
        Vector2 along = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector2 normal = new Vector2(-along.y, along.x);
        foreach (RectTransform half in new[] { cutTop, cutBottom })
        {
            half.localRotation = rot;
            half.anchoredPosition = Vector2.zero;
        }
        foreach (RawImage img in new[] { cutTopImage, cutBottomImage })
        {
            img.texture = cutShot;
            img.rectTransform.sizeDelta = size;
            img.rectTransform.localRotation = Quaternion.Inverse(rot); // 조각은 기울어도 그림은 똑바로
        }
        cutLine.localRotation = cutGlow.localRotation = rot;
        cutLine.sizeDelta = new Vector2(0f, 3f);
        cutGlow.sizeDelta = new Vector2(0f, 14f);
        SetLineAlpha(1f);
        cutGroup.alpha = 1f;
        cutRoot.SetActive(true);

        float length = size.magnitude * 1.2f;
        for (float t = 0f; t < 0.05f; t += Time.unscaledDeltaTime)
        {
            float k = t / 0.05f;
            cutLine.sizeDelta = new Vector2(length * k, 3f);
            cutGlow.sizeDelta = new Vector2(length * k, 14f);
            yield return null;
        }
        cutLine.sizeDelta = new Vector2(length, 3f);
        cutGlow.sizeDelta = new Vector2(length, 14f);
        for (float t = 0f; t < maxHold; t += Time.unscaledDeltaTime)
        {
            if (t >= hold && (splitWhen == null || splitWhen())) break;
            yield return null;
        }

        if (onSplit != null) onSplit();
        const float slide = 0.5f;
        for (float t = 0f; t < slide; t += Time.unscaledDeltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / slide, 3f); // 처음에 확 어긋나고 천천히 멈춤
            Vector2 offset = along * (54f * k) + normal * (5f * k);
            cutTop.anchoredPosition = offset;
            cutBottom.anchoredPosition = -offset;
            SetLineAlpha(1f - k);
            cutGroup.alpha = 1f - Mathf.Clamp01((t - slide * 0.55f) / (slide * 0.45f));
            yield return null;
        }
        cutRoot.SetActive(false);
        Destroy(cutShot);
        cutShot = null;
    }

    void SetLineAlpha(float a)
    {
        cutLine.GetComponent<Image>().color = new Color(1f, 1f, 1f, a);
        cutGlow.GetComponent<Image>().color = new Color(0.75f, 0.85f, 1f, 0.35f * a);
    }

    // ===== 만들기 =====
    void BuildVolume()
    {
        var go = new GameObject("UltimateVolume");
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 200f; // 발도 자세 볼륨보다 위
        volume.weight = 0f;
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        color = profile.Add<ColorAdjustments>();
        color.saturation.overrideState = true;
        color.postExposure.overrideState = true;
        vignette = profile.Add<Vignette>();
        vignette.intensity.overrideState = true;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = 0.5f;
        volume.profile = profile;
    }

    void BuildOverlay()
    {
        SettingsMenu menu = FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include);
        font = menu != null ? menu.TitleFont : null;

        Canvas canvas = UIKit.CreateCanvas(transform, "UltimateCanvas", 40); // HUD 위, 메뉴 아래
        Overlay = canvas; // [변경] 궁극기 중 이 캔버스만 남기고 나머지 UI를 숨길 때 사용
        Transform root = canvas.transform;
        canvasRect = (RectTransform)root;

        flash = UIKit.Image(root, null, new Color(1f, 1f, 1f, 0f));
        flash.raycastTarget = false;
        UIKit.Stretch(flash.rectTransform);

        barTop = Bar(root, 1f);
        barBottom = Bar(root, 0f);

        var titleRoot = new GameObject("Title", typeof(RectTransform));
        titleRoot.transform.SetParent(root, false);
        UIKit.Stretch((RectTransform)titleRoot.transform);
        titleGroup = titleRoot.AddComponent<CanvasGroup>();
        titleGroup.alpha = 0f;
        titleGroup.blocksRaycasts = false;
        title = UIKit.Text(titleRoot.transform, "", font, 96, Color.white, TextAnchor.MiddleCenter);
        RectTransform t = title.rectTransform;
        t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.2f);
        t.sizeDelta = new Vector2(1400f, 130f);
        title.gameObject.AddComponent<Outline>().effectColor = new Color(0.3f, 0.45f, 1f, 0.8f);
        reading = UIKit.Text(titleRoot.transform, "", font, 28, new Color(0.95f, 0.8f, 0.45f), TextAnchor.MiddleCenter);
        RectTransform rd = reading.rectTransform;
        rd.anchorMin = rd.anchorMax = new Vector2(0.5f, 0.2f);
        rd.anchoredPosition = new Vector2(0f, -80f);
        rd.sizeDelta = new Vector2(800f, 40f);

        BuildCut(root);
    }

    // 화면 베기 조각: 검은 바탕 위에, 선(화면 가운데를 지나는 기울어진 직선)의 위쪽 · 아래쪽만 보이게 자르는 마스크 두 개
    void BuildCut(Transform root)
    {
        cutRoot = new GameObject("Cut", typeof(RectTransform));
        cutRoot.transform.SetParent(root, false);
        UIKit.Stretch((RectTransform)cutRoot.transform);
        cutGroup = cutRoot.AddComponent<CanvasGroup>();
        cutGroup.blocksRaycasts = false;

        Image back = UIKit.Image(cutRoot.transform, null, Color.black); // 벌어진 틈으로 보이는 바탕
        back.raycastTarget = false;
        UIKit.Stretch(back.rectTransform);

        cutTop = Half("Top", 0f, out cutTopImage);
        cutBottom = Half("Bottom", 1f, out cutBottomImage);
        cutGlow = Line("Glow", TaperSprite());
        cutLine = Line("Line", TaperSprite());
        cutRoot.SetActive(false);
    }

    RectTransform Half(string name, float pivotY, out RawImage image)
    {
        Image mask = UIKit.Image(cutRoot.transform, null, Color.white);
        mask.name = name;
        mask.raycastTarget = false;
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        RectTransform r = mask.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, pivotY); // 선에 닿는 변이 회전축
        r.sizeDelta = new Vector2(6000f, 3000f);

        image = new GameObject("Shot", typeof(RectTransform)).AddComponent<RawImage>();
        image.transform.SetParent(r, false);
        image.raycastTarget = false;
        RectTransform ir = image.rectTransform;
        ir.anchorMin = ir.anchorMax = new Vector2(0.5f, pivotY); // 마스크의 회전축 = 화면 가운데
        ir.pivot = new Vector2(0.5f, 0.5f);
        ir.anchoredPosition = Vector2.zero;
        return r;
    }

    RectTransform Line(string name, Sprite sprite)
    {
        Image img = UIKit.Image(cutRoot.transform, sprite, Color.white);
        img.name = name;
        img.type = Image.Type.Simple;
        img.raycastTarget = false;
        RectTransform r = img.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        return r;
    }

    // 양 끝이 가늘게 사라지고 위아래도 부드러운 선 무늬 (칼로 그은 듯 예리하게)
    static Sprite taper;
    static Sprite TaperSprite()
    {
        if (taper != null) return taper;
        const int w = 256, h = 16;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
        {
            float v = 1f - Mathf.Abs((y + 0.5f) / h * 2f - 1f);
            for (int x = 0; x < w; x++)
            {
                float u = 1f - Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                float a = Mathf.Clamp01(u * 6f) * v * v; // 끝 1/6 구간에서 뾰족하게
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        taper = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
        return taper;
    }

    static RectTransform Bar(Transform root, float y)
    {
        Image img = UIKit.Image(root, null, Color.black);
        img.raycastTarget = false;
        RectTransform r = img.rectTransform;
        r.anchorMin = new Vector2(0f, y);
        r.anchorMax = new Vector2(1f, y);
        r.pivot = new Vector2(0.5f, y);
        r.anchoredPosition = Vector2.zero;
        r.sizeDelta = new Vector2(0f, 0f);
        return r;
    }
}
