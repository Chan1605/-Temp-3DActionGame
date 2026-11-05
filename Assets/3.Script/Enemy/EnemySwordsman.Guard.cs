using UnityEngine;

// 방패병(EnemyKind.Guardian): 대기·추적 중에는 가드 자세로 걸어오며 정면의 □·△를 막는다
// 막을 때마다 가드 수치가 깎이고 0이 되면 가드 붕괴 → 긴 경직. 간파·패링·뇌락·차지 강타(2단계+)는 한 번에 붕괴
// 등 뒤 공격, 벽력일섬, 처형은 막지 못함. 공격(예고·휘두르기) 중에는 가드를 내림
public partial class EnemySwordsman
{
    static readonly int GuardMoveHash = Animator.StringToHash("Guard_Move");
    static readonly int GuardHitHash = Animator.StringToHash("Guard_Hit");
    static readonly int GuardBreakHash = Animator.StringToHash("Guard_Break");

    [Header("Guardian Look")]
    [SerializeField] Color guardBlockColor = new Color(0.6f, 0.8f, 1.4f);
    [SerializeField] Color guardBreakColor = new Color(1.5f, 0.45f, 1.1f);
    [SerializeField] float guardPushback = 0.25f;

    int guardCurrent;
    float guardRegenTimer;
    float guardPauseTimer;
    float animLockUntil;
    bool moveAnimGuard;

    public bool IsGuardian => data != null && data.Kind == EnemyKind.Guardian;
    public float GuardRatio => IsGuardian ? guardCurrent / (float)Mathf.Max(1, data.GuardPoints) : 0f;
    bool GuardReady => IsGuardian && guardCurrent > 0;
    bool IsGuarding => GuardReady && (state == State.Idle || state == State.Chase);
    int MoveStateHash => GuardReady ? GuardMoveHash : LocomotionHash;

    void InitGuard()
    {
        guardCurrent = IsGuardian ? data.GuardPoints : 0;
        guardRegenTimer = 0f;
        guardPauseTimer = 0f;
        animLockUntil = 0f;
    }

    void PlayMoveState(float fade)
    {
        moveAnimGuard = GuardReady;
        animator.CrossFadeInFixedTime(MoveStateHash, fade, 0, 0f);
    }

    void UpdateGuard(float dt)
    {
        if (!IsGuardian) return;
        if (guardPauseTimer > 0f) guardPauseTimer -= dt;

        // 경직 중에는 회복하지 않음 → 붕괴 뒤 경직 + 회복 대기 시간만큼 무방비
        if (guardCurrent < data.GuardPoints && state != State.Stun && state != State.Dead)
        {
            guardRegenTimer -= dt;
            if (guardRegenTimer <= 0f)
            {
                guardCurrent++;
                guardRegenTimer = data.GuardRegenInterval;
            }
        }

        // 가드가 다시 생기거나 막은 모션이 끝나면 걷는 자세를 맞춤
        if ((state == State.Idle || state == State.Chase) && guardPauseTimer <= 0f && moveAnimGuard != GuardReady)
            PlayMoveState(0.2f);
    }

    void LowerGuardForAttack()
    {
        moveAnimGuard = false;
        animator.CrossFadeInFixedTime(LocomotionHash, 0.15f, 0, 0f);
    }

    // 핵심: true = 피해 없이 막음. 가드가 0이 되는 공격은 가드를 깨고 false (그 공격의 피해·경직은 그대로 들어감)
    bool TryBlock(DamageInfo info)
    {
        if (!IsGuarding) return false;
        if (info.InstantKill || info.Weight == AttackWeight.Skill || info.Weight == AttackWeight.Execution) return false;
        if (!IsFrontal(info.Direction)) return false;

        bool breaks = info.BreakGuard || info.BreakArmor || info.Weight == AttackWeight.Parry;
        int cost = info.Weight == AttackWeight.Light ? data.LightGuardCost : data.HeavyGuardCost;
        guardCurrent = breaks ? 0 : guardCurrent - cost;
        guardRegenTimer = data.GuardRegenDelay;

        if (guardCurrent <= 0)
        {
            BreakGuard(info.Direction);
            return false;
        }

        moveSpeed = 0f;
        guardPauseTimer = data.GuardHitPause;
        moveAnimGuard = false; // 멈칫이 끝나면 UpdateGuard가 가드 걷기로 되돌림
        animator.CrossFadeInFixedTime(GuardHitHash, 0.04f, 0, 0f);
        Push(info.Direction, guardPushback);
        Flash(guardBlockColor);
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2f, "막음", DamageEvents.BlockColor);
        Log($"가드 {guardCurrent}/{data.GuardPoints}");
        return true;
    }

    void BreakGuard(Vector3 hitDirection)
    {
        if (swingActive) EndSwing();
        currentAttack = null;
        guardCurrent = 0;
        guardRegenTimer = data.GuardRegenDelay;
        guardPauseTimer = 0f;
        moveSpeed = 0f;

        float remaining = state == State.Stun ? stunDuration - stateTime : 0f;
        stunDuration = Mathf.Max(data.GuardBreakStun, remaining);
        attackReadyTime = Mathf.Max(attackReadyTime, localTime + stunDuration);
        animator.CrossFadeInFixedTime(GuardBreakHash, 0.04f, 0, 0f);
        animLockUntil = localTime + 0.6f; // 바로 뒤따르는 피해의 피격 모션이 붕괴 모션을 덮지 않게
        OpenExecution(stunDuration); // [변경] 가드 붕괴 = 처형 기회
        Push(hitDirection, data.HeavyKnockback);
        SetState(State.Stun);

        Flash(guardBreakColor);
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2f, "가드 붕괴!", DamageEvents.BreakColor);
        Log($"가드 붕괴 · 경직 {stunDuration:F1}초");
    }

    // 공격이 들어온 쪽이 몸 정면 guardAngle 안인지 (방향을 모르면 정면으로 봄)
    bool IsFrontal(Vector3 hitDirection)
    {
        Vector3 from = -Flat(hitDirection);
        if (from.sqrMagnitude < 0.0001f) return true;
        return Vector3.Angle(transform.forward, from) <= data.GuardAngle;
    }

    void Push(Vector3 direction, float distance)
    {
        Vector3 push = Flat(direction);
        if (push.sqrMagnitude < 0.0001f || distance <= 0f) return;
        knockbackVelocity = push.normalized * (distance / knockbackTime);
        knockbackTimer = knockbackTime;
    }
}
