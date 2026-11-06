using UnityEngine;

// 띄우기·공중 콤보·내려찍기를 맞는 쪽
// 띄워짐(Airborne): 느린 중력으로 떠 있고, 공중 공격에 맞으면 다시 떠받쳐짐 → 착지하면 쓰러짐(Downed) → 누워 있다가 기상
// 내려찍기: 떠 있으면 바닥으로 내리꽂히고(더 오래 누움), 서 있으면 충격파로 넘어짐
// 보스는 뜨지 않음 (체간만 쌓임). 셋업(Tools/Setup/Air Combo) 전이라 상태가 없으면 기존 경직으로 대신함
public partial class EnemySwordsman
{
    static readonly int LaunchHash = Animator.StringToHash("Launch");
    static readonly int AirLoopHash = Animator.StringToHash("Air_Loop");
    static readonly int AirHitHash = Animator.StringToHash("Air_Hit");
    static readonly int AirLandHash = Animator.StringToHash("Air_Land");
    static readonly int DownHash = Animator.StringToHash("Down");
    static readonly int GetUpHash = Animator.StringToHash("Get_Up");

    [Header("Air")]
    [SerializeField] float airGravityScale = 0.55f;
    [SerializeField] float juggleLift = 2.4f;   // 공중에서 맞을 때 위로 받쳐 주는 속도
    [SerializeField] float maxAirTime = 3f;     // 이보다 오래 떠 있으면 중력 그대로 (무한 띄우기 방지)
    [SerializeField] float airPush = 0.3f;
    [SerializeField] float slamDownSpeed = 24f;
    [SerializeField] float downTime = 1f;       // 착지 후 누워 있는 시간 (착지 모션 포함)
    [SerializeField] float slamDownTime = 1.6f;

    bool slammed, gettingUp, deadFalling;
    float getUpAt;

    public bool IsAirborne => state == State.Airborne;

    bool HasAirStates => animator != null && animator.HasState(0, AirLoopHash) && animator.HasState(0, GetUpHash);

    float AirGravityFactor => state == State.Airborne && !slammed && stateTime < maxAirTime ? airGravityScale : 1f;

    float LaunchSpeed(float height) => Mathf.Sqrt(2f * height * -gravity * airGravityScale);

    void ResetAirState()
    {
        slammed = false;
        gettingUp = false;
        deadFalling = false;
    }

    // 떠 있거나 쓰러져 있을 때 맞음 (true면 ReceiveDamage가 여기서 끝남)
    bool HandleAirHit(DamageInfo info)
    {
        if (state == State.Downed)
        {
            Flash(hitColor);
            return true;
        }
        if (state != State.Airborne) return false;

        Flash(hitColor);
        if (info.Slam)
        {
            slammed = true;
            verticalVelocity = -slamDownSpeed;
        }
        else verticalVelocity = Mathf.Max(verticalVelocity, info.Launch > 0f ? LaunchSpeed(info.Launch) : juggleLift);
        Push(info.Direction, airPush);
        animator.Play(AirHitHash, 0, 0f); // 끝나면 컨트롤러 전환으로 Air_Loop
        return true;
    }

    // 서 있을 때 (아머·가드 판정 뒤)
    bool TryAirReaction(DamageInfo info)
    {
        if (IsBoss || !HasAirStates) return false;
        if (info.Launch > 0f)
        {
            Launch(info);
            return true;
        }
        if (info.Slam)
        {
            Push(info.Direction, airPush * 2f);
            EnterDowned(DownHash, slamDownTime);
            return true;
        }
        return false;
    }

    void Launch(DamageInfo info)
    {
        if (swingActive) EndSwing();
        currentAttack = null;
        moveSpeed = 0f;
        slammed = false;
        verticalVelocity = LaunchSpeed(info.Launch);
        Push(info.Direction, airPush);
        animator.CrossFadeInFixedTime(LaunchHash, 0.05f, 0, 0f);
        SetState(State.Airborne);
        Log("띄워짐");
    }

    void UpdateAirborne()
    {
        moveSpeed = 0f;
        if (stateTime < 0.15f || !controller.isGrounded) return;
        EnterDowned(AirLandHash, slammed ? slamDownTime : downTime);
    }

    void EnterDowned(int hash, float lieTime)
    {
        if (swingActive) EndSwing();
        currentAttack = null;
        moveSpeed = 0f;
        gettingUp = false;
        stunDuration = lieTime; // 경직 시간 필드를 누워 있는 시간으로 재사용
        animator.CrossFadeInFixedTime(hash, 0.05f, 0, 0f);
        SetState(State.Downed);
        Log(slammed ? "내리꽂힘" : "쓰러짐");
    }

    // 누운 모션(마지막 프레임 유지) → 시간이 차면 기상 모션 → 끝나면 추적
    void UpdateDowned()
    {
        moveSpeed = 0f;
        if (!gettingUp)
        {
            if (stateTime < stunDuration) return;
            gettingUp = true;
            getUpAt = stateTime;
            animator.CrossFadeInFixedTime(GetUpHash, 0.15f, 0, 0f);
            return;
        }

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        bool done = (info.shortNameHash == GetUpHash && !animator.IsInTransition(0) && info.normalizedTime >= 0.9f) || stateTime - getUpAt > 3f;
        if (!done) return;

        slammed = false;
        attackReadyTime = Mathf.Max(attackReadyTime, localTime + 0.4f);
        PlayMoveState(0.2f);
        SetState(State.Chase);
    }

    void UpdateDeadFall()
    {
        if (!deadFalling) return;
        if (!controller.enabled || controller.isGrounded || stateTime > 3f)
        {
            deadFalling = false;
            controller.enabled = false;
        }
    }
}
