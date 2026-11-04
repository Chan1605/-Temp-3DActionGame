using System;
using System.Collections.Generic;
using UnityEngine;

// 패링: 대기·이동 중 적 공격이 곧 들어오는 순간 우클릭하면 받아치고 반격. 아니면 강공격·차지로 이어감 // [변경] 로그·주석 이름을 패링으로 (간파는 PlayerKanpa)
// v2(clashStyle): 누른 순간엔 무적 + 반격 모션만, 칼이 부딪히는 프레임에 경직·피해·연출을 한 번에 (오공식)
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
    [SerializeField] PlayerThunder thunder; // 간파 비용(뇌기 1칸)

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

    // v2: 부딪히는 시점 = 적 판정 시작까지 남은 시간을 이 범위로 자름. 반격 클립은 그만큼 앞을 건너뛰고 재생해 칼이 닿는 프레임을 맞춤
    [Header("Clash (v2)")]
    [SerializeField] bool clashStyle = true;
    [SerializeField] float minClashDelay = 0.06f;
    [SerializeField] float maxClashDelay = 0.2f;

    [Header("Feedback (v1)")]
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
    public event Action<GameObject, Vector3> Clashed; // v2 부딪힌 순간 (적, 접촉 지점). 적이 아직 공격 자세일 때 호출

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
        if (thunder == null) thunder = GetComponent<PlayerThunder>();
    }

    void OnDisable()
    {
        if (active) End(true);
    }

    // PlayerChargeAttack이 우클릭을 누른 순간 먼저 호출. true면 간파 발동, false면 강공격(탭)·차지(홀드)로
    public bool TryParry()
    {
        if (active) return true;
        if (locomotion != null && !locomotion.IsGrounded) return false;
        if (skill != null && skill.IsActive) return false;
        if (dodge != null && dodge.IsDodging && !dodge.CanCancel) return false; // 회피 후반이면 간파로 끊고 들어감
        if (health != null && health.IsStunned) return false;
        if (thunder != null && !thunder.CanSpend(thunder.ParryCost)) return false; // 뇌기가 없으면 간파 대신 강공격

        IAttackThreat found = FindThreat();
        if (found == null) return false;

        // v2는 경직을 부딪히는 순간으로 미룸
        if (clashStyle)
        {
            if (thunder != null) thunder.TrySpend(thunder.ParryCost);
            BeginClash(found);
            return true;
        }

        HitResult result = found.ReceiveParry(gameObject);
        if (result != HitResult.Countered) return false;
        if (thunder != null) thunder.TrySpend(thunder.ParryCost);

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
        StartCounter(threat, 0f);

        // 받아친 순간: 적만 거의 멈춤 + 카메라 흔들림 + 짧은 잔상
        CombatTime.SlowEnemies(freezeScale, freezeTime, 0.05f);
        if (followCamera != null) followCamera.Shake(shakeStrength, 0.15f);
        if (afterimage != null) afterimage.Spawn();

        Parried?.Invoke(((Component)threat).gameObject);
        if (logParry) Debug.Log($"[Parry] 패링 성공 · {((Component)threat).name}");
    }

    // 공용 시작부: 잠금 · 적 방향 회전 · 반격 모션(startTime초부터 재생)
    void StartCounter(IAttackThreat threat, float startTime)
    {
        target = threat;
        active = true;
        counterApplied = false;
        timer = startTime;

        if (combat != null)
        {
            combat.CancelAttack();
            combat.InputLocked = true;
        }
        if (dodge != null && dodge.IsDodging) dodge.Interrupt(); // 잠금·애니메이션은 간파가 이어받음
        if (locomotion != null) locomotion.MovementLocked = true;

        Vector3 to = ((Component)threat).transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);

        animator.CrossFadeInFixedTime(CounterHash, 0.03f, 0, startTime);
    }

    // v2: 칼이 닿는 시각(counterHitTime)이 적 판정 시작과 겹치도록 클립 앞부분을 건너뜀
    void BeginClash(IAttackThreat threat)
    {
        float until = threat.SecondsUntilHit();
        float delay = Mathf.Clamp(until < 0f ? 0f : until, minClashDelay, maxClashDelay);
        StartCounter(threat, Mathf.Max(0f, counterHitTime - delay));
        if (logParry) Debug.Log($"[Parry] 패링 입력 · 충돌까지 {delay:F2}초");
    }

    // v2 핵심: 연출(Clashed) → 적 경직 → 표식·스파크(Parried) → 반격 피해를 같은 프레임에
    void Clash()
    {
        if (target == null || (target as UnityEngine.Object) == null) return;

        GameObject enemy = ((Component)target).gameObject;
        Vector3 point = Vector3.Lerp(transform.position, enemy.transform.position, 0.5f) + Vector3.up * 1.2f;
        Clashed?.Invoke(enemy, point);
        if (afterimage != null) afterimage.Spawn();

        HitResult result = target.ReceiveParry(gameObject);
        if (result != HitResult.Countered)
        {
            if (logParry) Debug.Log($"[Parry] 충돌 시점에 패링 불가 ({result}) · {enemy.name}");
            return;
        }

        Parried?.Invoke(enemy);
        ApplyCounter();
        if (logParry) Debug.Log($"[Parry] 패링 성공(v2) · {enemy.name}");
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
            if (clashStyle) Clash();
            else ApplyCounter();
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
