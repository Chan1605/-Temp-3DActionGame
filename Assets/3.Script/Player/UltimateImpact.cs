using UnityEngine;

// 낙뢰가 떨어진 자리: 바닥을 따라 퍼지는 빛 고리 + 잠깐 번쩍이는 불빛. 스스로 사라짐
// 번개 줄기는 화면을 향한 선이라 평면처럼 보이기 쉬워서, 바닥에 붙은 고리와 주변을 비추는 빛으로 깊이감을 줌
public class UltimateImpact : MonoBehaviour
{
    const int Segments = 40;
    const float Life = 0.38f;
    static Material shared;

    LineRenderer ring;
    Light glow;
    float radius, age;
    static readonly Color Tint = new Color(0.7f, 0.82f, 1f);

    public static void Spawn(Vector3 ground, float radius)
    {
        var go = new GameObject("UltimateImpact");
        go.transform.position = ground + Vector3.up * 0.06f;
        go.AddComponent<UltimateImpact>().radius = radius;
    }

    void Awake()
    {
        if (shared == null) shared = new Material(Shader.Find("Sprites/Default"));
        ring = gameObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = shared;
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = Segments;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.alignment = LineAlignment.TransformZ; // 바닥에 눕힘
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0f, -1.2f); // 눕혀 놨으므로 -Z가 위
        glow = lightGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = Tint;
        glow.shadows = LightShadows.None;
        Apply(0f);
    }

    void Update()
    {
        age += Time.unscaledDeltaTime;
        if (age >= Life)
        {
            Destroy(gameObject);
            return;
        }
        Apply(age / Life);
    }

    void Apply(float k)
    {
        float r = Mathf.Lerp(0.2f, radius, 1f - (1f - k) * (1f - k)); // 처음에 빠르게 퍼짐
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
        }
        float fade = 1f - k;
        ring.widthMultiplier = Mathf.Lerp(0.22f, 0.02f, k);
        Color c = new Color(Tint.r, Tint.g, Tint.b, fade);
        ring.startColor = ring.endColor = c;
        glow.range = radius * 3.5f;
        glow.intensity = 6f * fade * fade;
    }
}
