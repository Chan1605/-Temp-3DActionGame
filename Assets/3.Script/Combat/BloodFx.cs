using UnityEngine;

// 피격 출혈: 적이 피해를 받을 때 맞은 자리에서 핏방울이 베인 방향으로 튐 (에셋 없이 코드로 만든 파티클 하나를 재사용)
//  - 핏방울: 속도 방향으로 늘어나는 짧은 줄기 + 중력 / 피안개: 잠깐 번지는 붉은 연기
//  - 실제 시간으로 움직여 히트스톱 · 슬로 중에도 흩날림
//  - EnemySwordsman(보스 포함)이 피해 처리 때 호출. 허수아비는 호출하지 않음
public static class BloodFx
{
    public static bool Enabled = true;

    static readonly Color DropDark = new Color(0.32f, 0.01f, 0.02f, 1f);
    static readonly Color DropBright = new Color(0.62f, 0.03f, 0.04f, 1f);
    static readonly Color Mist = new Color(0.45f, 0.02f, 0.03f, 0.35f);

    static ParticleSystem drops, mist;

    // weight별 핏방울 수. 치명타 · 마무리 일격은 더 많이
    public static void Spawn(Vector3 point, Vector3 direction, AttackWeight weight, bool lethal)
    {
        if (!Enabled) return;
        if (drops == null) Build();

        int count = weight == AttackWeight.Light ? 10 : weight == AttackWeight.Heavy ? 16 : weight == AttackWeight.Execution ? 36 : 22;
        if (lethal) count = Mathf.RoundToInt(count * 1.6f);

        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;

        // 핵심: 대부분은 베인 방향(공격자 반대편)으로, 일부는 칼이 지나간 옆 방향으로 흩어짐
        var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < count; i++)
        {
            Vector3 v = i % 3 == 2
                ? side * (Random.value < 0.5f ? -1f : 1f) * Random.Range(1.5f, 3.5f)
                : dir * Random.Range(2.5f, 6f);
            v += Random.insideUnitSphere * 1.6f + Vector3.up * Random.Range(0.8f, 2.6f);
            p.position = point + Random.insideUnitSphere * 0.08f;
            p.velocity = v;
            p.startSize = Random.Range(0.035f, 0.09f);
            p.startLifetime = Random.Range(0.35f, 0.75f);
            p.startColor = Color.Lerp(DropDark, DropBright, Random.value);
            drops.Emit(p, 1);
        }

        var m = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        int puffs = lethal ? 4 : 2;
        for (int i = 0; i < puffs; i++)
        {
            m.position = point + dir * 0.15f + Random.insideUnitSphere * 0.1f;
            m.velocity = dir * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * 0.3f;
            m.startSize = Random.Range(0.35f, 0.6f) * (lethal ? 1.4f : 1f);
            m.startLifetime = Random.Range(0.25f, 0.4f);
            m.startColor = Mist;
            mist.Emit(m, 1);
        }
    }

    static void Build()
    {
        Material material = new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftDot(32) };

        drops = Create("BloodDrops", material, 600, 2.2f);
        var renderer = drops.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch; // 속도 방향으로 늘어나 튀는 줄기처럼
        renderer.velocityScale = 0.045f;
        renderer.lengthScale = 1.2f;
        var size = drops.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        mist = Create("BloodMist", material, 80, 0f);
        var color = mist.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;
        var grow = mist.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
    }

    static ParticleSystem Create(string name, Material material, int maxParticles, float gravity)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.gravityModifier = gravity;
        main.startSpeed = 0f;

        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        ps.Play();
        return ps;
    }

    // 가장자리가 부드러운 동그란 점 텍스처
    static Texture2D SoftDot(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = Mathf.Clamp01((1f - d) * 2.2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }
}
