using System;
using System.Collections.Generic;
using UnityEngine;

// 간파(기본형): 적 공격이 곧 들어오는 순간 우클릭하면 받아치고 반격. 아니면 PlayerGuard가 가드로 이어감.
public class PlayerParry : MonoBehaviour
{
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
    static readonly int CounterHash = Animator.StringToHash("Parry_Counter");

    [SerializeField] Animator animator;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerDodge dodge;
    [SerializeField] PlayerAfterimage afterimage;
    [SerializeField] ThirdPersonCamera followCamera;

    // 간파 타이밍: 적 판정 시작 window초 전부터 판정 시작 후 lateWindow초까지 우클릭하면 성공
    [Header("Judge")]
    [SerializeField] float window = 0.2f;
    [SerializeField] float lateWindow = 0.2f;
    [SerializeField] float searchRadius = 4f;

    [Header("Counter")]
    [SerializeField] float counterHitTime = 0.5f;
    [SerializeField] float duration = 0.85f;
    [SerializeField] float counterDamage = 30f;
    [SerializeField] float counterPostureDamage = 40f;

    [Header("Feedback")]
    [SerializeField] float freezeScale = 0.05f;
    [SerializeField] float freezeTime = 0.15f;
    [SerializeField] float shakeStrength = 0.2f;
    [SerializeField] bool logParry = true;

    readonly Collider[] overlapBuffer = new Collider[32];
    readonly HashSet<IAttackThreat> seen = new HashSet<IAttackThreat>();

    IAttackThreat target;
    bool active;
    bool counterApplied;
    float timer;

    public bool IsActive => active;
    public bool IsInvulnerable => active;
    public event Action<GameObject> Parried;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        if (afterimage == null) afterimage = GetComponent<PlayerAfterimage>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
    }

    void OnDisable()
    {
        if (active) End(true);
    }

    // PlayerGuard가 우클릭을 누른 순간 먼저 호출. true면 간파 발동, false면 가드 자세로
    public bool TryParry()
    {
        if (active) return true;
        if (locomotion != null && !locomotion.IsGrounded) return false;
        if (skill != null && skill.IsActive) return false;
        if (dodge != null && dodge.IsDodging && !dodge.CanCancel) return false; // [변경] 회피 후반이면 간파로 끊고 들어감
        if (health != null && health.IsStunned) return false;

        IAttackThreat found = FindThreat();
        if (found == null) return false;

        HitResult result = found.ReceiveParry(gameObject);
        if (result != HitResult.Countered) return false;

        Begin(found);
        return true;
    }

    IAttackThreat FindThreat()
    {
        seen.Clear();
        int count = Physics.OverlapSphereNonAlloc(transform.position, searchRadius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        IAttackThreat best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col.transform.IsChildOf(transform)) continue;

            IAttackThreat threat = col.GetComponentInParent<IAttackThreat>();
            if (threat == null || !seen.Add(threat)) continue;
            if (!threat.IsParryable || !threat.IsAboutToHit(transform.position, window, lateWindow)) continue;

            float dist = (((Component)threat).transform.position - transform.position).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = threat;
            }
        }

        return best;
    }

    void Begin(IAttackThreat threat)
    {
        target = threat;
        active = true;
        counterApplied = false;
        timer = 0f;

        if (combat != null)
        {
            combat.CancelAttack();
            combat.InputLocked = true;
        }
        if (dodge != null && dodge.IsDodging) dodge.Interrupt(); // [변경] 잠금·애니메이션은 간파가 이어받음
        if (locomotion != null) locomotion.MovementLocked = true;

        Vector3 to = ((Component)threat).transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);

        animator.CrossFadeInFixedTime(CounterHash, 0.03f, 0, 0f);

        // 받아친 순간: 적만 거의 멈춤 + 카메라 흔들림 + 짧은 잔상
        CombatTime.SlowEnemies(freezeScale, freezeTime, 0.05f);
        if (followCamera != null) followCamera.Shake(shakeStrength, 0.15f);
        if (afterimage != null) afterimage.Spawn();

        Parried?.Invoke(((Component)threat).gameObject);
        if (logParry) Debug.Log($"[Parry] 간파 성공 · {((Component)threat).name}");
    }

    void Update()
    {
        if (!active) return;

        // 반격 도중 스킬이 시작되면 제어권을 넘기고 조용히 끝냄
        if (skill != null && skill.IsActive)
        {
            End(false);
            return;
        }

        timer += Time.deltaTime;

        if (!counterApplied && timer >= counterHitTime)
        {
            counterApplied = true;
            ApplyCounter();
        }

        if (timer >= duration) End(true);
    }

    void ApplyCounter()
    {
        if (target == null || (target as UnityEngine.Object) == null) return;

        var damageable = ((Component)target).GetComponentInParent<IDamageable>();
        if (damageable == null) return;

        Vector3 dir = ((Component)target).transform.position - transform.position;
        dir.y = 0f;

        damageable.ReceiveDamage(new DamageInfo
        {
            Attacker = gameObject,
            Damage = counterDamage,
            PostureDamage = counterPostureDamage,
            Parryable = false,
            Weight = AttackWeight.Parry,
            HitPoint = ((Component)target).transform.position + Vector3.up,
            Direction = dir.normalized
        });

        if (followCamera != null) followCamera.Shake(shakeStrength * 0.6f, 0.12f);
    }

    void End(bool restoreControl)
    {
        active = false;
        target = null;
        if (!restoreControl) return;

        if (combat != null) combat.InputLocked = false;
        if (locomotion != null) locomotion.MovementLocked = false;
        animator.CrossFadeInFixedTime(LocomotionHash, 0.15f, 0, 0f);
    }
}
