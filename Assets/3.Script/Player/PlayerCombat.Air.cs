using System.Collections.Generic;
using UnityEngine;

// 공중 콤보: 지상 콤보 중 Space = 띄우기 (맞으면 같이 떠오름) → 공중 □□□ (체공) → □ 또는 우클릭 = 내려찍기 (낙하 + 착지 충격파)
// 점프 중 □로도 공중 콤보 시작 (한 번 뜰 때마다 한 번), 점프 중 우클릭은 바로 내려찍기
public partial class PlayerCombat
{
    static readonly int SlamStartHash = Animator.StringToHash("Slam_Start");
    static readonly int SlamLoopHash = Animator.StringToHash("Slam_Loop");
    static readonly int SlamEndHash = Animator.StringToHash("Slam_End");

    [Header("Air Combo")]
    [SerializeField] AttackData launcher;
    [SerializeField] AttackData airStarter;
    [SerializeField] float followLaunchHeight = 2.6f; // 띄운 적(기본 3.2m)보다 살짝 낮게 → 올려 베는 각도
    [SerializeField] float airGravityScale = 0.12f;
    [SerializeField] float airHangLift = 0.6f;

    [Header("Slam")]
    [SerializeField] float slamMinHeight = 0.8f;
    [SerializeField] float slamPop = 2f;
    [SerializeField] float slamStartTime = 0.18f;
    [SerializeField] float slamDiveSpeed = 26f;
    [SerializeField] float slamHomingRange = 4f;
    [SerializeField] float slamDamage = 28f;
    [SerializeField] float slamPostureDamage = 25f;
    [SerializeField] float slamRadius = 3f;
    [SerializeField] float slamDiveRadius = 1.4f;
    [SerializeField] float slamRecoverTime = 0.45f;
    [SerializeField] float slamMoveCancel = 0.3f;
    [SerializeField] ThirdPersonCamera followCamera;

    readonly Collider[] slamBuffer = new Collider[24];
    readonly HashSet<IDamageable> slamHits = new HashSet<IDamageable>();
    int slamPhase; // 0 없음 · 1 도약 · 2 낙하 · 3 착지
    float slamTimer;
    Vector3 slamDrift;
    Transform slamTarget;
    bool airUsed, launchFollowed, launchMark;

    public bool IsSlamming => slamPhase != 0;
    public event System.Action<Vector3> SlamLanded; // 착지 충격 이펙트용

    // Space는 PlayerLocomotion이 기록 → 이번 공격의 입력 구간 안에 눌렸으면 콤보 전환 시점에 띄우기로 분기
    bool TryLauncher(float t)
    {
        if (launcher == null || locomotion == null || deflected || !IsGrounded()) return false;
        if (!SkillTree.IsUnlocked(SkillId.Launcher)) return false; // [변경] 띄우기는 스킬트리 해금
        if (current == launcher || current.AirAttack || current.LaunchHeight > 0f) return false;
        float openAt = attackStartTime + current.InputOpen * current.Duration / AttackSpeed;
        if (locomotion.LastJumpPressTime < openAt || t < current.ComboTransition) return false;

        locomotion.ConsumeJump(); // 공격이 끝난 뒤 점프로 또 나가지 않게
        lastPressInput = AttackInput.Light;
        launchMark = true;
        StartAttack(launcher, false);
        return true;
    }

    // 띄우기가 실제로 적을 띄웠을 때만 따라 올라감 (보스·허수아비처럼 안 뜨는 대상이면 제자리)
    void FollowLaunch(IDamageable target, HitResult result)
    {
        if (launchFollowed || result != HitResult.Hit || current == null || current.LaunchHeight <= 0f) return;
        if (!(target is EnemySwordsman enemy) || !enemy.IsAirborne || locomotion == null) return;
        launchFollowed = true;
        locomotion.Launch(followLaunchHeight);
    }

    void ApplyAirPhysics(AttackData attack)
    {
        launchFollowed = false;
        if (locomotion == null) return;
        if (attack.AirAttack && !IsGrounded())
        {
            airUsed = true;
            locomotion.GravityScale = airGravityScale;
            if (locomotion.VerticalVelocity < airHangLift) locomotion.SetVerticalVelocity(airHangLift); // 올라가는 중이면 그대로 둠
        }
        else locomotion.GravityScale = 1f;
    }

    bool TryStartAirAttack()
    {
        if (lastPressInput == AttackInput.Heavy) return TryStartSlam();
        if (airStarter == null || airUsed) return false;
        StartAttack(airStarter, true);
        return true;
    }

    bool EndAirAttackOnLanding()
    {
        if (!current.AirAttack || !IsGrounded() || elapsed < 0.1f) return false;
        EndCombo(returnCrossFade);
        return true;
    }

    bool TryAirSlam(bool buffered, float t)
    {
        if (!current.AirAttack || !buffered || t < current.ComboTransition) return false;
        bool wants = lastPressInput == AttackInput.Heavy || current.GetNext(AttackInput.Light) == null;
        return wants && TryStartSlam();
    }

    string RouteMark(AttackInput input)
    {
        if (launchMark)
        {
            launchMark = false;
            return "↑";
        }
        if (current != null && current.AirAttack) return "○";
        return input == AttackInput.Light ? "□" : "△";
    }

    void ResetAir()
    {
        slamPhase = 0;
        if (locomotion != null) locomotion.GravityScale = 1f;
    }

    // ===== 내려찍기 =====
    bool TryStartSlam()
    {
        if (animator == null || !animator.HasState(0, SlamLoopHash) || HeightAboveGround() < slamMinHeight) return false;

        if (swingActive) EndSwing();
        FinishSwingFx();
        current = null;
        lastPressTime = float.NegativeInfinity;
        slamPhase = 1;
        slamTimer = 0f;
        slamHits.Clear();

        slamTarget = null;
        if (targeting != null && locomotion != null && targeting.TryFindTarget(locomotion.GetInputDirection(), out Transform found, out _))
        {
            slamTarget = found;
            Vector3 to = Vector3.ProjectOnPlane(found.position - transform.position, Vector3.up);
            if (to.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }
        if (locomotion != null)
        {
            locomotion.MovementLocked = true;
            locomotion.GravityScale = 0f;
            locomotion.SetVerticalVelocity(slamPop); // 살짝 솟았다가
        }
        animator.CrossFadeInFixedTime(SlamStartHash, 0.05f, 0, 0f);
        if (logComboRoute) Debug.Log($"[Combo] {route}↓ 내려찍기");
        return true;
    }

    void UpdateSlam(float dt)
    {
        slamTimer += dt;
        switch (slamPhase)
        {
            case 1:
                if (slamTimer < slamStartTime) return;
                slamPhase = 2;
                slamTimer = 0f;
                slamDrift = DiveDrift();
                animator.CrossFadeInFixedTime(SlamLoopHash, 0.05f, 0, 0f);
                return;

            case 2: // 일정 속도로 내리꽂힘 + 대상 쪽으로 미끄러짐, 아래에 떠 있는 적은 같이 끌고 내려감
                if (locomotion != null) locomotion.SetVerticalVelocity(-slamDiveSpeed);
                if (controller != null && controller.enabled) controller.Move(slamDrift * dt);
                DiveHits();
                if (IsGrounded() || slamTimer > 2f) LandSlam();
                return;

            case 3:
                bool moveOut = slamTimer >= slamMoveCancel && HasMoveInput();
                if (moveOut || slamTimer >= slamRecoverTime) EndCombo(moveOut ? moveCancelCrossFade : returnCrossFade);
                return;
        }
    }

    // 낙하 시간 동안 대상 앞(0.8m)까지 가도록 수평 속도를 정함
    Vector3 DiveDrift()
    {
        if (slamTarget == null) return Vector3.zero;
        Vector3 to = Vector3.ProjectOnPlane(slamTarget.position - transform.position, Vector3.up);
        float distance = to.magnitude;
        if (distance > slamHomingRange || distance < 0.8f) return Vector3.zero;
        float duration = Mathf.Max(HeightAboveGround() / slamDiveSpeed, 0.05f);
        return Vector3.ClampMagnitude(to / distance * ((distance - 0.8f) / duration), 20f);
    }

    void DiveHits()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.down * 0.5f, slamDiveRadius, slamBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = slamBuffer[i];
            if (col.transform.IsChildOf(transform)) continue;
            var enemy = col.GetComponentInParent<EnemySwordsman>();
            if (enemy == null || !enemy.IsAirborne || !slamHits.Add(enemy)) continue;
            SlamHit(enemy, col, slamDamage * 0.4f, slamPostureDamage * 0.4f);
        }
    }

    // 착지 충격파: 반경 안 모두에게 (아머·가드 무시, 서 있는 적은 넘어뜨림)
    void LandSlam()
    {
        slamPhase = 3;
        slamTimer = 0f;
        if (locomotion != null) locomotion.GravityScale = 1f;
        animator.CrossFadeInFixedTime(SlamEndHash, 0.03f, 0, 0f);

        slamHits.Clear();
        int count = Physics.OverlapSphereNonAlloc(transform.position, slamRadius, slamBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = slamBuffer[i];
            if (col.transform.IsChildOf(transform)) continue;
            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null || !slamHits.Add(target)) continue;
            SlamHit(target, col, slamDamage, slamPostureDamage);
        }

        if (followCamera != null) followCamera.Shake(0.22f, 0.25f);
        SlamLanded?.Invoke(transform.position);
    }

    void SlamHit(IDamageable target, Collider col, float damage, float posture)
    {
        var info = new DamageInfo
        {
            Attacker = gameObject,
            Damage = damage,
            PostureDamage = posture,
            Parryable = false,
            Weight = AttackWeight.Heavy,
            BreakArmor = true,
            Slam = true,
            HitPoint = col.ClosestPoint(transform.position + Vector3.up),
            Direction = Vector3.ProjectOnPlane(col.transform.position - transform.position, Vector3.up).normalized
        };
        if (weapon != null) weapon.HitDirect(target, info);
        else target.ReceiveDamage(info);
    }

    readonly RaycastHit[] groundHits = new RaycastHit[8];

    // 바닥까지 높이 (발밑에 떠 있는 적은 바닥으로 치지 않음)
    float HeightAboveGround()
    {
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = 30f;
        for (int i = 0; i < count; i++)
        {
            Collider col = groundHits[i].collider;
            if (col.transform.IsChildOf(transform) || col.GetComponentInParent<IDamageable>() != null) continue;
            best = Mathf.Min(best, groundHits[i].distance - 0.2f);
        }
        return best;
    }
}
