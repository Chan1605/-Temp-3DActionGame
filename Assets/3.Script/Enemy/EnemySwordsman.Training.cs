using UnityEngine;

// 수련장용 행동 (Tools/Setup/Tutorial Scene이 켬)
//  passive: 공격하지 않는 허수아비. 경직·띄우기·쓰러짐은 그대로 받고, 끝나면 제자리로 걸어 돌아가 플레이어를 바라봄
//  sparArmor: 공격하는 수련 상대(trainer · passive 아님)는 공격 동작 내내 경직 없음
//  leashRadius > 0: 플레이어가 자기 자리 주변 이 거리 안에 있을 때만 싸움 → 다른 구역에서 기술을 찍을 때 끼어들지 않음
public partial class EnemySwordsman
{
    [Header("Training")]
    [SerializeField] bool passive;
    [SerializeField] float leashRadius;
    [SerializeField] float homeTolerance = 0.4f;
    [SerializeField] bool sparArmor = true; // [변경] 공격하는 수련 상대는 예고 ~ 휘두르기 끝까지 맞아도 경직 없음 (움찔만)

    // 플레이어가 먼저 때려 공격을 끊으면 간파·패링을 연습할 수 없어서. 각성 평타 등 아머 무시 공격은 그대로 통함
    bool SparArmored => sparArmor && trainer && !passive &&
        (state == State.Windup || state == State.Attack || swingActive);

    bool OutsideLeash => passive ||
        (leashRadius > 0f && Flat(target.position - homePosition).sqrMagnitude > leashRadius * leashRadius);

    // 핵심: true면 이번 프레임 Idle·Chase 처리를 여기서 끝냄 (자리로 복귀 → 도착하면 플레이어 쪽으로 몸만 돌림)
    bool UpdateTraining(float dt)
    {
        if (!passive && leashRadius <= 0f) return false;
        if (!OutsideLeash) return false;

        if (state == State.Chase)
        {
            PlayMoveState(0.2f); // 옆걸음 중이었으면 이동 모션으로
            SetState(State.Idle);
        }

        Vector3 toHome = Flat(homePosition - transform.position);
        if (toHome.magnitude > homeTolerance)
        {
            Quaternion want = Quaternion.LookRotation(toHome.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, data.TurnSpeed * dt);
            moveSpeed = Vector3.Angle(transform.forward, toHome) < 45f ? data.WalkSpeed : 0f;
            return true;
        }

        moveSpeed = 0f;
        if (DistanceToTarget() <= data.DetectRange) FaceTarget(dt, 0.4f);
        return true;
    }
}
