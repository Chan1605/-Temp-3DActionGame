using UnityEngine;

// 돌격병(EnemyKind.Charger): 일정 거리에서 몸이 달아오르는 예고 → 일직선 돌진 → 도착하면 내려베기(chargeAttack)
// 돌진 초반만 살짝 꺾어서 옆으로 회피하면 지나쳐 감. 내려베기가 끝나면 숨을 고르는 동안 무방비(맞으면 항상 강경직)
// 가까우면 일반 검병처럼 firstAttack으로 싸움
public partial class EnemySwordsman
{
    static readonly int ChargeWindupHash = Animator.StringToHash("Charge_Windup");
    static readonly int ChargeRunHash = Animator.StringToHash("Charge_Run");

    [Header("Charger Look")]
    [SerializeField] Color exhaustColor = new Color(0.32f, 0.36f, 0.42f);

    float chargeTraveled;
    bool exhausted;

    public bool IsCharger => data != null && data.Kind == EnemyKind.Charger && data.ChargeAttack != null;
    public bool IsExhausted => exhausted;
    bool IsCharging => state == State.ChargeWindup || state == State.Charge;

    bool TryStartCharge(float dist)
    {
        if (!IsCharger || localTime < attackReadyTime) return false;
        if (dist < data.ChargeMinDistance || dist > data.ChargeMaxDistance) return false;

        moveSpeed = 0f;
        animator.CrossFadeInFixedTime(ChargeWindupHash, 0.1f, 0, 0f);
        SetState(State.ChargeWindup);
        Log("돌진 예고");
        return true;
    }

    // 예고 동안은 플레이어를 계속 조준 → 끝나는 순간의 방향으로 출발
    void UpdateChargeWindup(float dt)
    {
        moveSpeed = 0f;
        FaceTarget(dt, 1.5f);
        if (stateTime < data.ChargeWindup) return;

        chargeTraveled = 0f;
        animator.CrossFadeInFixedTime(ChargeRunHash, 0.06f, 0, 0f);
        SetState(State.Charge);
    }

    void UpdateCharge(float dt)
    {
        if (stateTime <= data.ChargeHomingTime) TurnToward(dt, data.ChargeTurnSpeed);
        moveSpeed = data.ChargeSpeed;
        chargeTraveled += data.ChargeSpeed * dt;

        Vector3 to = Flat(target.position - transform.position);
        bool ahead = Vector3.Dot(transform.forward, to) > 0f;
        bool arrived = ahead && to.magnitude <= data.ChargeStopDistance;
        bool passed = !ahead && to.magnitude <= data.ChargeStopDistance + 1.5f; // 옆으로 피해서 지나침
        Vector3 v = controller.velocity;
        bool stuck = stateTime > 0.2f && new Vector2(v.x, v.z).magnitude < data.ChargeSpeed * CombatTime.EnemyScale * 0.3f; // 바위·벽에 막힘 (간파 슬로 중 오판 방지)

        if (arrived || passed || stuck || chargeTraveled >= data.ChargeMaxTravel)
        {
            moveSpeed = 0f;
            StartAttack(data.ChargeAttack);
        }
    }

    void Exhaust()
    {
        exhausted = true;
        moveSpeed = 0f;
        stunDuration = data.ExhaustTime;
        attackReadyTime = localTime + data.ExhaustTime + data.ChargeCooldown;
        OpenExecution(data.ExhaustTime); // [변경] 탈진 = 처형 기회
        PlayMoveState(0.2f);
        SetState(State.Stun);
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2f, "탈진", DamageEvents.ExhaustColor);
        Log($"탈진 {data.ExhaustTime:F1}초");
    }

    // 간파·저스트 회피용: 예고 남은 시간 + 남은 거리 / 돌진 속도 + 내려베기 판정 시작. 돌진 중이 아니면 -1
    float ChargeTimeUntilHit()
    {
        if (!IsCharging || data.ChargeAttack == null) return -1f;

        float distance = Mathf.Max(0f, DistanceToTarget() - data.ChargeStopDistance);
        float travel = Mathf.Min(distance, Mathf.Max(0f, data.ChargeMaxTravel - chargeTraveled)) / Mathf.Max(data.ChargeSpeed, 0.01f);
        float windup = state == State.ChargeWindup ? Mathf.Max(0f, data.ChargeWindup - stateTime) : 0f;
        return windup + travel + data.ChargeAttack.HitStart * data.ChargeAttack.Duration;
    }
}
