using System.Collections;
using System.Collections.Generic;
using DigitalRuby.ThunderAndLightning;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 간파 v2 연출: PlayerParry.Clashed 한 번에 시간 정지→슬로→복귀, 펀치 줌, 화면 펄스, 충격 링, 적 잔상, 낙뢰, 소리를 묶어서 낸다
// 전투 로직은 모름. 끄면(enabled=false) 간파 판정은 그대로 두고 연출만 빠짐
public class ParryFx : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] PlayerParry parry;
    [SerializeField] PlayerSkill skill;
    [SerializeField] ThirdPersonCamera followCamera;

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
    [SerializeField] float flashExposure = 1.2f;
    [SerializeField] float flashTime = 0.05f;

    [Header("Shock Ring")]
    [SerializeField] Material fxMaterial; // Afterimage.mat (URP Unlit 가산)
    [SerializeField] Color innerRingColor = new Color(1f, 0.95f, 0.9f, 1f);
    [SerializeField] Color outerRingColor = new Color(1f, 0.15f, 0.1f, 0.8f);
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

    Volume volumeFx;
    ChromaticAberration ca;
    Vignette vig;
    ColorAdjustments colorAdjust;

    Coroutine timeRoutine;
    float ownedScale = -1f; // 마지막으로 이 스크립트가 넣은 timeScale. -1이면 시간을 쥐고 있지 않음
    float pulseStart = -1f;

    void Awake()
    {
        if (parry == null) parry = GetComponent<PlayerParry>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
        cam = Camera.main;
        block = new MaterialPropertyBlock();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        strikeBolt = LightningFactory.Create(boltPrefab, "ParryStrikeLightning", strikeStyle);
        if (screenPulse) CreateVolume();
    }

    void OnEnable()
    {
        if (parry != null) parry.Clashed += OnClashed;
    }

    void OnDisable()
    {
        if (parry != null) parry.Clashed -= OnClashed;
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

    void OnClashed(GameObject enemy, Vector3 point)
    {
        // 적이 아직 공격 자세일 때 호출되므로 잔상은 가장 먼저 굽는다
        if (enemyGhost) SpawnGhost(enemy, point);

        if (useTime)
        {
            if (timeRoutine != null) StopCoroutine(timeRoutine);
            timeRoutine = StartCoroutine(TimeCurve());
        }

        float hold = useTime ? freezeTime + slowTime : 0.2f;
        if (followCamera != null)
        {
            followCamera.Punch(punchAmount, punchIn, hold, punchOut, Random.value < 0.5f ? -punchRoll : punchRoll);
            followCamera.Shake(shake, 0.1f);
        }

        pulseStart = Time.unscaledTime;
        SpawnRing(point, innerRingColor, innerRingSize, innerRingTime, 0.06f);
        SpawnRing(point, outerRingColor, outerRingSize, outerRingTime, 0.22f);

        if (strikeBolt != null) strikeBolt.Trigger(enemy.transform.position + Vector3.up * strikeHeight, enemy.transform.position);

        if (clashClip != null) audioSource.PlayOneShot(clashClip, volume);
        if (impactClip != null) audioSource.PlayOneShot(impactClip, volume);
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
    IEnumerator TimeCurve()
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
        yield return new WaitForSecondsRealtime(slowTime);

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
        float hold = useTime ? freezeTime + slowTime : 0.2f;
        float fade = Mathf.Max(recoverTime, 0.15f);

        float w = e < hold ? 1f : 1f - Mathf.Clamp01((e - hold) / fade);
        volumeFx.weight = w;
        colorAdjust.postExposure.value = e < flashTime ? flashExposure * (1f - e / flashTime) : 0f;

        if (w <= 0f) pulseStart = -1f;
    }

    // ===== 충격 링 =====
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
