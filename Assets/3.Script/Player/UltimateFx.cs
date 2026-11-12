using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// 궁극기 화면 연출 (PlayerUltimate가 만들어 씀). 모든 값은 실제 시간으로 움직이고 EndAll로 한꺼번에 원래대로
//  레터박스(위아래 검은 띠) · 기술 이름(한자 + 읽기) · 번쩍임 · 흑백/어둡게(볼륨) · 하늘 어둡게(안개) · 화면을 가르는 베기 선
public class UltimateFx : MonoBehaviour
{
    const float BarHeight = 120f;

    Volume volume;
    ColorAdjustments color;
    Vignette vignette;
    RectTransform barTop, barBottom;
    Image flash, slash;
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
        if (volume != null) Destroy(volume.gameObject);
    }

    // ===== 명령 =====
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

    // 화면을 가로지르는 흰 선이 순식간에 그어졌다가 굵어지며 사라짐
    public void Slash(float angle) => StartCoroutine(SlashRoutine(angle));

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
        color.postExposure.value = -0.8f * dark;
        vignette.intensity.value = 0.25f + 0.25f * Mathf.Max(desat, dark);

        if (skySaved)
        {
            RenderSettings.fogColor = Color.Lerp(fogColor, darkFog, dark);
            RenderSettings.fogDensity = Mathf.Lerp(fogDensity, darkFogDensity, dark);
            RenderSettings.ambientIntensity = Mathf.Lerp(ambient, ambient * 0.35f, dark);
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

    IEnumerator SlashRoutine(float angle)
    {
        RectTransform r = slash.rectTransform;
        r.localRotation = Quaternion.Euler(0f, 0f, angle);
        // 0.08초에 화면 끝까지 그어짐
        for (float t = 0f; t < 0.08f; t += Time.unscaledDeltaTime)
        {
            float k = t / 0.08f;
            r.sizeDelta = new Vector2(3000f * k, 6f);
            slash.color = Color.white;
            yield return null;
        }
        r.sizeDelta = new Vector2(3000f, 6f);
        yield return new WaitForSecondsRealtime(0.35f);
        // 갈라지듯 굵어지며 사라짐
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            float k = t / 0.35f;
            r.sizeDelta = new Vector2(3000f, Mathf.Lerp(6f, 80f, k));
            slash.color = new Color(1f, 1f, 1f, 1f - k);
            yield return null;
        }
        slash.color = new Color(1f, 1f, 1f, 0f);
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
        Transform root = canvas.transform;

        flash = UIKit.Image(root, null, new Color(1f, 1f, 1f, 0f));
        flash.raycastTarget = false;
        UIKit.Stretch(flash.rectTransform);

        slash = UIKit.Image(root, null, new Color(1f, 1f, 1f, 0f));
        slash.raycastTarget = false;
        RectTransform s = slash.rectTransform;
        s.anchorMin = s.anchorMax = new Vector2(0.5f, 0.52f);
        s.sizeDelta = new Vector2(0f, 6f);

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
