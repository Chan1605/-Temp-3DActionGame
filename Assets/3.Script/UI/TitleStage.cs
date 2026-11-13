using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 타이틀 뒤 3D 무대: 폭풍 치는 밤, 산 능선 · 비 · 안개 · 재, 발도 자세로 선 검객. TitleScreen이 실행할 때 붙여서 코드로 만듦
//  - FlashLevel: 번개 밝기(0~1). 조명 · 하늘 · 안개 · 블룸이 따라 밝아짐
//  - Strike(): 번개 줄기 새로 그리기 + 칼 주변 스파크 + 카메라 흔들림
//  - MenuBlend: 0 = 넓은 화면(로고 가운데), 1 = 검객을 오른쪽에 두고 다가간 화면(메뉴)
//  - PlayRelease(): 게임 시작할 때 발도 베기
public class TitleStage : MonoBehaviour
{
    static readonly Color SkyColor = new Color(0.07f, 0.085f, 0.13f);
    static readonly Color FogColor = new Color(0.035f, 0.045f, 0.07f);
    static readonly Color Bolt = new Color(0.62f, 0.82f, 1f);
    static readonly Color Ember = new Color(1f, 0.7f, 0.36f);
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    struct View
    {
        public Vector3 position, look;
        public float fov;
        public View(Vector3 position, Vector3 look, float fov) { this.position = position; this.look = look; this.fov = fov; }
    }

    // 검객은 원점. 넓은 화면은 가운데 아래, 메뉴 화면은 오른쪽 1/3에 오게
    static readonly View Wide = new View(new Vector3(0f, 1.15f, -10.5f), new Vector3(0f, 2.35f, 0f), 34f);
    static readonly View Close = new View(new Vector3(-1.85f, 1.3f, -5.4f), new Vector3(-1.05f, 1.45f, 0f), 32f);

    public float FlashLevel { get; set; }
    public float MenuBlend { get; set; }

    readonly List<Object> owned = new List<Object>();
    Camera cam;
    Animator animator;
    string releaseState;
    Light lightning, bladeLight;
    Material skyMaterial;
    LineRenderer bolt, branch;
    ParticleSystem sparks;
    Bloom bloom;
    ChromaticAberration chroma;
    float intro, shake;
    Vector2 parallax;

    public void Build(GameObject characterSource, string idleState, string release, float characterYaw)
    {
        releaseState = release;
        cam = Camera.main;
        SetupCamera();
        SetupAtmosphere();
        BuildBackdrop();
        BuildGround();
        BuildLights();
        BuildCharacter(characterSource, idleState, characterYaw);
        BuildParticles();
        BuildPost();
        LateUpdate();
    }

    void OnDestroy()
    {
        foreach (Object o in owned)
            if (o != null) Destroy(o);
    }

    T Own<T>(T o) where T : Object
    {
        owned.Add(o);
        return o;
    }

    // ===== 카메라 · 대기 =====
    void SetupCamera()
    {
        if (cam == null) return;
        cam.cullingMask = ~0; // 예전 타이틀은 UI만 그려서 0이었음
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = FogColor;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 200f;
        cam.allowHDR = true;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
    }

    void SetupAtmosphere()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.06f, 0.09f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.03f;
        RenderSettings.fogColor = FogColor;

        // 젖은 땅이 기본 하늘(회색)을 비추지 않게 반사도 어두운 색으로
        var cube = Own(new Cubemap(4, TextureFormat.RGBA32, false));
        var reflect = new Color(0.02f, 0.025f, 0.04f);
        for (int face = 0; face < 6; face++)
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    cube.SetPixel((CubemapFace)face, x, y, reflect);
        cube.Apply();
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = cube;
        DynamicGI.UpdateEnvironment();
    }

    // ===== 배경: 폭풍 구름 + 두 겹 산 능선 + 번개 줄기 =====
    void BuildBackdrop()
    {
        GameObject sky = GameObject.CreatePrimitive(PrimitiveType.Quad);
        sky.name = "StormSky";
        Destroy(sky.GetComponent<Collider>());
        sky.transform.SetParent(transform, false);
        sky.transform.position = new Vector3(0f, 9f, 34f);
        sky.transform.localScale = new Vector3(110f, 46f, 1f);
        skyMaterial = Own(new Material(FindShader("Universal Render Pipeline/Unlit")));
        Texture2D clouds = Own(CloudTexture(512, 256));
        skyMaterial.mainTexture = clouds;
        skyMaterial.SetTexture("_BaseMap", clouds);
        skyMaterial.SetColor(BaseColorId, SkyColor);
        sky.GetComponent<Renderer>().sharedMaterial = skyMaterial;

        Ridge("FarRidge", new Vector3(0f, 3f, 27f), new Vector2(90f, 14f), new Color(0.045f, 0.058f, 0.09f), 11);
        Ridge("NearRidge", new Vector3(3f, 1.2f, 19f), new Vector2(70f, 7f), new Color(0.022f, 0.027f, 0.04f), 37);

        Material boltMat = Own(new Material(Shader.Find("Sprites/Default")));
        bolt = NewLine("Bolt", boltMat, 0.32f);
        branch = NewLine("BoltBranch", boltMat, 0.14f);
    }

    void Ridge(string name, Vector3 position, Vector2 size, Color color, int seed)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(transform, false);
        q.transform.position = position;
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        Material m = Own(new Material(Shader.Find("Sprites/Default")));
        m.mainTexture = Own(RidgeTexture(1024, 128, seed));
        m.color = color;
        Renderer r = q.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    LineRenderer NewLine(string name, Material material, float width)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false);
        line.sharedMaterial = material;
        line.widthMultiplier = width;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f));
        line.startColor = line.endColor = new Color(0.86f, 0.93f, 1f);
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.numCapVertices = 2;
        line.enabled = false;
        return line;
    }

    // 중점을 흔들어 나누는 번개 모양 (멀리 하늘 → 능선 뒤)
    void DrawBolt()
    {
        float x = Random.Range(-16f, 16f);
        if (Mathf.Abs(x) < 4f) x = 6f * Mathf.Sign(x == 0f ? 1f : x); // 검객 머리 바로 뒤는 피함
        var top = new Vector3(x + Random.Range(-4f, 4f), 26f, 30f);
        var bottom = new Vector3(x, 1f, 28f);
        List<Vector3> points = Jagged(top, bottom, 5, 3.2f);
        bolt.positionCount = points.Count;
        bolt.SetPositions(points.ToArray());

        int from = Random.Range(points.Count / 4, points.Count / 2);
        Vector3 start = points[from];
        Vector3 end = start + new Vector3(Random.Range(4f, 8f) * (Random.value < 0.5f ? -1f : 1f), -Random.Range(6f, 10f), 0f);
        List<Vector3> twig = Jagged(start, end, 4, 1.6f);
        branch.positionCount = twig.Count;
        branch.SetPositions(twig.ToArray());
    }

    static List<Vector3> Jagged(Vector3 a, Vector3 b, int depth, float spread)
    {
        var points = new List<Vector3> { a, b };
        for (int d = 0; d < depth; d++)
        {
            var next = new List<Vector3>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                next.Add(points[i]);
                Vector3 mid = (points[i] + points[i + 1]) * 0.5f;
                mid.x += Random.Range(-spread, spread);
                next.Add(mid);
            }
            next.Add(points[points.Count - 1]);
            points = next;
            spread *= 0.55f;
        }
        return points;
    }

    // ===== 땅 · 조명 =====
    void BuildGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "WetGround";
        Destroy(ground.GetComponent<Collider>());
        ground.transform.SetParent(transform, false);
        ground.transform.localScale = new Vector3(12f, 1f, 12f);
        Material m = Own(new Material(FindShader("Universal Render Pipeline/Lit")));
        m.SetColor(BaseColorId, new Color(0.03f, 0.034f, 0.042f));
        m.SetFloat("_Smoothness", 0.72f); // 비에 젖은 바닥: 뒤에서 오는 달빛이 길게 번들거림
        m.SetFloat("_Metallic", 0f);
        ground.GetComponent<Renderer>().sharedMaterial = m;
    }

    void BuildLights()
    {
        // 기존 씬 조명이 있으면 끔 (연출을 이 무대 조명으로만)
        foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.enabled = false;

        Light moon = NewLight("MoonRim", LightType.Directional, new Color(0.55f, 0.66f, 1f), 1.25f);
        moon.transform.rotation = Quaternion.Euler(24f, 160f, 0f); // 뒤 · 왼쪽 위에서 → 윤곽선 + 젖은 바닥 반사
        moon.shadows = LightShadows.Soft;
        moon.shadowStrength = 0.85f;
        RenderSettings.sun = moon;

        Light fill = NewLight("Fill", LightType.Directional, new Color(0.55f, 0.6f, 0.78f), 0.22f);
        fill.transform.rotation = Quaternion.Euler(28f, -40f, 0f);

        lightning = NewLight("Lightning", LightType.Directional, Bolt, 0f);
        lightning.transform.rotation = Quaternion.Euler(35f, 200f, 0f);

        Light warm = NewLight("EmberGlow", LightType.Point, Ember, 0.9f);
        warm.transform.position = new Vector3(-1.4f, 0.35f, -0.9f);
        warm.range = 4.5f;
    }

    Light NewLight(string name, LightType type, Color color, float intensity)
    {
        var l = new GameObject(name).AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.type = type;
        l.color = color;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        return l;
    }

    // ===== 검객: Player 프리팹에서 모델(Animator)만 떼어 와 스크립트 · 판정을 지움 =====
    void BuildCharacter(GameObject source, string idleState, float yaw)
    {
        Animator sourceAnimator = source != null ? source.GetComponentInChildren<Animator>(true) : null;
        if (sourceAnimator == null) return;

        var holder = new GameObject("Swordsman");
        holder.SetActive(false); // 복사본의 Awake가 돌기 전에 정리
        holder.transform.SetParent(transform, false);
        holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        GameObject model = Instantiate(sourceAnimator.gameObject, holder.transform);
        model.transform.localPosition = sourceAnimator.transform.localPosition;
        model.transform.localRotation = Quaternion.identity;
        Strip(model);
        holder.SetActive(true);

        animator = model.GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        int idle = Animator.StringToHash(idleState);
        if (animator.HasState(0, idle)) animator.Play(idle, 0, 0f);

        Transform blade = FindDeep(model.transform, "SamuraiKatana") ?? FindDeep(model.transform, "WeaponAdjust") ?? FindDeep(model.transform, "hand_r");
        if (blade == null) blade = model.transform;
        bladeLight = NewLight("BladeLight", LightType.Point, Bolt, 0.5f);
        bladeLight.transform.SetParent(blade, false);
        bladeLight.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        bladeLight.range = 2.6f;
        sparks = SparkParticles(blade);
    }

    // 게임용 컴포넌트(판정 · 트레일 · 이펙트 · 소리)를 없애고 겉모습만 남김. 칼 잡는 자리 조정(KatanaGrip)은 유지
    static void Strip(GameObject model)
    {
        for (int pass = 0; pass < 2; pass++) // 서로 의존하는 컴포넌트가 있어 두 번
            foreach (MonoBehaviour mb in model.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && !(mb is KatanaGrip)) DestroyImmediate(mb);
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (Rigidbody r in model.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(r);
        foreach (AudioSource a in model.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
        foreach (TrailRenderer t in model.GetComponentsInChildren<TrailRenderer>(true)) t.enabled = false;
        foreach (ParticleSystem ps in model.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
        }
    }

    public void PlayRelease()
    {
        if (animator != null)
        {
            int release = Animator.StringToHash(releaseState);
            if (animator.HasState(0, release)) animator.CrossFadeInFixedTime(release, 0.05f, 0);
        }
        if (sparks != null) sparks.Emit(60);
        shake = 1f;
    }

    // ===== 파티클: 비 · 바닥 안개 · 떠오르는 불티 · 칼 스파크 =====
    void BuildParticles()
    {
        Texture2D dot = Own(SoftDot(64));
        Material soft = Own(new Material(Shader.Find("Sprites/Default")) { mainTexture = dot });
        Material plain = Own(new Material(Shader.Find("Sprites/Default")));

        // 비: 길게 늘인 빗줄기, 바람에 살짝 기울어짐
        ParticleSystem rain = NewParticles("Rain", plain, new Vector3(0f, 9f, 1f), 3000);
        ParticleSystem.MainModule main = rain.main;
        main.startLifetime = 0.9f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
        main.startColor = new Color(0.72f, 0.8f, 0.95f, 0.22f);
        Shape(rain, new Vector3(28f, 0.1f, 24f));
        Velocity(rain, new Vector3(-2.2f, -19f, 0f), new Vector3(-1.6f, -16f, 0f));
        Rate(rain, 1800f);
        Stretch(rain, 0.04f, 1f);

        // 바닥 안개: 크고 흐린 덩어리가 천천히 흘러감
        ParticleSystem mist = NewParticles("GroundMist", soft, new Vector3(0f, 0.5f, 5f), 80);
        main = mist.main;
        main.startLifetime = 16f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(5f, 10f);
        main.startColor = new Color(0.5f, 0.58f, 0.72f, 0.07f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        Shape(mist, new Vector3(30f, 0.6f, 18f));
        Velocity(mist, new Vector3(0.15f, 0f, -0.05f), new Vector3(0.45f, 0.03f, 0.05f));
        Rate(mist, 5f);
        FadeInOut(mist);

        // 불티: 금빛 점이 떠올라 흔들리며 사라짐
        ParticleSystem embers = NewParticles("Embers", soft, new Vector3(0f, 0.3f, 1.5f), 120);
        main = embers.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
        main.startColor = Ember;
        Shape(embers, new Vector3(12f, 0.5f, 9f));
        Velocity(embers, new Vector3(-0.05f, 0.2f, -0.05f), new Vector3(0.15f, 0.55f, 0.05f));
        Rate(embers, 11f);
        FadeInOut(embers);
        ParticleSystem.NoiseModule noise = embers.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.2f;

        foreach (ParticleSystem ps in new[] { rain, mist, embers })
        {
            ParticleSystem.MainModule m = ps.main;
            m.prewarm = true;
            ps.Play();
        }
    }

    ParticleSystem SparkParticles(Transform parent)
    {
        Material plain = Own(new Material(Shader.Find("Sprites/Default")));
        var go = new GameObject("BladeSparks");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
        main.startColor = new Color(0.85f, 0.94f, 1f);
        main.maxParticles = 200;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.45f;
        Rate(ps, 7f);
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = plain;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.05f;
        r.lengthScale = 1f;
        ps.Play();
        return ps;
    }

    ParticleSystem NewParticles(string name, Material material, Vector3 position, int max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.maxParticles = max;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static void Shape(ParticleSystem ps, Vector3 box)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;
    }

    static void Velocity(ParticleSystem ps, Vector3 min, Vector3 max)
    {
        ParticleSystem.VelocityOverLifetimeModule v = ps.velocityOverLifetime;
        v.enabled = true;
        v.space = ParticleSystemSimulationSpace.World;
        v.x = new ParticleSystem.MinMaxCurve(min.x, max.x);
        v.y = new ParticleSystem.MinMaxCurve(min.y, max.y);
        v.z = new ParticleSystem.MinMaxCurve(min.z, max.z);
    }

    static void Rate(ParticleSystem ps, float rate)
    {
        ParticleSystem.EmissionModule e = ps.emission;
        e.enabled = true;
        e.rateOverTime = rate;
    }

    static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = velocityScale;
        r.lengthScale = lengthScale;
    }

    static void FadeInOut(ParticleSystem ps)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    // ===== 후처리: 블룸 · 비네트 · 필름 그레인 · 색 분리(번개 때 강해짐) · 청록/주황 톤 =====
    void BuildPost()
    {
        var go = new GameObject("TitleVolume");
        go.transform.SetParent(transform, false);
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        VolumeProfile profile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
        volume.sharedProfile = profile;

        bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.8f);
        bloom.intensity.Override(1.1f);
        bloom.scatter.Override(0.72f);

        Tonemapping tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);

        ColorAdjustments color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.35f);
        color.contrast.Override(18f);
        color.saturation.Override(-12f);
        color.colorFilter.Override(Color.white);
        color.hueShift.Override(0f);

        SplitToning split = profile.Add<SplitToning>(true);
        split.shadows.Override(new Color(0.36f, 0.45f, 0.6f));
        split.highlights.Override(new Color(0.72f, 0.6f, 0.45f));
        split.balance.Override(-15f);

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.color.Override(Color.black);
        vignette.intensity.Override(0.42f);
        vignette.smoothness.Override(0.5f);
        vignette.center.Override(new Vector2(0.5f, 0.5f));
        vignette.rounded.Override(false);

        FilmGrain grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Medium2);
        grain.intensity.Override(0.32f);
        grain.response.Override(0.8f);

        chroma = profile.Add<ChromaticAberration>(true);
        chroma.intensity.Override(0.06f);
    }

    // ===== 매 프레임 =====
    void Update()
    {
        float f = FlashLevel;
        float t = Time.unscaledTime;
        if (lightning != null) lightning.intensity = 7f * f;
        if (skyMaterial != null) skyMaterial.SetColor(BaseColorId, SkyColor * (1f + 9f * f));
        RenderSettings.fogColor = Color.Lerp(FogColor, Bolt * 0.32f, f * 0.75f);
        if (cam != null) cam.backgroundColor = RenderSettings.fogColor;
        if (bloom != null) bloom.intensity.value = 1.1f + 2.2f * f;
        if (chroma != null) chroma.intensity.value = 0.06f + 0.55f * f + 0.3f * shake;

        // 번개 줄기는 밝을 때만, 끊기듯 깜빡이며
        bool showBolt = f > 0.3f && Mathf.Sin(t * 90f) > -0.4f;
        if (bolt != null) bolt.enabled = showBolt;
        if (branch != null) branch.enabled = showBolt && f > 0.55f;

        // 칼에 머문 뇌기: 불규칙하게 지직거리는 빛
        if (bladeLight != null)
            bladeLight.intensity = 0.35f + 0.35f * Mathf.PerlinNoise(t * 9f, 0.3f) + (Random.value < 0.04f ? 1.2f : 0f) + 3f * f;
    }

    public void Strike()
    {
        DrawBolt();
        if (sparks != null) sparks.Emit(28);
        shake = Mathf.Max(shake, 0.6f);
    }

    void LateUpdate()
    {
        if (cam == null) return;
        float dt = Time.unscaledDeltaTime;
        intro = Mathf.MoveTowards(intro, 1f, dt / 6f);
        shake = Mathf.MoveTowards(shake, 0f, dt * 2.5f);

        float k = Smoother(Mathf.Clamp01(MenuBlend));
        Vector3 position = Vector3.Lerp(Wide.position, Close.position, k);
        Vector3 look = Vector3.Lerp(Wide.look, Close.look, k);
        float fov = Mathf.Lerp(Wide.fov, Close.fov, k);

        // 시작할 때 천천히 다가오는 카메라 + 숨 쉬듯 흔들림 + 마우스 따라 살짝 시차
        position += (position - look).normalized * 3f * (1f - Smoother(intro));
        float t = Time.unscaledTime;
        position += new Vector3(Mathf.Sin(t * 0.21f) * 0.12f, Mathf.Sin(t * 0.33f) * 0.05f, Mathf.Sin(t * 0.17f) * 0.08f);
        Vector2 target = Vector2.zero;
        if (Mouse.current != null && Screen.width > 0)
        {
            Vector2 m = Mouse.current.position.ReadValue();
            target = new Vector2(m.x / Screen.width - 0.5f, m.y / Screen.height - 0.5f) * 2f;
            target = Vector2.ClampMagnitude(target, 1.5f);
        }
        parallax = Vector2.Lerp(parallax, target, dt * 1.5f);
        Quaternion facing = Quaternion.LookRotation(look - position);
        position += facing * new Vector3(parallax.x * 0.2f, parallax.y * 0.08f, 0f);
        position += Random.insideUnitSphere * 0.035f * shake * shake;

        cam.transform.position = position;
        cam.transform.rotation = Quaternion.LookRotation(look - position);
        cam.fieldOfView = fov;
    }

    static float Smoother(float x) => x * x * x * (x * (x * 6f - 15f) + 10f);

    // ===== 코드로 만드는 텍스처 =====
    static Shader FindShader(string name)
    {
        Shader s = Shader.Find(name);
        return s != null ? s : Shader.Find("Sprites/Default");
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f, amp = 0.5f, freq = 1f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq, y * freq);
            freq *= 2.03f;
            amp *= 0.5f;
        }
        return sum;
    }

    // 먹구름: 위는 어둡고 지평선 쪽이 조금 밝은 덩어리 구름 (번개 때 머티리얼 색을 올려 밝힘)
    static Texture2D CloudTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);
            float horizon = Mathf.Lerp(1.15f, 0.35f, Mathf.SmoothStep(0f, 1f, v));
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w;
                float n = Fbm(u * 6f + 13.7f, v * 3f + 2.1f, 5);
                float billow = Mathf.SmoothStep(0.35f, 0.75f, n);
                float c = Mathf.Clamp01((0.25f + 0.85f * billow) * horizon);
                byte b = (byte)(c * 255f);
                pixels[y * w + x] = new Color32(b, b, b, 255);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // 산 능선 실루엣: 아래는 채우고 위는 투명
    static Texture2D RidgeTexture(int w, int h, int seed)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[w * h];
        for (int x = 0; x < w; x++)
        {
            float u = x / (float)w;
            float ridge = 0.3f + 0.55f * Fbm(u * 5f + seed, seed * 0.37f, 6);
            ridge += 0.12f * Mathf.Abs(Mathf.Sin(u * 9f + seed)); // 봉우리
            float top = Mathf.Clamp01(ridge) * h;
            for (int y = 0; y < h; y++)
            {
                byte a = (byte)(Mathf.Clamp01(top - y) * 255f);
                pixels[y * w + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D SoftDot(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01(1f - d);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}
