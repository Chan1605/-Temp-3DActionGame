using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 뇌검 각성 (R): 남은 뇌기를 전부 써서 칼에 번개를 두름. 지속시간 = 쓴 칸 × 칸당 시간
// 지속 중: 평타 적중마다 작은 낙뢰(추가 피해, 주변으로 번짐) · 체간 피해 배율 · 아머 무시 (PlayerCombat이 IsActive를 봄)
//          패링은 칸 없이 됨 (PlayerThunder.Enchanted) // [변경] 뇌기는 계속 모이고, 남은 시간은 HUD 링(EnchantRing)
// 끝나는 순간 주변 방전. 연출은 PlayerVfx가 이벤트를 받아 그림
public class PlayerEnchant : MonoBehaviour
{
    [SerializeField] InputAction enchantAction = new InputAction("Enchant", InputActionType.Button, "<Keyboard>/r");
    [SerializeField] PlayerThunder thunder;
    [SerializeField] WeaponHitbox weapon;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerExecution execution;
    [SerializeField] ThirdPersonCamera followCamera;

    [Header("Duration")]
    [SerializeField] float minCellsToStart = 2f; // [변경] 최소 2칸 (새 이름이라 기존 저장값 1 대신 2가 바로 적용)
    [SerializeField] float secondsPerCell = 3.5f;

    [Header("While Active")]
    [SerializeField] float postureScale = 1.5f;
    [SerializeField] float strikeDamage = 6f;
    [SerializeField] float strikePostureDamage = 3f;
    [SerializeField] float strikeRadius = 1.5f;

    [Header("Discharge")]
    [SerializeField] float dischargeDamage = 20f;
    [SerializeField] float dischargePostureDamage = 20f;
    [SerializeField] float dischargeRadius = 3f;

    [SerializeField] bool logEnchant = true;

    static readonly Color LabelColor = new Color(0.6f, 0.85f, 1f);

    readonly Collider[] buffer = new Collider[24];
    readonly HashSet<IDamageable> hitSet = new HashSet<IDamageable>();
    float remaining, total;

    public bool IsActive => remaining > 0f;
    public float Remaining01 => total > 0f ? remaining / total : 0f;
    public float PostureScale => postureScale;
    public float Remaining => remaining;           // [변경] HUD 링용
    public float MinCells => minCellsToStart;

    public event Action Started;
    public event Action<Vector3> Struck;            // 낙뢰 지점
    public event Action<Vector3, float> Discharged; // 방전 중심 · 반경
    public event Action Ended;

    void Awake()
    {
        if (thunder == null) thunder = GetComponent<PlayerThunder>();
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (execution == null) execution = GetComponent<PlayerExecution>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
    }

    void OnEnable()
    {
        enchantAction.Enable();
        if (weapon != null) weapon.OnHit += OnWeaponHit;
    }

    void OnDisable()
    {
        enchantAction.Disable();
        if (weapon != null) weapon.OnHit -= OnWeaponHit;
        if (IsActive) Finish(false);
    }

    void Update()
    {
        if (!IsActive)
        {
            if (enchantAction.WasPressedThisFrame()) TryStart();
            return;
        }

        remaining -= Time.deltaTime;
        if (remaining <= 0f)
        {
            Finish(true);
            return;
        }
    }

    bool TryStart()
    {
        if (thunder == null || thunder.Gauge < minCellsToStart - 0.001f)
        {
            if (logEnchant) Debug.Log($"[Enchant] 뇌기 부족 ({(thunder != null ? thunder.Gauge : 0f):0.0} / {minCellsToStart})");
            return false;
        }
        if ((skill != null && skill.IsActive) || (health != null && (health.IsDead || health.IsStunned)) || (execution != null && execution.IsActive)) return false;

        // 핵심: 남은 뇌기 전부 → 칸당 지속시간
        float cells = thunder.Gauge;
        thunder.SpendAll();
        thunder.Enchanted = true;
        total = remaining = cells * secondsPerCell;

        if (followCamera != null) followCamera.Shake(0.15f, 0.2f);
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.2f, "뇌검 각성", LabelColor);
        Started?.Invoke();
        if (logEnchant) Debug.Log($"[Enchant] 뇌검 각성 · 뇌기 {cells:0.0}칸 → {total:0.0}초");
        return true;
    }

    void Finish(bool discharge)
    {
        remaining = 0f;
        if (discharge) Discharge();
        if (thunder != null) thunder.Enchanted = false;
        Ended?.Invoke();
        if (logEnchant) Debug.Log("[Enchant] 각성 종료" + (discharge ? " · 방전" : ""));
    }

    // 평타가 맞은 자리에 작은 낙뢰 → 맞은 적 + 주변 적에게 추가 피해 (weapon.OnHit를 다시 부르지 않게 ReceiveDamage 직접)
    void OnWeaponHit(IDamageable target, DamageInfo info, HitResult result)
    {
        if (!IsActive || result != HitResult.Hit || info.Attacker != gameObject) return;
        Vector3 center = ((Component)target).transform.position;
        Burst(center, strikeRadius, strikeDamage, strikePostureDamage, AttackWeight.Light);
        Struck?.Invoke(center);
    }

    void Discharge()
    {
        Burst(transform.position, dischargeRadius, dischargeDamage, dischargePostureDamage, AttackWeight.Heavy);
        if (followCamera != null) followCamera.Shake(0.25f, 0.3f);
        Discharged?.Invoke(transform.position, dischargeRadius);
    }

    void Burst(Vector3 center, float radius, float damage, float posture, AttackWeight weight)
    {
        hitSet.Clear();
        int count = Physics.OverlapSphereNonAlloc(center, radius, buffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = buffer[i];
            if (col.transform.IsChildOf(transform)) continue;
            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null || !target.CanBeTargeted || !hitSet.Add(target)) continue;

            target.ReceiveDamage(new DamageInfo
            {
                Attacker = gameObject,
                Damage = damage,
                PostureDamage = posture,
                Parryable = false,
                Weight = weight,
                HitPoint = col.ClosestPoint(center + Vector3.up),
                Direction = Vector3.ProjectOnPlane(col.transform.position - transform.position, Vector3.up).normalized
            });
        }
    }
}
