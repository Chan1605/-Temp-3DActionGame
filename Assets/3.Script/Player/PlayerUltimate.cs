using System.Collections;
using DigitalRuby.ThunderAndLightning;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 궁극기 (V). 궁극기 게이지 3칸 — 패링 · 간파 · 저스트 회피 · 처형으로만 참
//  V를 누르면 모인 칸 수만큼 한 번에 씀: 1칸 뇌운낙뢰진 · 2칸 천뢰일섬 · 3칸 뇌신강림 (아끼면 더 센 것)
//  연출 동안 무적 · 입력 잠금. 기술별 흐름은 partial 파일(.Storm · .Heavens · .Descent), 화면 연출은 UltimateFx
// 셋업: Tools/Setup/Player Ultimate (Player 프리팹에 붙이고 소리 · 번개 프리팹 연결)
public partial class PlayerUltimate : MonoBehaviour
{
    public const int MaxBars = 3;

    [Header("Gauge")]
    [SerializeField] float parryGain = 0.5f;
    [SerializeField] float kanpaGain = 0.5f;
    [SerializeField] float justDodgeGain = 0.4f;
    [SerializeField] float executeGain = 1f;
    [SerializeField] Key key = Key.V;

    [Header("Links")]
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerParry parry;
    [SerializeField] PlayerKanpa kanpa;
    [SerializeField] PlayerDodge dodge;
    [SerializeField] PlayerExecution execution;
    [SerializeField] PlayerChargeAttack charge;
    [SerializeField] PlayerStats stats;
    [SerializeField] ThirdPersonCamera followCamera;
    [SerializeField] Animator animator;
    [SerializeField] GameObject boltPrefab; // PlayerVfx와 같은 번개 프리팹

    [Header("Sound")]
    [SerializeField] AudioClip[] thunderClips;
    [SerializeField] AudioClip slashClip;
    [SerializeField] AudioClip chargeClip;
    [SerializeField] AudioClip clashClip;

    static readonly string[] Names = { "", "雷雲落雷陣", "天雷一閃", "雷神降臨" };
    static readonly string[] Readings = { "", "뇌운낙뢰진", "천뢰일섬", "뇌신강림" };

    public float Gauge { get; private set; }
    public int Bars => Mathf.Min(MaxBars, Mathf.FloorToInt(Gauge + 0.0001f));
    public bool Active { get; private set; }
    public event System.Action<int> Started;  // 단계 (1~3)
    public event System.Action Blocked;       // 게이지 부족

    UltimateFx fx;
    AudioSource audioSource;
    LightningBoltPrefabScriptBase skyBolt, arcBolt;
    UltimateHud hud;

    void Awake()
    {
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (parry == null) parry = GetComponent<PlayerParry>();
        if (kanpa == null) kanpa = GetComponent<PlayerKanpa>();
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        if (execution == null) execution = GetComponent<PlayerExecution>();
        if (charge == null) charge = GetComponent<PlayerChargeAttack>();
        if (stats == null) stats = GetComponent<PlayerStats>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        fx = new GameObject("UltimateFx").AddComponent<UltimateFx>();
        skyBolt = LightningFactory.Create(boltPrefab, "UltimateSkyBolt", SkyStyle());
        arcBolt = LightningFactory.Create(boltPrefab, "UltimateArcBolt", ArcStyle());
    }

    void Start()
    {
        hud = UltimateHud.Create(this);
        if (RunState.UltimateGauge > 0f) SetGauge(RunState.UltimateGauge); // 씬을 넘어와도 이어받음
    }

    void OnEnable()
    {
        if (parry != null) parry.Parried += OnParried;
        if (kanpa != null) kanpa.Countered += OnCountered;
        if (dodge != null) dodge.JustDodged += OnJustDodged;
        if (execution != null) execution.Executed += OnExecuted;
    }

    void OnDisable()
    {
        if (parry != null) parry.Parried -= OnParried;
        if (kanpa != null) kanpa.Countered -= OnCountered;
        if (dodge != null) dodge.JustDodged -= OnJustDodged;
        if (execution != null) execution.Executed -= OnExecuted;
    }

    void OnParried(GameObject e) => AddGauge(parryGain);
    void OnCountered(GameObject e) => AddGauge(kanpaGain);
    void OnJustDodged() => AddGauge(justDodgeGain);
    void OnExecuted(EnemySwordsman e) => AddGauge(executeGain);

    public void AddGauge(float amount)
    {
        if (Active || amount <= 0f) return;
        SetGauge(Gauge + amount);
    }

    public void SetGauge(float value)
    {
        int before = Bars;
        Gauge = Mathf.Clamp(value, 0f, MaxBars);
        if (hud != null && Bars > before) hud.Ready(Bars);
    }

    void Update()
    {
        if (GamePause.IsPaused || Active) return;
        Keyboard kb = Keyboard.current;
        if (kb != null && kb[key].wasPressedThisFrame) TryActivate();
    }

    // ===== 발동 =====
    bool CanActivate()
    {
        if (health != null && (health.IsDead || health.IsStunned)) return false;
        if (locomotion != null && !locomotion.IsGrounded) return false;
        if (skill != null && skill.IsActive) return false;
        if (execution != null && execution.IsActive) return false;
        if (charge != null && charge.IsDashing) return false;
        if (kanpa != null && kanpa.IsInvulnerable) return false;
        return true;
    }

    void TryActivate()
    {
        int tier = Bars;
        if (tier < 1)
        {
            Blocked?.Invoke();
            if (hud != null) hud.Deny();
            return;
        }
        if (!CanActivate()) return;

        Gauge -= tier;
        Active = true;
        Started?.Invoke(tier);
        Debug.Log($"[Ultimate] {Readings[tier]} ({tier}칸)");
        IEnumerator routine = tier == 1 ? Storm() : tier == 2 ? Heavens() : Descent();
        StartCoroutine(Run(routine, tier != 3)); // 뇌신강림은 발도가 납도까지 마치므로 자세를 덮어쓰지 않음
    }

    // 공통 앞뒤: 잠금 · 무적 · 레터박스 · 기술 이름 → 기술 흐름 → 원상복구
    IEnumerator Run(IEnumerator routine, bool returnToIdle)
    {
        Lock(true, false);
        yield return routine;
        Lock(false, returnToIdle);
        fx.EndAll();
        if (followCamera != null) followCamera.EndCinematic();
        Active = false;
    }

    void Lock(bool on, bool returnToIdle)
    {
        if (on)
        {
            if (combat != null)
            {
                combat.CancelAttack();
                combat.InputLocked = true;
            }
            if (locomotion != null) locomotion.MovementLocked = true;
            if (health != null) health.SetInvulnerable(30f);
        }
        else
        {
            if (combat != null) combat.InputLocked = false;
            if (locomotion != null) locomotion.MovementLocked = false;
            if (health != null) health.SetInvulnerable(0.4f); // 끝난 직후 잠깐 더
            if (returnToIdle && animator != null) animator.CrossFadeInFixedTime(LocomotionHash, 0.2f, 0, 0f);
        }
    }

    // ===== 공용 도구 =====
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    void Play(string state, float fade = 0.08f)
    {
        if (animator == null || string.IsNullOrEmpty(state)) return;
        int hash = Animator.StringToHash(state);
        if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
    }

    string ChargeState => skill != null && skill.Data != null ? skill.Data.ChargeState : "";
    string ReleaseState => skill != null && skill.Data != null ? skill.Data.ReleaseState : "";

    void Sound(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume * AudioOptions.SfxVolume);
    }

    void Thunder(float volume = 1f)
    {
        if (thunderClips == null || thunderClips.Length == 0) return;
        Sound(thunderClips[Random.Range(0, thunderClips.Length)], volume, Random.Range(0.85f, 1.1f));
    }

    // 하늘에서 지점으로 낙뢰 (흔들리게 몇 번 겹침)
    void SkyStrike(Vector3 ground, float height = 18f)
    {
        if (skyBolt == null) return;
        Vector2 o = Random.insideUnitCircle * 2f;
        Vector3 sky = ground + new Vector3(o.x, height, o.y);
        skyBolt.Trigger(sky, ground);
        if (arcBolt != null)
            for (int i = 0; i < 3; i++)
            {
                Vector2 a = Random.insideUnitCircle.normalized * Random.Range(0.8f, 2f);
                arcBolt.Trigger(ground + Vector3.up * 0.1f, ground + new Vector3(a.x, Random.Range(0f, 0.4f), a.y));
            }
    }

    void Arc(Vector3 from, Vector3 to)
    {
        if (arcBolt != null) arcBolt.Trigger(from, to);
    }

    // 반경 안 적에게 피해 (아머 무시). 보스는 bossScale 배
    int HitArea(Vector3 center, float radius, float damage, float posture, float bossScale = 1f, bool instantKill = false)
    {
        int hits = 0;
        foreach (EnemySwordsman enemy in EnemySwordsman.Alive.ToArrayCopy())
        {
            if (enemy == null || !enemy.CanBeTargeted) continue;
            Vector3 d = enemy.transform.position - center;
            d.y = 0f;
            if (d.sqrMagnitude > radius * radius) continue;
            Hit(enemy, damage, posture, bossScale, instantKill);
            hits++;
        }
        return hits;
    }

    void Hit(EnemySwordsman enemy, float damage, float posture, float bossScale, bool instantKill)
    {
        bool boss = enemy.IsBoss;
        var info = new DamageInfo
        {
            Attacker = gameObject,
            Damage = damage * (boss ? bossScale : 1f),
            PostureDamage = posture * (boss ? bossScale : 1f),
            Parryable = false,
            Weight = AttackWeight.Skill,
            BreakArmor = true,
            InstantKill = instantKill && !boss,
            HitPoint = enemy.transform.position + Vector3.up,
            Direction = Flat(enemy.transform.position - transform.position).normalized
        };
        if (stats != null) info = stats.Modify(info);
        enemy.ReceiveDamage(info);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    float Yaw => transform.eulerAngles.y;
    Vector3 Chest => transform.position + Vector3.up * 1.2f;

    // ===== 번개 모양 =====
    static LightningBoltStyle SkyStyle() => new LightningBoltStyle
    {
        width = new Vector2(0.18f, 0.3f),
        duration = new Vector2(0.18f, 0.3f),
        count = new Vector2Int(1, 2),
        generations = 7,
        chaos = 0.28f,
        forkedness = 0.55f,
        intensity = 3f,
        glowIntensity = 2.2f,
        glowWidth = 8f,
        glowColor = new Color(0.55f, 0.65f, 1f),
        lightRange = 28f,
        lightIntensity = 3f,
        lightPercent = 0.2f,
        lightColor = new Color(0.65f, 0.75f, 1f)
    };

    static LightningBoltStyle ArcStyle() => new LightningBoltStyle
    {
        width = new Vector2(0.04f, 0.08f),
        duration = new Vector2(0.06f, 0.12f),
        generations = 5,
        chaos = 0.35f,
        forkedness = 0.3f,
        intensity = 2f,
        glowIntensity = 1.4f,
        glowWidth = 5f,
        glowColor = new Color(0.5f, 0.65f, 1f)
    };
}

static class ListCopyExtensions
{
    // 피해를 주는 도중 적 목록이 바뀌어도 안전하게 (쓰러지면 Alive에서 빠짐)
    public static T[] ToArrayCopy<T>(this System.Collections.Generic.IReadOnlyList<T> list)
    {
        var copy = new T[list.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = list[i];
        return copy;
    }
}
