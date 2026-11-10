using System;
using UnityEngine;

// 볼륨 설정 (전체·효과음·배경음). PlayerPrefs에 저장되고, 바뀌면 Changed로 알림
// 소리를 내는 쪽은 SfxVolume / BgmVolume을 곱해서 쓰고, ESC 패널 슬라이더는 여기 값을 바꾸기만 하면 됨
public static class AudioOptions
{
    const string MasterKey = "Audio.Master";
    const string SfxKey = "Audio.Sfx";
    const string BgmKey = "Audio.Bgm";

    static bool loaded;
    static float master = 1f, sfx = 1f, bgm = 0.7f;

    public static event Action Changed;

    public static float Master { get { Load(); return master; } set => Set(ref master, MasterKey, value); }
    public static float Sfx { get { Load(); return sfx; } set => Set(ref sfx, SfxKey, value); }
    public static float Bgm { get { Load(); return bgm; } set => Set(ref bgm, BgmKey, value); }

    public static float SfxVolume => Master * Sfx;
    public static float BgmVolume => Master * Bgm;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        master = PlayerPrefs.GetFloat(MasterKey, master);
        sfx = PlayerPrefs.GetFloat(SfxKey, sfx);
        bgm = PlayerPrefs.GetFloat(BgmKey, bgm);
    }

    static void Set(ref float field, string key, float value)
    {
        Load();
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(field, value)) return;
        field = value;
        PlayerPrefs.SetFloat(key, value);
        Changed?.Invoke();
    }
}
