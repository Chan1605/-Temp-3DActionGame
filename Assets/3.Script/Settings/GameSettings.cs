using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 게임 설정 (화면·조작·그래픽). PlayerPrefs에 저장, 시작할 때 자동 적용. 볼륨은 AudioOptions
//  - 화면(해상도·창 모드·VSync·최대 FPS)은 SetDisplay로 한 번에 적용 (설정 화면의 Apply)
//  - 밝기·감도·데미지 숫자·그래픽은 바꾸는 즉시 적용
// 그래픽 항목은 카메라 단위 값만 바꿈 (URP 에셋을 바꾸면 에디터에서 에셋 자체가 바뀌어 버림)
public static class GameSettings
{
    const string ResKey = "Display.Resolution";
    const string WindowedKey = "Display.Windowed";
    const string VSyncKey = "Display.VSync";
    const string FpsKey = "Display.MaxFps";
    const string BrightKey = "Display.Brightness";
    const string SensKey = "Gameplay.Sensitivity";
    const string DamageKey = "Gameplay.ShowDamage"; // [변경] 키 이름을 바꿔 테스트 중 저장된 값 대신 새 기본값(끔) 적용
    const string AAKey = "Graphics.AntiAliasing";
    const string ShadowKey = "Graphics.Shadows";

    public static readonly int[] FpsOptions = { 30, 60, 120, 144, -1 }; // -1 = 제한 없음
    public static readonly string[] AAOptions = { "Off", "FXAA", "SMAA", "TAA" };

    public static event Action Changed;

    static bool loaded;
    static readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    static int resolutionIndex, fpsIndex = 1, antiAliasing = -1; // -1 = 카메라 원래 값 유지
    static bool windowed, vSync = true, showDamage, shadows = true; // [변경] 데미지 숫자 기본 끔
    static float brightness = 1f, sensitivity = 50f;
    static Volume brightnessVolume;
    static ColorAdjustments colorAdjust;

    public static IReadOnlyList<Vector2Int> Resolutions { get { Load(); return resolutions; } }
    public static int ResolutionIndex { get { Load(); return resolutionIndex; } }
    public static bool Windowed { get { Load(); return windowed; } }
    public static bool VSync { get { Load(); return vSync; } }
    public static int FpsIndex { get { Load(); return fpsIndex; } }

    public static float Brightness { get { Load(); return brightness; } set { Load(); brightness = Mathf.Clamp(value, 0.5f, 1.5f); PlayerPrefs.SetFloat(BrightKey, brightness); ApplyBrightness(); Changed?.Invoke(); } }
    public static float Sensitivity { get { Load(); return sensitivity; } set { Load(); sensitivity = Mathf.Clamp(value, 1f, 100f); PlayerPrefs.SetFloat(SensKey, sensitivity); Changed?.Invoke(); } }
    public static bool ShowDamageNumbers { get { Load(); return showDamage; } set { Load(); showDamage = value; PlayerPrefs.SetInt(DamageKey, value ? 1 : 0); Changed?.Invoke(); } }
    public static int AntiAliasing { get { Load(); return antiAliasing < 0 ? CameraAA() : antiAliasing; } set { Load(); antiAliasing = Mathf.Clamp(value, 0, AAOptions.Length - 1); PlayerPrefs.SetInt(AAKey, antiAliasing); ApplyCamera(); Changed?.Invoke(); } }
    public static bool Shadows { get { Load(); return shadows; } set { Load(); shadows = value; PlayerPrefs.SetInt(ShadowKey, value ? 1 : 0); ApplyCamera(); Changed?.Invoke(); } }

    // 감도 50 = 원래 배속(1배). 1 → 0.25배, 100 → 2.5배
    public static float SensitivityMultiplier
    {
        get
        {
            float s = Sensitivity;
            return s <= 50f ? Mathf.Lerp(0.25f, 1f, (s - 1f) / 49f) : Mathf.Lerp(1f, 2.5f, (s - 50f) / 50f);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ApplyOnStart()
    {
        Load();
        ApplyDisplay();
        ApplyBrightness();
        ApplyCamera();
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;

        // 주사율만 다른 같은 해상도는 하나로
        resolutions.Clear();
        foreach (Resolution r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!resolutions.Contains(size)) resolutions.Add(size);
        }
        if (resolutions.Count == 0) resolutions.Add(new Vector2Int(Screen.width, Screen.height));
        resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        int current = resolutions.FindIndex(r => r.x == Screen.width && r.y == Screen.height);
        resolutionIndex = Mathf.Clamp(PlayerPrefs.GetInt(ResKey, current >= 0 ? current : resolutions.Count - 1), 0, resolutions.Count - 1);
        windowed = PlayerPrefs.GetInt(WindowedKey, Screen.fullScreenMode == FullScreenMode.Windowed ? 1 : 0) == 1;
        vSync = PlayerPrefs.GetInt(VSyncKey, 1) == 1;
        fpsIndex = Mathf.Clamp(PlayerPrefs.GetInt(FpsKey, 1), 0, FpsOptions.Length - 1);
        brightness = PlayerPrefs.GetFloat(BrightKey, 1f);
        sensitivity = PlayerPrefs.GetFloat(SensKey, 50f);
        showDamage = PlayerPrefs.GetInt(DamageKey, 0) == 1; // [변경]
        antiAliasing = PlayerPrefs.GetInt(AAKey, -1);
        shadows = PlayerPrefs.GetInt(ShadowKey, 1) == 1;
    }

    // 화면 설정은 Apply 버튼으로 한 번에
    public static void SetDisplay(int resolution, bool isWindowed, bool useVSync, int fps)
    {
        Load();
        resolutionIndex = Mathf.Clamp(resolution, 0, resolutions.Count - 1);
        windowed = isWindowed;
        vSync = useVSync;
        fpsIndex = Mathf.Clamp(fps, 0, FpsOptions.Length - 1);
        PlayerPrefs.SetInt(ResKey, resolutionIndex);
        PlayerPrefs.SetInt(WindowedKey, windowed ? 1 : 0);
        PlayerPrefs.SetInt(VSyncKey, vSync ? 1 : 0);
        PlayerPrefs.SetInt(FpsKey, fpsIndex);
        ApplyDisplay();
        Changed?.Invoke();
    }

    public static void RestoreDefaults()
    {
        Load();
        int native = resolutions.FindIndex(r => r.x == Screen.currentResolution.width && r.y == Screen.currentResolution.height);
        SetDisplay(native >= 0 ? native : resolutions.Count - 1, false, true, 1);
        Brightness = 1f;
        Sensitivity = 50f;
        ShowDamageNumbers = false; // [변경]
        Shadows = true;
        antiAliasing = -1;
        PlayerPrefs.DeleteKey(AAKey);
        AudioOptions.Master = 1f;
        AudioOptions.Sfx = 1f;
        AudioOptions.Bgm = 0.7f;
        Changed?.Invoke();
    }

    static void ApplyDisplay()
    {
        Vector2Int size = resolutions[resolutionIndex];
#if !UNITY_EDITOR
        Screen.SetResolution(size.x, size.y, windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow);
#endif
        QualitySettings.vSyncCount = vSync ? 1 : 0;
        Application.targetFrameRate = vSync ? -1 : FpsOptions[fpsIndex];
    }

    // 밝기: 낮은 우선순위 전역 볼륨의 노출값 (발도 자세 흑백 등 다른 볼륨과 겹쳐도 그대로 동작)
    static void ApplyBrightness()
    {
        if (brightnessVolume == null)
        {
            var go = new GameObject("SettingsVolume");
            UnityEngine.Object.DontDestroyOnLoad(go);
            brightnessVolume = go.AddComponent<Volume>();
            brightnessVolume.isGlobal = true;
            brightnessVolume.priority = -50f;
            brightnessVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            colorAdjust = brightnessVolume.profile.Add<ColorAdjustments>(true);
            colorAdjust.SetAllOverridesTo(false);
        }
        colorAdjust.postExposure.overrideState = !Mathf.Approximately(brightness, 1f);
        colorAdjust.postExposure.value = Mathf.Log(brightness, 2f);
    }

    public static void ApplyCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null) return;
        if (antiAliasing >= 0) data.antialiasing = (AntialiasingMode)antiAliasing; // Off·FXAA·SMAA·TAA 순서가 enum과 같음
        data.renderShadows = shadows;
    }

    static int CameraAA()
    {
        Camera cam = Camera.main;
        var data = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
        return data != null ? Mathf.Clamp((int)data.antialiasing, 0, AAOptions.Length - 1) : 0;
    }
}
