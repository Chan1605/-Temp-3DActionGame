using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 우클릭 하나로 세 가지 (가드 대신):
//  누른 순간 적 공격 직전이면 → 패링 (대기·이동 중)
//  콤보 중 누르면      → △를 바로 예약, △ 초반에 적 공격이 닿으면 간파 (PlayerKanpa)
//  짧게 눌렀다 떼면       → 강공격 △ (콤보 분기: □□△, □□□△ …)
//  길게 누르고 있으면     → 차지 자세, 단계(1~3)가 오름
//  dashFromLevel 단계 이상에서 떼면 섬격(閃擊): 벽력일섬처럼 앞으로 빠르게 지나가며 길 위의 적을 베고, 한 박자 뒤 일괄 피해
//  그 전에 떼면 아무것도 안 나가고 취소 (차지 강타는 없앰) // [변경]
public class PlayerChargeAttack : MonoBehaviour
{
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
    static readonly int ChargeStartHash = Animator.StringToHash("Charge_Start");
    static readonly int DashHash = Animator.StringToHash("Skill_Release"); // 납도를 못 할 때(Sheathe 상태 없음 등)만 씀

    [SerializeField] InputActionReference heavyAction;
    [SerializeField] AttackData releaseAttack; // 섬격 피해·체간 기준값으로만 씀 // [변경]

    [SerializeField] Animator animator;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerParry parry;
    [SerializeField] PlayerKanpa kanpa;
    [SerializeField] PlayerDodge dodge;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerHealth health;
    [SerializeField] ThirdPersonCamera followCamera;
    [SerializeField] PlayerLockOn lockOn;
    [SerializeField] PlayerStats stats;
    [SerializeField] PlayerAfterimage afterimage;
    [SerializeField] TrailRenderer dashTrail; // 비어 있으면 일섬 궤적을 같이 씀
    [SerializeField] PlayerStance stance;     // 섬격 = 납도 → 평상시 자세 (락온 중에도)
    [SerializeField] bool sheatheAfterDash = true;

    [Header("Input")]
    [SerializeField] float holdThreshold = 0.2f; // 이보다 오래 누르면 차지, 짧으면 강공격

    // levelTimes: 차지 자세 진입 후 각 단계에 도달하는 시간 (1단계는 바로)
    [Header("Charge")]
    [SerializeField] float[] levelTimes = { 0f, 0.55f, 1.15f };
    [SerializeField] float[] damageMultipliers = { 1.4f, 2f, 3f };
    [SerializeField] float turnSpeed = 360f;
    [SerializeField] bool logCharge = true;

    // 섬격: 단계별 거리. 피해 = releaseAttack 피해 × 단계 배율. 2단계부터 방패 붕괴, 풀차지는 아머도 무시 + 낙뢰 연출
    [Header("Dash (섬격)")]
    [SerializeField] int dashFromLevel = 3;            // 1이면 모든 차지가 섬격, 3이면 풀차지만 (그 전에 떼면 취소)
    [SerializeField] float[] dashDistances = { 4.5f, 6f, 8f };
    [SerializeField] float dashDuration = 0.14f;
    [SerializeField] float dashPathRadius = 1f;
    [SerializeField] float behindDistance = 1.6f;      // 락온 대상이 있으면 그 뒤까지
    [SerializeField] float cutDelay = 0.12f;           // 지나간 뒤 한 박자 늦게 베임
    [SerializeField] float dashRecovery = 0.3f;
    [SerializeField] float cutHitStop = 0.08f;         // 실제 시간
    [SerializeField] int[] dashMarks = { 1, 1, 2 };     // 맞은 적에게 쌓는 뇌인
    [SerializeField] LayerMask obstacleMask = ~0;

    bool pressed;
    bool consumed;   // 누른 순간 간파가 나갔으면 이번 입력은 끝
    float holdTime;
    bool charging;
    float chargeTime;
    int level;
    bool heavyQueued;          // 콤보 중 누른 순간 이미 △를 예약했으면 뗄 때 다시 넣지 않음
    AttackData pressedDuring;  // 누를 때 진행 중이던 공격 → △로 넘어가기 전엔 차지로 끊지 않음

    enum DashPhase { None, Dashing, Finishing }
    DashPhase dashPhase;
    Vector3 dashStart, dashEnd;
    float dashTimer;
    int dashLevel;
    bool cutApplied;
    bool dashSheathing; // 돌진하면서 바로 납도 모션을 틀었는지
    float hitStopUntil = -1f;
    float savedTimeScale = 1f;
    CharacterController controller;
    readonly Collider[] pathBuffer = new Collider[64];
    readonly RaycastHit[] castBuffer = new RaycastHit[16];
    readonly System.Collections.Generic.List<IDamageable> pathTargets = new System.Collections.Generic.List<IDamageable>();

    public bool IsHeld => pressed && !consumed;
    public bool IsDashing => dashPhase != DashPhase.None; // PlayerHealth가 무적 판정에 씀
    public bool IsCharging => charging;
    public int Level => level;
    public int MaxLevel => levelTimes.Length;
    // HUD용 0~1 (마지막 단계 도달 시 1)
    public float ChargeRatio => charging ? Mathf.Clamp01(chargeTime / Mathf.Max(0.01f, levelTimes[levelTimes.Length - 1])) : 0f;

    public event Action ChargeStarted;
    public event Action<int> ChargeLevelChanged;
    public event Action ChargeEnded;
    public event Action<Vector3, Vector3> Dashed; // 섬격 시작·끝 (번개 궤적)
    public event Action<Vector3> TargetCut;       // 풀차지 섬격에 베인 적 위치 (낙뢰)

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (parry == null) parry = GetComponent<PlayerParry>();
        if (kanpa == null) kanpa = GetComponent<PlayerKanpa>();
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
        if (lockOn == null) lockOn = GetComponent<PlayerLockOn>();
        if (stats == null) stats = GetComponent<PlayerStats>();
        if (afterimage == null) afterimage = GetComponent<PlayerAfterimage>();
        if (stance == null) stance = GetComponent<PlayerStance>();
        if (dashTrail == null && skill != null) dashTrail = skill.DashTrail;
        controller = GetComponent<CharacterController>();
    }

    void OnEnable()
    {
        if (heavyAction == null) return;
        heavyAction.action.Enable();
        heavyAction.action.performed += OnPressed;
        heavyAction.action.canceled += OnReleased;
    }

    void OnDisable()
    {
        if (heavyAction != null)
        {
            heavyAction.action.performed -= OnPressed;
            heavyAction.action.canceled -= OnReleased;
        }
        if (charging) EndCharge(true);
        if (IsDashing) EndDash(true);
        pressed = false;
    }

    void OnPressed(InputAction.CallbackContext ctx)
    {
        pressed = true;
        holdTime = 0f;
        heavyQueued = false;
        pressedDuring = null;

        // 간파 직후 창이 열려 있으면 이번 우클릭은 뇌락으로 소비 (강공격·차지·패링보다 우선)
        if (kanpa != null && kanpa.TryFollowUp())
        {
            consumed = true;
            return;
        }

        // 콤보 중이면 패링 대신 △를 누른 순간 바로 예약 (뗄 때까지 기다리면 간파 타이밍이 밀림)
        if (kanpa != null && kanpa.HandlesPress)
        {
            consumed = false;
            heavyQueued = true;
            pressedDuring = combat.CurrentAttack;
            combat.PressHeavy();
            return;
        }

        consumed = parry != null && parry.TryParry();
    }

    void OnReleased(InputAction.CallbackContext ctx)
    {
        if (!pressed) return;
        pressed = false;

        if (charging) Release();
        else if (!consumed && !heavyQueued && combat != null) combat.PressHeavy(); // 짧게 누름 → 강공격 (콤보 버퍼로 들어감)
        consumed = false;
        heavyQueued = false;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (hitStopUntil > 0f && Time.unscaledTime >= hitStopUntil) RestoreTime();
        if (IsDashing)
        {
            UpdateDash(dt);
            return;
        }

        if (!charging)
        {
            if (!pressed || consumed) return;
            holdTime += dt;
            if (holdTime >= holdThreshold && CanCharge()) BeginCharge();
            return;
        }

        // 회피·피격·스킬·간파가 끼어들면 잠금과 애니메이션은 그쪽에 맡기고 차지만 끝냄
        if (IsInterrupted())
        {
            EndCharge(false);
            consumed = true; // 손을 떼도 강공격이 나가지 않게
            return;
        }

        chargeTime += dt;
        int next = 1;
        for (int i = 1; i < levelTimes.Length; i++)
            if (chargeTime >= levelTimes[i]) next = i + 1;
        if (next != level) SetLevel(next);

        FaceInput(dt);
    }

    bool CanCharge()
    {
        if (IsDashing) return false;
        if (locomotion != null && !locomotion.IsGrounded) return false;
        if (skill != null && skill.IsActive) return false;
        if (parry != null && parry.IsActive) return false;
        if (dodge != null && dodge.IsDodging && !dodge.CanCancel) return false;
        if (health != null && (health.IsStunned || health.IsDead)) return false;
        // 콤보 중이면 판정이 끝난 뒤부터 차지로 끊고 들어감
        if (combat == null || !combat.IsAttacking) return true;
        if (heavyQueued && combat.CurrentAttack == pressedDuring) return false; // 예약한 △가 나가기 전에는 차지 금지
        return combat.CanMoveCancel;
    }

    bool IsInterrupted()
    {
        return (dodge != null && dodge.IsDodging)
               || (skill != null && skill.IsActive)
               || (parry != null && parry.IsActive)
               || (health != null && (health.IsStunned || health.IsDead));
    }

    void BeginCharge()
    {
        if (combat != null)
        {
            if (combat.IsAttacking) combat.CancelAttack();
            combat.InputLocked = true;
        }
        if (locomotion != null) locomotion.MovementLocked = true;

        charging = true;
        chargeTime = 0f;
        level = 0;
        animator.CrossFadeInFixedTime(ChargeStartHash, 0.1f, 0, 0f);
        ChargeStarted?.Invoke();
        SetLevel(1);
    }

    void SetLevel(int next)
    {
        level = next;
        ChargeLevelChanged?.Invoke(level);
        if (followCamera != null && level > 1) followCamera.Shake(0.03f * level, 0.1f);
        if (logCharge) Debug.Log($"[Charge] {level}단계");
    }

    // [변경] 차지 강타 삭제: dashFromLevel 전에 떼면 취소, 이후면 섬격
    void Release()
    {
        int releasedLevel = level;
        if (releasedLevel < dashFromLevel)
        {
            EndCharge(true);
            if (logCharge) Debug.Log($"[Charge] {releasedLevel}단계에서 뗌 → 취소");
            return;
        }
        EndCharge(false);
        StartDash(releasedLevel);
    }

    public void Cancel() // 처형이 차지를 끊고 이어받을 때
    {
        if (charging) EndCharge(false);
        if (IsDashing) EndDash(false);
    }

    void EndCharge(bool restoreControl)
    {
        charging = false;
        level = 0;
        ChargeEnded?.Invoke();
        if (!restoreControl) return;

        if (combat != null) combat.InputLocked = false;
        if (locomotion != null) locomotion.MovementLocked = false;
        animator.CrossFadeInFixedTime(LocomotionHash, 0.15f, 0, 0f);
    }

    // 차지 중에는 제자리에서 입력 방향(락온 중이면 대상)으로 천천히 돌아 조준
    void FaceInput(float dt)
    {
        if (locomotion == null) return;
        Vector3 dir = lockOn != null && lockOn.IsLocked ? Flat(lockOn.Target.position - transform.position) : locomotion.GetInputDirection();
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
    }

    // ===== 섬격 =====
    // 핵심: 시작 순간 경로 위 적을 모아 두고, CharacterController를 끈 채 직선 이동 → 도착 후 cutDelay 뒤 일괄 피해 + 정지
    void StartDash(int level)
    {
        dashLevel = level;
        Vector3 start = transform.position;
        Vector3 forward = Flat(transform.forward).normalized;
        float distance = dashDistances[Mathf.Clamp(level - 1, 0, dashDistances.Length - 1)];
        Vector3 end = start + forward * distance;

        // 락온 대상이 사거리 안이면 그 뒤까지 꿰뚫음
        if (lockOn != null && lockOn.IsLocked)
        {
            Vector3 to = Flat(lockOn.Target.position - start);
            if (to.magnitude <= distance + behindDistance)
            {
                forward = to.normalized;
                end = start + forward * (to.magnitude + behindDistance);
            }
        }
        end = ClampByObstacles(start, end);
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        CollectPathTargets(start, end);
        dashStart = start;
        dashEnd = end;
        dashTimer = 0f;
        cutApplied = false;
        dashPhase = DashPhase.Dashing;

        if (combat != null) combat.InputLocked = true;
        if (locomotion != null) locomotion.MovementLocked = true;
        if (skill != null) skill.enabled = false;
        if (controller != null) controller.enabled = false; // 적을 통과
        if (afterimage != null) afterimage.Emit(dashDuration);
        if (dashTrail != null)
        {
            dashTrail.Clear();
            dashTrail.emitting = true;
        }
        // 돌진 순간부터 납도 모션: 지나가며 칼을 넣고, 한 박자 뒤 적이 베임
        dashSheathing = sheatheAfterDash && stance != null && stance.SheatheNow(0.03f, true); // 락온 중에도 같은 납도
        if (!dashSheathing) animator.CrossFadeInFixedTime(DashHash, 0.03f, 0, 0f);
        if (followCamera != null) followCamera.HoldFollow(0.1f);
        if (level >= 2) Dashed?.Invoke(start, end);
        if (logCharge) Debug.Log($"[Charge] 섬격 {level}단계 · {Vector3.Distance(start, end):F1}m · {pathTargets.Count}명");
    }

    void UpdateDash(float dt)
    {
        dashTimer += dt;
        if (dashPhase == DashPhase.Dashing)
        {
            float t = Mathf.Clamp01(dashTimer / dashDuration);
            transform.position = Vector3.Lerp(dashStart, dashEnd, 1f - (1f - t) * (1f - t));
            if (t < 1f) return;

            if (controller != null) controller.enabled = true;
            if (dashTrail != null) dashTrail.emitting = false;
            dashPhase = DashPhase.Finishing;
            dashTimer = 0f;
            return;
        }

        if (!cutApplied && dashTimer >= cutDelay) ApplyCuts();
        if (cutApplied && dashTimer >= cutDelay + dashRecovery) EndDash(true, true); // 끝까지 마친 경우만 납도
    }

    void ApplyCuts()
    {
        cutApplied = true;
        bool full = dashLevel >= MaxLevel;
        float mul = damageMultipliers[Mathf.Clamp(dashLevel - 1, 0, damageMultipliers.Length - 1)];
        float baseDamage = releaseAttack != null ? releaseAttack.Damage : 20f;
        float posture = releaseAttack != null ? releaseAttack.PostureDamage : 10f;
        int marks = dashMarks[Mathf.Clamp(dashLevel - 1, 0, dashMarks.Length - 1)];
        Vector3 dir = Flat(dashEnd - dashStart).normalized;

        int hit = 0;
        foreach (IDamageable target in pathTargets)
        {
            if (target == null || (target as UnityEngine.Object) == null || !target.CanBeTargeted) continue;
            Transform t = ((Component)target).transform;
            var info = new DamageInfo
            {
                Attacker = gameObject,
                Damage = baseDamage * mul,
                PostureDamage = posture * mul,
                Weight = AttackWeight.Heavy,
                BreakGuard = dashLevel >= 2,
                BreakArmor = full,
                HitPoint = t.position + Vector3.up * 1.1f,
                Direction = dir
            };
            if (stats != null) info = stats.Modify(info);
            target.ReceiveDamage(info);
            ThunderMark.Of((Component)target)?.Add(marks);
            if (full) TargetCut?.Invoke(t.position);
            hit++;
        }

        if (hit == 0) return;
        if (followCamera != null) followCamera.Shake(full ? 0.22f : 0.14f, 0.18f);
        savedTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
        Time.timeScale = 0.05f;
        hitStopUntil = Time.unscaledTime + cutHitStop * (full ? 1.5f : 1f);
    }

    void EndDash(bool restoreControl, bool finished = false)
    {
        if (!cutApplied && dashPhase != DashPhase.None) ApplyCuts();
        dashPhase = DashPhase.None;
        pathTargets.Clear();
        if (controller != null) controller.enabled = true;
        if (dashTrail != null) dashTrail.emitting = false;
        if (skill != null) skill.enabled = true;
        if (!restoreControl) return;

        if (combat != null) combat.InputLocked = false;
        if (locomotion != null) locomotion.MovementLocked = false;
        if (dashSheathing && finished) // 이미 납도 중 → 끝나면 PlayerStance가 로코모션으로 돌려줌
        {
            dashSheathing = false;
            return;
        }
        dashSheathing = false;
        animator.CrossFadeInFixedTime(LocomotionHash, 0.2f, 0, 0f);
    }

    void RestoreTime()
    {
        hitStopUntil = -1f;
        Time.timeScale = savedTimeScale;
    }

    void CollectPathTargets(Vector3 start, Vector3 end)
    {
        pathTargets.Clear();
        Vector3 up = Vector3.up;
        int count = Physics.OverlapCapsuleNonAlloc(start + up, end + up, dashPathRadius, pathBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (pathBuffer[i].transform.IsChildOf(transform)) continue;
            IDamageable target = pathBuffer[i].GetComponentInParent<IDamageable>();
            if (target == null || !target.CanBeTargeted || pathTargets.Contains(target)) continue;
            pathTargets.Add(target);
        }
    }

    // 벽에 막히면 그 앞에서 멈춤. 적과 바닥은 무시 (PlayerSkill과 같은 규칙)
    Vector3 ClampByObstacles(Vector3 start, Vector3 end)
    {
        Vector3 delta = Flat(end - start);
        float distance = delta.magnitude;
        if (distance < 0.01f || controller == null) return end;

        Vector3 dir = delta / distance;
        float radius = controller.radius * 0.9f;
        Vector3 p0 = start + Vector3.up * (radius + 0.1f);
        Vector3 p1 = start + Vector3.up * (controller.height - radius);
        int count = Physics.CapsuleCastNonAlloc(p0, p1, radius, dir, castBuffer, distance, obstacleMask, QueryTriggerInteraction.Ignore);

        float allowed = distance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = castBuffer[i];
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;
            if (hit.normal.y > 0.7f) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - 0.05f));
        }
        return start + dir * allowed;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
