using System;
using UnityEngine;

// 뇌기(雷氣): 벽력일섬(Q) · 패링이 쓰는 플레이어 자원 하나
// [변경] 적에게 쌓던 뇌인 3칸 · 확산(A/B/C)은 없앰 → 일섬의 즉사 대상은 "흐트러진 적"(IStaggerable)
// 모으기: 적중 · 간파 · 저스트 회피 · 처형 / 쓰기: 일섬 즉발 2 · 차지1 3 · 차지2 4, 패링 1
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
    [SerializeField] float[] skillCost = { 2f, 3f, 4f }; // 즉발, 차지1, 차지2
    [SerializeField] float parryCost = 1f;

    [Header("Skill Damage")]
    [UnityEngine.Serialization.FormerlySerializedAs("unmarkedSkillDamage")]
    [SerializeField] float normalSkillDamage = 30f;        // 흐트러지지 않은 적은 즉사 대신 이 피해
    [SerializeField] float bossPostureScale = 2f;          // 보스는 즉사 없이 체간 피해를 크게
    [SerializeField] float bossStaggerDamageScale = 2.5f;  // 체간이 무너진 보스에겐 피해도 크게

    [SerializeField] bool logThunder = true;

    float gauge;

    public float Gauge => gauge;
    public float MaxGauge => maxGauge;
    public bool UseGaugeForSkill => useGaugeForSkill;
    public float ParryCost => parryCost;
    public float NormalSkillDamage => normalSkillDamage;
    public float BossPostureScale => bossPostureScale;
    public float BossStaggerDamageScale => bossStaggerDamageScale;
    public event Action<float> GaugeChanged;

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

    public void Add(float amount) => SetGauge(gauge + amount);

    public float SkillCost(int chargeLevel) => skillCost[Mathf.Clamp(chargeLevel, 0, skillCost.Length - 1)];

    // 지금 뇌기로 쓸 수 있는 가장 높은 차지 단계 (-1이면 일섬 불가)
    public int MaxAffordableLevel(int maxLevel)
    {
        for (int level = Mathf.Min(maxLevel, skillCost.Length - 1); level >= 0; level--)
            if (CanSpend(skillCost[level])) return level;
        return -1;
    }

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
