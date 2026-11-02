using DigitalRuby.ThunderAndLightning;
using UnityEngine;

// 번개 한 종류의 굵기·밝기·갈래·조명 설정 (PlayerVfx, HitFeedback 공용)
[System.Serializable]
public class LightningBoltStyle
{
    public Vector2 width = new Vector2(0.05f, 0.1f);
    public Vector2 duration = new Vector2(0.08f, 0.15f);
    public Vector2Int count = new Vector2Int(1, 1);
    public int generations = 6;
    public float chaos = 0.2f;
    public float forkedness = 0.3f;
    [Range(0f, 1f)] public float growth;
    public float intensity = 2f;
    public float glowIntensity = 1.5f;
    public float glowWidth = 6f;
    public float jitter;
    public Color trunkColor = Color.white;
    public Color glowColor = new Color(0.45f, 0.6f, 1f);

    [Header("Light")]
    public float lightRange;
    public float lightIntensity = 1f;
    [Range(0f, 1f)] public float lightPercent = 0.02f;
    public Color lightColor = new Color(0.6f, 0.75f, 1f);
}

public static class LightningFactory
{
    // 프리팹을 복제해 수동 발사 모드로 만들고 스타일을 적용. 이후 Trigger(start, end)를 부를 때만 번개가 나옴
    public static LightningBoltPrefabScriptBase Create(GameObject prefab, string objectName, LightningBoltStyle style)
    {
        if (prefab == null || style == null) return null;

        GameObject go = Object.Instantiate(prefab);
        go.name = objectName;

        var bolt = go.GetComponent<LightningBoltPrefabScriptBase>();
        if (bolt == null)
        {
            Object.Destroy(go);
            return null;
        }

        bolt.ManualMode = true;
        bolt.CountRange = new RangeOfIntegers { Minimum = style.count.x, Maximum = Mathf.Max(style.count.x, style.count.y) };
        bolt.CountProbabilityModifier = 1f;
        bolt.TrunkWidthRange = new RangeOfFloats { Minimum = style.width.x, Maximum = style.width.y };
        bolt.DurationRange = new RangeOfFloats { Minimum = style.duration.x, Maximum = style.duration.y };
        bolt.Generations = style.generations;
        bolt.ChaosFactor = style.chaos;
        bolt.ChaosFactorForks = style.chaos * 0.8f;
        bolt.Forkedness = style.forkedness;
        bolt.GrowthMultiplier = style.growth;
        bolt.Intensity = style.intensity;
        bolt.GlowIntensity = style.glowIntensity;
        bolt.GlowWidthMultiplier = style.glowWidth;
        bolt.JitterMultiplier = style.jitter;
        bolt.Turbulence = 0f;
        bolt.MainTrunkTintColor = style.trunkColor;
        bolt.LightningTintColor = Color.Lerp(Color.white, style.glowColor, 0.35f);
        bolt.GlowTintColor = style.glowColor;

        bolt.LightParameters = new LightningLightParameters
        {
            LightColor = style.lightColor,
            LightRange = style.lightRange,
            LightIntensity = style.lightIntensity,
            LightPercent = style.lightPercent,
            LightShadowPercent = 0f
        };

        // HDR 프리팹에 기본으로 들어 있는 시작·끝 지점과 흔들림을 비워야 Trigger 좌표 그대로 나옴
        if (bolt is LightningBoltPrefabScript prefabScript)
        {
            prefabScript.Source = null;
            prefabScript.Destination = null;
            prefabScript.StartVariance = Vector3.zero;
            prefabScript.EndVariance = Vector3.zero;
        }
        return bolt;
    }
}
