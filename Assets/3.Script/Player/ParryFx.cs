using System.Collections;
using System.Collections.Generic;
using DigitalRuby.ThunderAndLightning;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 패링·간파 연출: PlayerParry.Clashed / PlayerKanpa.Clashed 한 번에 시간 정지→슬로→복귀, 펀치 줌, 화면 펄스, 충격 링, 적 잔상, 낙뢰, 소리를 묶어서 낸다
// 전투 로직은 모름. 끄면(enabled=false) 간파 판정은 그대로 두고 연출만 빠짐
public class ParryFx : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] PlayerParry parry;
    [SerializeField] PlayerKanpa kanpa;
    [SerializeField] PlayerSkill skill;
    [SerializeField] ThirdPersonCamera followCamera;

    // 패링은 간파보다 작게: 슬로 길이·줌·링 크기에 곱함. 낙뢰는 간파 전용(기본)
    [Header("Parry vs Kanpa")]
    [SerializeField, Range(0f, 1f)] float parryStrength = 0.6f;
    [SerializeField] bool parryStrike;

    [Header("Time (실제 시간)")]
    [SerializeField] bool useTime = true;
    [SerializeField] float freezeScale = 0.02f;
    [SerializeField] float freezeTime = 0.07f;
    [SerializeField] float slowScale = 0.2f;
    [SerializeField] float slowTime = 0.35f;
    [SerializeField] float recoverTime = 0.25f;

    [Header("Camera")]
    [SerializeField] float punchAmount = 0.35f;
    [SerializeField] float punchIn = 0.04f;
    [SerializeField] float punchOut = 0.35f;
    [SerializeField] float punchRoll = 4f;
    [SerializeField] float shake = 0.05f;

    [Header("Screen Pulse")]
    [SerializeField] bool screenPulse = true;
    [SerializeField] float chromatic = 0.8f;
    [SerializeField] float vignette = 0.35f;
    [SerializeField] Color vignetteColor = new Color(0.55f, 0.03f, 0.03f);
    [SerializeField] float saturation = -50f;
    // 간파 순간 화면이 0.5초 가까이 하얗게 덮여 정작 순간이 안 보였음 → 섬광 낮추고 낙뢰는 뇌락 전용
    [SerializeField] float flashPeak = 0.5f;
    [SerializeField] bool kanpaStrike;
    [SerializeField, Range(0.1f, 1f)] float strikeGlowScale = 0.5f; // 낙뢰 번짐 폭 배율 (카메라 가까이서 화면을 덮지 않게)
    [SerializeField] float flashTime = 0.05f;

    [Header("Shock Ring")]
    [SerializeField] Material fxMaterial; // Afterimage.mat (URP Unlit 가산)
    [SerializeField] Color innerRingColor = new Color(1f, 0.95f, 0.9f, 1f);
    [SerializeField] Color outerRingColor = new Color(1f, 0.15f, 0.1f, 0.8f);
    // 간파 전용 색: 패링(붉은색)과 한눈에 구분되게 청백 번개 계열
    [SerializeField] Color kanpaInnerRingColor = new Color(0.9f, 0.97f, 1f, 1f);
    [SerializeField] Color kanpaOuterRingColor = new Color(0.35f, 0.65f, 1f, 0.85f);
    [SerializeField] Color kanpaVignetteColor = new Color(0.03f, 0.12f, 0.45f);
    [SerializeField] float innerRingSize = 2.6f;
    [SerializeField] float outerRingSize = 3.6f;
    [SerializeField] float innerRingTime = 0.22f;
    [SerializeField] float outerRingTime = 0.45f;

    [Header("Enemy Ghost")]
    [SerializeField] bool enemyGhost = true;
    [SerializeField] Color ghostColor = new Color(0.85f, 0.92f, 1f, 0.7f);
    [SerializeField] float ghostLifetime = 0.5f;
    [SerializeField] float ghostGrow = 0.08f;
    [SerializeField] float ghostDrift = 0.5f;

    [Header("Lightning Strike")]
    [SerializeField] GameObject boltPrefab;
    [SerializeField] float strikeHeight = 12f;
    [SerializeField] LightningBoltStyle strikeStyle = new LightningBoltStyle
    {
        width = new Vector2(0.18f, 0.26f), duration = new Vector2(0.3f, 0.4f), count = new Vector2Int(2, 3),
        generations = 6, chaos = 0.2f, forkedness = 0.6f, intensity = 3f, glowIntensity = 2.5f, glowWidth = 8f,
        lightRange = 14f, lightIntensity = 4f, lightPercent = 1f
    };

    [Header("Sound")]
    [SerializeField] AudioClip clashClip;  // 높은 금속음
    [SerializeField] AudioClip impactClip; // 낮은 쿵
    [SerializeField] AudioClip kanpaClip;  // 간파에만 겹쳐 재생 (예: 천둥·여운)
    [SerializeField, Range(0f, 1f)] float volume = 0.9f;

    class Ring { public LineRenderer Line; public Vector3 Center; public float Age, Life, Size; public Color Color; }
    class Ghost { public GameObject Root; public List<Mesh> Meshes; public List<Renderer> Renderers; public Vector3 Start, Drift; public float Age; }

    const int RingSegments = 48;

    readonly List<Ring> rings = new List<Ring>();
    readonly List<Ghost> ghosts = new List<Ghost>();
    MaterialPropertyBlock block;
    Camera cam;
    AudioSource audioSource;
    LightningBoltPrefabScriptBase strikeBolt;

    // 뇌락: 입력 창 동안 칼에 잔번개, 착지 순간 금빛 충격 + 낙뢰 2줄
    [Header("뇌락")]
    [SerializeField] Color smashRingColor = new Color(1f, 0.82f, 0.35f, 0.9f);
    [SerializeField] float smashRingSize = 4.5f;
    [SerializeField] float smashPunch = 0.25f;
    [SerializeField] float bladeSparkInterval = 0.06f;
    [SerializeField] LightningBoltStyle bladeSparkStyle = new LightningBoltStyle
    {
        width = new Vector2(0.015f, 0.03f), duration = new Vector2(0.05f, 0.08f), generations = 4, chaos = 0.35f,
        intensity = 2.5f, glowIntensity = 1.5f, glowWidth = 4f, lightRange = 3f, lightIntensity = 1.5f, lightPercent = 0.3f
    };
    LightningBoltPrefabScriptBase bladeBolt;
    BoxCollider blade;
    Coroutine bladeRoutine;

    Volume volumeFx;
    ChromaticAberration ca;
    Vignette vig;
    ColorAdjustments colorAdjust;

    Coroutine timeRoutine;
    float ownedScale = -1f; // 마지막으로 이 스크립트가 넣은 timeScale. -1이면 시간을 쥐고 있지 않음
    float pulseStart = -1f;
    float pulseHold;

    void Awake()
    {
        if (parry == null) parry = GetComponent<PlayerParry>();
        if (kanpa == null) kanpa = GetComponent<PlayerKanpa>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
        cam = Camera.main;
        block = new MaterialPropertyBlock();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        // 저장된 스타일은 그대로 두고 번짐 폭·밝기만 줄인 복사본으로 생성
        var strike = JsonUtility.FromJson<LightningBoltStyle>(JsonUtility.ToJson(strikeStyle));
        strike.glowWidth *= strikeGlowScale;
        strike.glowIntensity *= Mathf.Lerp(0.6f, 1f, strikeGlowScale);
        strikeBolt = LightningFactory.Create(boltPrefab, "ParryStrikeLightning", strike);
        bladeBolt = LightningFactory.Create(boltPrefab, "KanpaBladeLightning", bladeSparkStyle);
        WeaponHitbox weapon = GetComponentInChildren<WeaponHitbox>(true);
        if (weapon != null) blade = weapon.GetComponent<BoxCollider>();
        if (screenPulse) CreateVolume();
    }

    void OnEnable()
    {
        if (parry != null) parry.Clashed += OnParryClashed;
        if (kanpa != null)
        {
            kanpa.Clashed += OnKanpaClashed;
            kanpa.FollowReady += OnFollowReady;
            kanpa.FollowClosed += OnFollowClosed;
            kanpa.SmashLanded += OnSmashLanded;
        }
    }

    void OnDisable()
    {
        if (parry != null) parry.Clashed -= OnParryClashed;
        if (kanpa != null)
        {
            kanpa.Clashed -= OnKanpaClashed;
            kanpa.FollowReady -= OnFollowReady;
            kanpa.FollowClosed -= OnFollowClosed;
            kanpa.SmashLanded -= OnSmashLanded;
        }
        bladeRoutine = null;
        if (timeRoutine != null) StopCoroutine(timeRoutine);
        timeRoutine = null;
        ReleaseTime();
        if (volumeFx != null) volumeFx.weight = 0f;
    }

    void OnDestroy()
    {
        foreach (Ghost g in ghosts) DestroyGhost(g);
        if (volumeFx != null && volumeFx.sharedProfile != null) Destroy(volumeFx.sharedProfile);
    }

    // 색 묶음을 넘겨 패링=붉은색, 간파=청백
    void OnParryClashed(GameObject enemy, Vector3 point) =>
        Play(enemy, point, parryStrength, parryStrike, null, innerRingColor, outerRingColor, vignetteColor);
    void OnKanpaClashed(GameObject enemy, Vector3 point) =>
        Play(enemy, point, 1f, kanpaStrike, kanpaClip, kanpaInnerRingColor, kanpaOuterRingColor, kanpaVignetteColor); // 낙뢰는 뇌락에서

    // strength(0~1)로 패링·간파 크기를 구분
    void Play(GameObject enemy, Vector3 point, float strength, bool strike, AudioClip extraClip,
        Color inner, Color outer, Color vignetteTint)
    {
        // 적이 아직 공격 자세일 때 호출되므로 잔상은 가장 먼저 굽는다
        if (enemyGhost) SpawnGhost(enemy, point);

        if (useTime)
        {
            if (timeRoutine != null) StopCoroutine(timeRoutine);
            timeRoutine = StartCoroutine(TimeCurve(slowTime * strength));
        }

        float hold = useTime ? freezeTime + slowTime * strength : 0.2f;
        float size = Mathf.Lerp(0.5f, 1f, strength);
        if (followCamera != null)
        {
            float roll = punchRoll * strength;
            followCamera.Punch(punchAmount * strength, punchIn, hold, punchOut, Random.value < 0.5f ? -roll : roll);
            followCamera.Shake(shake, 0.1f);
        }

        pulseStart = Time.unscaledTime;
        pulseHold = hold;
        SpawnRing(point, inner, innerRingSize * size, innerRingTime, 0.06f);
        SpawnRing(point, outer, outerRingSize * size, outerRingTime, 0.22f * size);
        if (vig != null) vig.color.value = vignetteTint;

        if (strike && strikeBolt != null) strikeBolt.Trigger(enemy.transform.position + Vector3.up * strikeHeight, enemy.transform.position);

        float v = volume * AudioOptions.SfxVolume; // [변경] 효과음 볼륨 설정 반영
        if (clashClip != null) audioSource.PlayOneShot(clashClip, v);
        if (impactClip != null) audioSource.PlayOneShot(impactClip, v * Mathf.Lerp(0.6f, 1f, strength));
        if (extraClip != null) audioSource.PlayOneShot(extraClip, v);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        UpdatePulse();
        UpdateRings(dt);
        UpdateGhosts(dt);
    }

    // ===== 시간 =====
    // 핵심: 다른 쪽(일섬·히트스톱·구역 연출)이 이미 시간을 쥐고 있으면 손대지 않고,
    // 도중에 일섬이 시작돼 값을 바꾸면 그쪽이 끝날 때까지 기다렸다가 우리가 넣은 값일 때만 1로 되돌린다
    IEnumerator TimeCurve(float slowDuration)
    {
        if (ownedScale < 0f)
        {
            if (skill != null && skill.IsActive) yield break;
            if (!Mathf.Approximately(Time.timeScale, 1f)) yield break;
        }

        SetTime(freezeScale);
        yield return new WaitForSecondsRealtime(freezeTime);
        if (!StillOwned()) { yield return WaitAndRelease(); yield break; }

        SetTime(slowScale);
        yield return new WaitForSecondsRealtime(slowDuration);

        for (float t = 0f; t < recoverTime; t += Time.unscaledDeltaTime)
        {
            if (!StillOwned()) break;
            float k = t / recoverTime;
            SetTime(Mathf.Lerp(slowScale, 1f, k * k));
            yield return null;
        }

        yield return WaitAndRelease();
        timeRoutine = null;
    }

    void SetTime(float scale)
    {
        Time.timeScale = scale;
        ownedScale = scale;
    }

    bool StillOwned() => (skill == null || !skill.IsActive) && Mathf.Approximately(Time.timeScale, ownedScale);

    IEnumerator WaitAndRelease()
    {
        while (skill != null && skill.IsActive) yield return null;
        ReleaseTime();
    }

    void ReleaseTime()
    {
        if (ownedScale >= 0f && Mathf.Approximately(Time.timeScale, ownedScale)) Time.timeScale = 1f;
        ownedScale = -1f;
    }

    // ===== 화면 펄스 =====
    void CreateVolume()
    {
        var go = new GameObject("ParryVolume");
        go.transform.SetParent(transform, false);
        volumeFx = go.AddComponent<Volume>();
        volumeFx.isGlobal = true;
        volumeFx.priority = 100f;
        volumeFx.weight = 0f;

        // 지정한 값만 Override → 씬의 기존 색 보정(대비·필터 등)은 그대로
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volumeFx.sharedProfile = profile;

        ca = profile.Add<ChromaticAberration>();
        ca.intensity.Override(chromatic);
        vig = profile.Add<Vignette>();
        vig.intensity.Override(vignette);
        vig.color.Override(vignetteColor);
        vig.smoothness.Override(0.6f);
        colorAdjust = profile.Add<ColorAdjustments>();
        colorAdjust.saturation.Override(saturation);
        colorAdjust.postExposure.Override(0f);

        // 카메라 후처리가 꺼져 있으면 볼륨이 보이지 않음
        if (cam != null)
        {
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null && !data.renderPostProcessing)
            {
                data.renderPostProcessing = true;
                Debug.Log("[ParryFx] 메인 카메라 Post Processing을 켰습니다.");
            }
        }
    }

    void UpdatePulse()
    {
        if (volumeFx == null || pulseStart < 0f) return;

        float e = Time.unscaledTime - pulseStart;
        float fade = Mathf.Max(recoverTime, 0.15f);

        float w = e < pulseHold ? 1f : 1f - Mathf.Clamp01((e - pulseHold) / fade);
        volumeFx.weight = w;
        colorAdjust.postExposure.value = e < flashTime ? flashPeak * (1f - e / flashTime) : 0f;
        if (w <= 0f) pulseStart = -1f;
    }

    // ===== 충격 링 =====
    // ===== 뇌락 =====
    void OnFollowReady(float duration)
    {
        if (bladeRoutine != null) StopCoroutine(bladeRoutine);
        if (blade != null && bladeBolt != null) bladeRoutine = StartCoroutine(BladeSparks(duration));
    }

    void OnFollowClosed(bool used)
    {
        if (bladeRoutine != null) StopCoroutine(bladeRoutine);
        bladeRoutine = null;
    }

    // 칼날(히트박스 긴 축)을 따라 짧은 번개를 계속 튀김 → "지금 한 번 더"라는 신호
    IEnumerator BladeSparks(float duration)
    {
        float end = Time.unscaledTime + duration;
        while (Time.unscaledTime < end)
        {
            Vector3 size = blade.size;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            Vector3 half = Vector3.zero;
            half[axis] = size[axis] * 0.5f;
            Vector3 a = blade.transform.TransformPoint(blade.center - half);
            Vector3 b = blade.transform.TransformPoint(blade.center + half);
            Vector3 p = Vector3.Lerp(a, b, Random.value);
            bladeBolt.Trigger(p, p + Random.onUnitSphere * 0.35f);
            yield return new WaitForSecondsRealtime(bladeSparkInterval);
        }
        bladeRoutine = null;
    }

    void OnSmashLanded(Vector3 center)
    {
        Vector3 ground = center + Vector3.up * 0.2f;
        if (strikeBolt != null)
        {
            strikeBolt.Trigger(center + Vector3.up * strikeHeight, center); // 2줄 → 1줄
        }
        SpawnRing(ground, smashRingColor, smashRingSize, 0.35f, 0.25f);
        SpawnRing(ground, innerRingColor, smashRingSize * 0.6f, 0.2f, 0.08f);
        if (followCamera != null)
        {
            followCamera.Punch(smashPunch, 0.03f, 0.12f, 0.3f, 0f);
            followCamera.Shake(0.22f, 0.25f);
        }
        if (impactClip != null) audioSource.PlayOneShot(impactClip, volume * AudioOptions.SfxVolume); // [변경]
    }

    void SpawnRing(Vector3 center, Color c, float size, float life, float width)
    {
        if (fxMaterial == null) return;

        Ring ring = rings.Find(r => !r.Line.gameObject.activeSelf);
        if (ring == null)
        {
            var go = new GameObject("ParryRing");
            var line = go.AddComponent<LineRenderer>();
            line.loop = true;
            line.useWorldSpace = true;
            line.positionCount = RingSegments;
            line.sharedMaterial = fxMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            ring = new Ring { Line = line };
            rings.Add(ring);
        }

        ring.Center = center;
        ring.Age = 0f;
        ring.Life = life;
        ring.Size = size;
        ring.Color = c;
        ring.Line.widthMultiplier = width;
        ring.Line.gameObject.SetActive(true);
        DrawRing(ring, 0f);
    }

    void UpdateRings(float dt)
    {
        foreach (Ring r in rings)
        {
            if (!r.Line.gameObject.activeSelf) continue;
            r.Age += dt;
            float k = r.Age / r.Life;
            if (k >= 1f) { r.Line.gameObject.SetActive(false); continue; }
            DrawRing(r, k);
        }
    }

    // 카메라를 마주 보는 원을 ease-out으로 키우며 흐리게
    void DrawRing(Ring r, float k)
    {
        Transform view = cam != null ? cam.transform : transform;
        float eased = 1f - (1f - k) * (1f - k);
        float radius = Mathf.Lerp(0.2f, r.Size, eased);

        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * Mathf.PI * 2f / RingSegments;
            r.Line.SetPosition(i, r.Center + (view.right * Mathf.Cos(a) + view.up * Mathf.Sin(a)) * radius);
        }

        Color c = r.Color;
        c.a *= (1f - k) * (1f - k);
        r.Line.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        r.Line.SetPropertyBlock(block);
    }

    // ===== 적 잔상: 공격하던 자세를 흰 껍데기로 남기고 뒤로 밀리며 흩어짐 =====
    void SpawnGhost(GameObject enemy, Vector3 point)
    {
        if (fxMaterial == null) return;

        // 몸·무기만: 머리 위 뇌인 표식 같은 부착물은 제외
        Animator body = enemy.GetComponentInChildren<Animator>();
        Transform source = body != null ? body.transform : enemy.transform;

        var root = new GameObject("ParryGhost");
        root.transform.position = enemy.transform.position;
        var ghost = new Ghost { Root = root, Meshes = new List<Mesh>(), Renderers = new List<Renderer>(), Start = root.transform.position };

        foreach (SkinnedMeshRenderer src in source.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!src.enabled || !src.gameObject.activeInHierarchy) continue;
            var mesh = new Mesh();
            src.BakeMesh(mesh, true);
            ghost.Meshes.Add(mesh);
            AddPart(ghost, mesh, src.transform.position, src.transform.rotation, Vector3.one);
        }

        foreach (MeshFilter src in source.GetComponentsInChildren<MeshFilter>())
        {
            var r = src.GetComponent<MeshRenderer>();
            if (r == null || !r.enabled || !src.gameObject.activeInHierarchy || src.sharedMesh == null) continue;
            AddPart(ghost, src.sharedMesh, src.transform.position, src.transform.rotation, src.transform.lossyScale);
        }

        Vector3 away = enemy.transform.position - transform.position;
        away.y = 0f;
        ghost.Drift = away.sqrMagnitude > 0.0001f ? away.normalized * ghostDrift : Vector3.zero;
        ghosts.Add(ghost);
        SetGhostAlpha(ghost, ghostColor.a);
    }

    void AddPart(Ghost ghost, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var part = new GameObject("Part");
        part.transform.SetPositionAndRotation(pos, rot);
        part.transform.localScale = scale;
        part.transform.SetParent(ghost.Root.transform, true);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = part.AddComponent<MeshRenderer>();
        r.sharedMaterial = fxMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ghost.Renderers.Add(r);
    }

    void UpdateGhosts(float dt)
    {
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            Ghost g = ghosts[i];
            g.Age += dt;
            float k = g.Age / ghostLifetime;
            if (k >= 1f)
            {
                DestroyGhost(g);
                ghosts.RemoveAt(i);
                continue;
            }

            float eased = 1f - (1f - k) * (1f - k);
            g.Root.transform.position = g.Start + g.Drift * eased;
            g.Root.transform.localScale = Vector3.one * (1f + ghostGrow * eased);
            SetGhostAlpha(g, ghostColor.a * (1f - k) * (1f - k));
        }
    }

    void SetGhostAlpha(Ghost g, float alpha)
    {
        Color c = ghostColor;
        c.a = alpha;
        block.Clear();
        block.SetColor(BaseColorId, c);
        foreach (Renderer r in g.Renderers) r.SetPropertyBlock(block);
    }

    void DestroyGhost(Ghost g)
    {
        foreach (Mesh m in g.Meshes) if (m != null) Destroy(m);
        if (g.Root != null) Destroy(g.Root);
    }
}
