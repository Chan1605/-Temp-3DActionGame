using System;
using UnityEngine;

// 성장 스탯: 공격력 배율 · 공격 속도 · 치명타 확률 · 치명타 피해. 레벨이 오르면 레벨당 증가량만큼 커짐
// 지금은 구역을 돌파할 때마다 1레벨 (나중에 보상 선택·장비로 확장할 때 AddBonus만 쓰면 됨)
public class PlayerStats : MonoBehaviour
{
    static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");

    [SerializeField] WeaponHitbox weapon;
    [SerializeField] Animator animator;

    [Header("Base")]
    [SerializeField] float attackPower = 1f;     // 피해 배율 (1 = 100%)
    [SerializeField] float attackSpeed = 2f;  // 공격 모션·판정 속도 배율
    [SerializeField, Range(0f, 1f)] float critChance = 0.1f;
    [SerializeField] float critDamage = 1.5f;    // 치명타 시 피해 배율

    [Header("Growth (레벨당)")]
    [SerializeField] int level = 1;
    [SerializeField] int maxLevel = 10;
    [SerializeField] float powerPerLevel = 0.1f;
    [SerializeField] float speedPerLevel = 0.04f;
    [SerializeField] float critChancePerLevel = 0.03f;
    [SerializeField] float critDamagePerLevel = 0.1f;
    [SerializeField] bool levelUpOnZoneClear = true;

    [Header("Limit")]
    [SerializeField] float maxAttackSpeed = 2.8f;
    [SerializeField] bool logStats = true;

    // 장비·보상 등 레벨 외 추가분
    float bonusPower, bonusSpeed, bonusCritChance, bonusCritDamage;
    bool hasSpeedParam;

    public int Level => level;
    public int MaxLevel => maxLevel;
    public float AttackPower => attackPower + powerPerLevel * (level - 1) + bonusPower;
    public float AttackSpeed => Mathf.Min(maxAttackSpeed, attackSpeed + speedPerLevel * (level - 1) + bonusSpeed);
    public float CritChance => Mathf.Clamp01(critChance + critChancePerLevel * (level - 1) + bonusCritChance);
    public float CritDamage => critDamage + critDamagePerLevel * (level - 1) + bonusCritDamage;

    public event Action StatsChanged;
    public event Action<int> LeveledUp;

    void Awake()
    {
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // 파라미터가 없으면 SetFloat가 매번 경고를 내므로 한 번만 확인
        if (animator != null)
            foreach (AnimatorControllerParameter p in animator.parameters)
                if (p.nameHash == AttackSpeedHash) hasSpeedParam = true;
        if (!hasSpeedParam) Debug.LogWarning("[Stats] Animator에 AttackSpeed 파라미터가 없습니다. Tools/Setup/Player Stats를 실행하세요.");
    }

    void OnEnable()
    {
        if (weapon != null) weapon.ModifyDamage = Modify;
        ZoneArena.AnyCleared += OnZoneCleared;
        ApplySpeed();
    }

    void OnDisable()
    {
        if (weapon != null && weapon.ModifyDamage == (Func<DamageInfo, DamageInfo>)Modify) weapon.ModifyDamage = null;
        ZoneArena.AnyCleared -= OnZoneCleared;
    }

    void OnValidate()
    {
        if (Application.isPlaying) Refresh();
    }

    // 핵심: 공격력 배율을 곱하고 치명타를 굴림. 즉사(일섬 표식)는 배율만 의미 없으니 그대로 둠
    public DamageInfo Modify(DamageInfo info)
    {
        info.Damage *= AttackPower;
        info.PostureDamage *= AttackPower;
        if (!info.InstantKill && UnityEngine.Random.value < CritChance)
        {
            info.Damage *= CritDamage;
            info.Critical = true;
        }
        info.Damage = Mathf.Round(info.Damage);
        return info;
    }

    [ContextMenu("Level Up")]
    public void LevelUp()
    {
        if (level >= maxLevel) return;
        level++;
        Refresh();
        LeveledUp?.Invoke(level);
        if (logStats) Debug.Log($"[Stats] Lv {level} · 공격력 {AttackPower:P0} · 공속 {AttackSpeed:F2} · 치명 {CritChance:P0} · 치피 {CritDamage:P0}");
    }

    public void AddBonus(float power, float speed, float chance, float damage)
    {
        bonusPower += power;
        bonusSpeed += speed;
        bonusCritChance += chance;
        bonusCritDamage += damage;
        Refresh();
    }

    void OnZoneCleared(ZoneArena zone)
    {
        if (levelUpOnZoneClear) LevelUp();
    }

    void Refresh()
    {
        ApplySpeed();
        StatsChanged?.Invoke();
    }

    void ApplySpeed()
    {
        if (hasSpeedParam && animator != null) animator.SetFloat(AttackSpeedHash, AttackSpeed);
    }
}
