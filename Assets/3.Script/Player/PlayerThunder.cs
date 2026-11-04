using System;
using System.Collections.Generic;
using UnityEngine;

// P4 뇌기(雷氣) + P5 뇌인 확산. 플레이어 쪽 이벤트(적중·간파·저스트 회피)를 듣고
//  - 적에게 뇌인을 새기고 (ThunderMark)
//  - 플레이어 뇌기를 모으고 쓴다 (간파 1칸, 일섬 즉발 2 · 차지1 3 · 차지2 4)
// 수치·확산 방식은 인스펙터 토글로 A/B 비교 (계획표 P4·P5)
public class PlayerThunder : MonoBehaviour
{
    public enum SpreadMode
    {
        None,            // A: 확산 없음
        AreaHits,        // B: 콤보 마무리 범위 +1, 간파 성공 시 주변 +1
        AreaAndTransfer  // C: B + 표식이 완성되면 주변으로 1칸 전이
    }

    [SerializeField] WeaponHitbox weapon;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerParry parry;
    [SerializeField] PlayerKanpa kanpa; // [변경]
    [SerializeField] PlayerDodge dodge;

    [Header("Gauge")]
    [SerializeField] float maxGauge = 4f;
    [SerializeField] float startGauge = 0f;
    [SerializeField] float lightHitGain = 0.2f;
    [SerializeField] float heavyHitGain = 0.4f;
    [SerializeField] float justDodgeGain = 1f;
    [SerializeField] float markCompleteGain = 0.3f; // P5 표식 보너스 (0으로 A/B)

    [Header("Cost")]
    [SerializeField] bool useGaugeForSkill = true;  // 끄면 예전 쿨타임형 일섬 (A/B)
    [SerializeField] float[] skillCost = { 2f, 3f, 4f }; // 즉발, 차지1, 차지2
    [SerializeField] float parryCost = 1f;

    [Header("Marks")]
    [SerializeField] int lightHitMarks = 1;
    [SerializeField] int heavyHitMarks = 2;
    // 차지 강타 단계별 뇌인 (1·2·3단계). 3단계는 주변까지 chargeFullMarks만큼
    [SerializeField] int[] chargeMarks = { 2, 3, 3 };
    [SerializeField] float chargeFullRadius = 3f;
    [SerializeField] int chargeFullMarks = 2;
    [SerializeField] float chargeGainPerLevel = 0.3f;

    [Header("Spread")]
    [SerializeField] SpreadMode spread = SpreadMode.AreaAndTransfer;
    [SerializeField] float finisherRadius = 3.5f;
    [SerializeField] float parryRadius = 3f;
    // [변경] 간파: 비용 없이 뇌기 획득 + 대상 가득 + 확산 모드와 상관없이 주변 +1
    [SerializeField] float kanpaGain = 0.5f;
    [SerializeField] float kanpaRadius = 3.5f;
    [SerializeField] float transferRadius = 3f;

    [Header("Skill Damage")]
    [SerializeField] float unmarkedSkillDamage = 30f; // 표식 안 된 적은 즉사 대신 큰 피해만

    [SerializeField] bool logThunder = true;

    readonly Collider[] overlapBuffer = new Collider[32];
    readonly HashSet<ThunderMark> seen = new HashSet<ThunderMark>();
    readonly HashSet<ThunderMark> subscribed = new HashSet<ThunderMark>();
    bool transferring; // 전이로 완성된 표식은 다시 전이하지 않음 (연쇄 폭주 방지)
    float gauge;

    public float Gauge => gauge;
    public float MaxGauge => maxGauge;
    public bool UseGaugeForSkill => useGaugeForSkill;
    public float ParryCost => parryCost;
    public float UnmarkedSkillDamage => unmarkedSkillDamage;
    public event Action<float> GaugeChanged;

    void Awake()
    {
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (parry == null) parry = GetComponent<PlayerParry>();
        if (kanpa == null) kanpa = GetComponent<PlayerKanpa>();
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        gauge = Mathf.Clamp(startGauge, 0f, maxGauge);
    }

    void OnEnable()
    {
        if (weapon != null) weapon.OnHit += OnWeaponHit;
        if (parry != null) parry.Parried += OnParried;
        if (kanpa != null) kanpa.Countered += OnKanpa; // [변경]
        if (dodge != null) dodge.JustDodged += OnJustDodged;
    }

    void OnDisable()
    {
        if (weapon != null) weapon.OnHit -= OnWeaponHit;
        if (parry != null) parry.Parried -= OnParried;
        if (kanpa != null) kanpa.Countered -= OnKanpa; // [변경]
        if (dodge != null) dodge.JustDodged -= OnJustDodged;
    }

    // ===== 뇌기 =====
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

    void SetGauge(float value)
    {
        float next = Mathf.Clamp(value, 0f, maxGauge);
        if (Mathf.Approximately(next, gauge)) return;
        gauge = next;
        GaugeChanged?.Invoke(gauge);
    }

    // ===== 뇌인 =====
    public static bool IsMarked(Component target)
    {
        ThunderMark mark = ThunderMark.Of(target);
        return mark != null && mark.IsFull;
    }

    // 핵심: 적중마다 뇌기 + 뇌인, 콤보 마무리(다음 연결이 없는 공격)는 주변까지 뇌인 +1
    void OnWeaponHit(IDamageable target, DamageInfo info, HitResult result)
    {
        if (result != HitResult.Hit || info.Attacker != gameObject) return;

        bool heavy = info.Weight == AttackWeight.Heavy;
        int charge = combat != null ? combat.CurrentChargeLevel : 0;
        Add((heavy ? heavyHitGain : lightHitGain) + charge * chargeGainPerLevel);

        ThunderMark mark = ThunderMark.Of((Component)target);
        int marks = charge > 0 ? chargeMarks[Mathf.Min(charge, chargeMarks.Length) - 1] : heavy ? heavyHitMarks : lightHitMarks;
        AddMarks(mark, marks);

        // 풀차지(마지막 단계)는 맞은 적 주변까지 표식 → 무리를 한 번에 새기는 수단
        if (charge >= chargeMarks.Length)
        {
            int n = AddMarksAround(((Component)target).transform.position, chargeFullRadius, chargeFullMarks, mark);
            if (logThunder && n > 0) Debug.Log($"[Thunder] 풀차지 확산 · 주변 {n}명 뇌인 +{chargeFullMarks}");
        }

        if (spread != SpreadMode.None && IsFinisher(combat != null ? combat.CurrentAttack : null))
        {
            int n = AddMarksAround(((Component)target).transform.position, finisherRadius, 1, mark);
            if (logThunder && n > 0) Debug.Log($"[Thunder] 마무리 확산 · 주변 {n}명 뇌인 +1");
        }
    }

    // 간파 성공: 대상 표식 가득, B·C면 주변도 +1. 비용(1칸)은 PlayerParry가 성공 직전에 차감
    void OnParried(GameObject enemy)
    {
        ThunderMark mark = enemy.GetComponentInParent<ThunderMark>();
        if (mark != null)
        {
            Subscribe(mark);
            mark.Fill();
        }
        if (spread != SpreadMode.None) AddMarksAround(enemy.transform.position, parryRadius, 1, mark);
    }

    // [변경]
    void OnKanpa(GameObject enemy)
    {
        ThunderMark mark = enemy.GetComponentInParent<ThunderMark>();
        if (mark != null)
        {
            Subscribe(mark);
            mark.Fill();
        }
        int n = AddMarksAround(enemy.transform.position, kanpaRadius, 1, mark);
        Add(kanpaGain);
        if (logThunder) Debug.Log($"[Thunder] 간파 · 대상 가득, 주변 {n}명 +1, 뇌기 +{kanpaGain}");
    }

    void OnJustDodged() => Add(justDodgeGain);

    void AddMarks(ThunderMark mark, int amount)
    {
        if (mark == null) return;
        Subscribe(mark);
        mark.Add(amount);
    }

    int AddMarksAround(Vector3 center, float radius, int amount, ThunderMark except)
    {
        seen.Clear();
        int count = Physics.OverlapSphereNonAlloc(center, radius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        int applied = 0;
        for (int i = 0; i < count; i++)
        {
            ThunderMark m = overlapBuffer[i].GetComponentInParent<ThunderMark>();
            if (m == null || m == except || !seen.Add(m)) continue;
            AddMarks(m, amount);
            applied++;
        }
        return applied;
    }

    // 표식 완성 이벤트는 처음 표식을 새길 때 한 번만 구독 (웨이브로 새로 나온 적도 자동 처리)
    void Subscribe(ThunderMark mark)
    {
        if (subscribed.Add(mark)) mark.Completed += OnMarkCompleted;
    }

    void OnMarkCompleted(ThunderMark mark)
    {
        Add(markCompleteGain);
        if (logThunder) Debug.Log($"[Thunder] 표식 완성 · {mark.name} (뇌기 {gauge:0.0})");

        if (spread != SpreadMode.AreaAndTransfer || transferring) return;
        transferring = true;
        AddMarksAround(mark.transform.position, transferRadius, 1, mark);
        transferring = false;
    }

    static bool IsFinisher(AttackData attack) =>
        attack != null && attack.GetNext(AttackInput.Light) == null && attack.GetNext(AttackInput.Heavy) == null;
}
