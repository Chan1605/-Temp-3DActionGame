using System;
using UnityEngine;

// 뇌기(雷氣): 벽력일섬(Q) · 패링이 쓰는 플레이어 자원 하나
// 적에게 쌓던 뇌인 3칸 · 확산(A/B/C)은 없앰 → 일섬의 즉사 대상은 "흐트러진 적"(IStaggerable)
// 모으기: 적중 · 간파 · 저스트 회피 · 처형 / 쓰기: 일섬 1칸(즉발·차지 같음), 패링 1, 뇌검 각성 = 남은 전부
public class PlayerThunder : MonoBehaviour
{
    [SerializeField] WeaponHitbox weapon;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerKanpa kanpa;
    [SerializeField] PlayerDodge dodge;
    [SerializeField] PlayerExecution execution;

    [Header("Gauge")]
    [SerializeField] float maxGauge = 4f;
    [SerializeField] float startGauge = 0f;
    [SerializeField] float lightHitGain = 0.2f;
    [SerializeField] float heavyHitGain = 0.4f;
    [SerializeField] float chargeGainPerLevel = 0.3f;
    [SerializeField] float justDodgeGain = 1f;
    [SerializeField] float kanpaGain = 0.5f;
    [SerializeField] float executionGain = 1.5f; // 처형 → 일섬으로 이어지게

    [Header("Cost")]
    [SerializeField] bool useGaugeForSkill = true;  // 끄면 예전 쿨타임형 일섬 (A/B)
    [SerializeField] float qCost = 1f; // 즉발·차지 모두 1칸 (차지는 사거리·연출 차이만)
    [SerializeField] float parryCost = 1f;

    [Header("Skill Damage")]
    [UnityEngine.Serialization.FormerlySerializedAs("unmarkedSkillDamage")]
    [SerializeField] float normalSkillDamage = 30f;        // 흐트러지지 않은 적은 즉사 대신 이 피해
    [SerializeField] float bossQPostureScale = 0.3f;       // 보스는 즉사 없이 체간만 조금 (2배였을 땐 한 방에 체간 붕괴). 새 이름이라 0.3이 바로 적용
    [SerializeField] float bossStaggerDamageScale = 2.5f;  // 체간이 무너진 보스에겐 피해도 크게

    [SerializeField] bool logThunder = true;

    float gauge;
    bool enchanted;

    public float Gauge => gauge;
    public float MaxGauge => maxGauge;
    public bool UseGaugeForSkill => useGaugeForSkill;
    public float ParryCost => parryCost;
    public float NormalSkillDamage => normalSkillDamage;
    public float BossPostureScale => bossQPostureScale;
    public float BossStaggerDamageScale => bossStaggerDamageScale;
    public event Action<float> GaugeChanged;
    public event Action<float> MaxGaugeChanged; // [변경] 뇌기 칸 수가 바뀜 (HUD 칸 추가)

    // [변경] 수련(뇌기 확장): 칸 수를 늘림. 지금 뇌기는 그대로
    public void AddMaxGauge(float amount)
    {
        maxGauge += amount;
        MaxGaugeChanged?.Invoke(maxGauge);
        GaugeChanged?.Invoke(gauge);
    }

    // 뇌검 각성 중: 패링은 칸 없이 됨 // 뇌기는 계속 모이고, 남은 시간은 HUD 링(EnchantRing)이 보여 줌
    public bool Enchanted
    {
        get => enchanted;
        set
        {
            enchanted = value;
            if (!value) GaugeChanged?.Invoke(gauge);
        }
    }
    public bool CanParry => enchanted || CanSpend(parryCost);
    public void SpendParry()
    {
        if (!enchanted) TrySpend(parryCost);
    }
    public void SpendAll() => SetGauge(0f);

    void Awake()
    {
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (kanpa == null) kanpa = GetComponent<PlayerKanpa>();
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        if (execution == null) execution = GetComponent<PlayerExecution>();
        gauge = Mathf.Clamp(startGauge, 0f, maxGauge);
    }

    void OnEnable()
    {
        if (weapon != null) weapon.OnHit += OnWeaponHit;
        if (kanpa != null) kanpa.Countered += OnKanpa;
        if (dodge != null) dodge.JustDodged += OnJustDodged;
        if (execution != null) execution.Executed += OnExecuted;
    }

    void OnDisable()
    {
        if (weapon != null) weapon.OnHit -= OnWeaponHit;
        if (kanpa != null) kanpa.Countered -= OnKanpa;
        if (dodge != null) dodge.JustDodged -= OnJustDodged;
        if (execution != null) execution.Executed -= OnExecuted;
    }

    public bool CanSpend(float amount) => gauge >= amount - 0.001f;

    public bool TrySpend(float amount)
    {
        if (!CanSpend(amount)) return false;
        SetGauge(gauge - amount);
        return true;
    }

    public void Add(float amount) => SetGauge(gauge + amount); // 각성 중에도 모임

    public float SkillCost(int chargeLevel) => qCost; // 단계와 상관없이 같은 비용

    // 지금 뇌기로 쓸 수 있는 가장 높은 차지 단계 (-1이면 일섬 불가)
    public int MaxAffordableLevel(int maxLevel) => CanSpend(qCost) ? maxLevel : -1;

    public static bool IsStaggered(Component target)
    {
        var s = target != null ? target.GetComponentInParent<IStaggerable>() : null;
        return s != null && s.IsStaggered;
    }

    void SetGauge(float value)
    {
        float next = Mathf.Clamp(value, 0f, maxGauge);
        if (Mathf.Approximately(next, gauge)) return;
        gauge = next;
        GaugeChanged?.Invoke(gauge);
    }

    void OnWeaponHit(IDamageable target, DamageInfo info, HitResult result)
    {
        if (result != HitResult.Hit || info.Attacker != gameObject) return;
        int charge = combat != null ? combat.CurrentChargeLevel : 0;
        Add((info.Weight == AttackWeight.Heavy ? heavyHitGain : lightHitGain) + charge * chargeGainPerLevel);
    }

    void OnKanpa(GameObject enemy)
    {
        Add(kanpaGain);
        if (logThunder) Debug.Log($"[Thunder] 간파 · 뇌기 +{kanpaGain} ({gauge:0.0})");
    }

    void OnJustDodged() => Add(justDodgeGain);

    void OnExecuted(EnemySwordsman victim)
    {
        Add(executionGain);
        if (logThunder) Debug.Log($"[Thunder] 처형 보상 · 뇌기 +{executionGain} ({gauge:0.0})");
    }
}
