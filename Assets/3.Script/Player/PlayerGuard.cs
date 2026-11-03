using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 가드: 우클릭을 누른 순간 간파를 먼저 시도하고, 실패하거나 계속 누르고 있으면 가드 자세.
// 정면 공격은 경직 없이 일부 피해만 받고 밀려남. 뇌기 보상이 없어 회피·간파보다 손해인 안전책.
public class PlayerGuard : MonoBehaviour
{
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
    static readonly int GuardStartHash = Animator.StringToHash("Guard_Start");
    static readonly int GuardHitHash = Animator.StringToHash("Guard_Hit");

    [SerializeField] InputActionReference guardAction;

    [SerializeField] Animator animator;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerParry parry;
    [SerializeField] PlayerDodge dodge;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerHealth health;
    [SerializeField] ThirdPersonCamera followCamera;

    [Header("Block")]
    [SerializeField] float guardAngle = 120f;
    [SerializeField, Range(0f, 1f)] float chipDamageRatio = 0.2f;
    [SerializeField] float pushBack = 0.4f;
    [SerializeField] float blockStunTime = 0.3f;
    [SerializeField] float shakeStrength = 0.05f;

    [Header("Animation")]
    [SerializeField] float startCrossFade = 0.08f;
    [SerializeField] float endCrossFade = 0.15f;
    [SerializeField] bool logGuard = true;

    CharacterController controller;
    bool guarding;
    float blockStunTimer;
    float pushTraveled;
    Vector3 pushDir;

    public bool IsGuarding => guarding;
    public bool IsHeld => guardAction != null && guardAction.action.IsPressed(); // [변경] 회피가 가드로 끊어줄지 판단용
    public float ChipDamageRatio => chipDamageRatio;
    public event Action<DamageInfo> Blocked;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (parry == null) parry = GetComponent<PlayerParry>();
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
    }

    void OnEnable()
    {
        if (guardAction == null) return;
        guardAction.action.Enable();
        guardAction.action.performed += OnGuardPressed;
    }

    void OnDisable()
    {
        if (guardAction != null) guardAction.action.performed -= OnGuardPressed;
        if (guarding) End(true);
    }

    // 누른 순간 한 번만 간파 판정. 성공하면 PlayerParry가 제어권을 가져가 가드는 시작되지 않음
    void OnGuardPressed(InputAction.CallbackContext ctx)
    {
        if (parry != null) parry.TryParry();
    }

    void Update()
    {
        bool held = IsHeld; // [변경]

        if (!guarding)
        {
            if (held && CanGuard()) Begin();
            return;
        }

        // 회피·간파·스킬·피격·공격이 끼어들면 잠금과 애니메이션은 그쪽에 맡기고 가드만 조용히 해제
        if (IsInterrupted())
        {
            guarding = false;
            blockStunTimer = 0f;
            return;
        }

        if (blockStunTimer > 0f)
        {
            UpdateBlockStun(Time.deltaTime);
            return; // 막는 모션 중에는 손을 떼도 바로 풀지 않음
        }

        if (!held) End(true);
    }

    bool CanGuard()
    {
        if (locomotion != null && !locomotion.IsGrounded) return false;
        if (skill != null && skill.IsActive) return false;
        if (parry != null && parry.IsActive) return false;
        if (dodge != null && dodge.IsDodging) return false;
        if (health != null && health.IsStunned) return false;

        // 공격 중이면 판정이 끝난 뒤(이동 캔슬 가능 시점)부터 가드로 끊음 → 그 전에 누르면 이어서 대기
        if (combat != null && combat.IsAttacking && !combat.CanMoveCancel) return false;
        return true;
    }

    bool IsInterrupted()
    {
        return (skill != null && skill.IsActive)
               || (parry != null && parry.IsActive)
               || (dodge != null && dodge.IsDodging)
               || (health != null && health.IsStunned)
               || (combat != null && combat.IsAttacking)
               || (locomotion != null && !locomotion.IsGrounded);
    }

    void Begin()
    {
        if (combat != null && combat.IsAttacking) combat.CancelAttack();

        // 입력 방향이 있으면 그쪽을 보고 막음 (가드는 정면 guardAngle 범위만)
        if (locomotion != null)
        {
            Vector3 dir = locomotion.GetInputDirection();
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            locomotion.MovementLocked = true;
        }

        guarding = true;
        blockStunTimer = 0f;
        animator.CrossFadeInFixedTime(GuardStartHash, startCrossFade, 0, 0f);
        if (logGuard) Debug.Log("[Guard] 가드 시작");
    }

    // PlayerHealth가 피격 직전에 호출. true면 막은 것 (깎임 피해는 PlayerHealth가 처리)
    public bool TryBlock(DamageInfo info)
    {
        if (!guarding) return false;

        Vector3 toAttacker = info.Attacker != null ? info.Attacker.transform.position - transform.position : -info.Direction;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, toAttacker) > guardAngle * 0.5f) return false;

        pushDir = toAttacker.sqrMagnitude > 0.0001f ? -toAttacker.normalized : -transform.forward;
        pushTraveled = 0f;
        blockStunTimer = blockStunTime;
        if (combat != null) combat.InputLocked = true;

        animator.CrossFadeInFixedTime(GuardHitHash, 0.03f, 0, 0f);
        if (followCamera != null) followCamera.Shake(shakeStrength, 0.1f);

        Blocked?.Invoke(info);
        if (logGuard) Debug.Log($"[Guard] 막음 · 깎임 피해 {info.Damage * chipDamageRatio:0.#}");
        return true;
    }

    // 막는 동안 뒤로 밀림 (ease-out), 끝나면 공격 입력 다시 허용
    void UpdateBlockStun(float dt)
    {
        blockStunTimer -= dt;

        float p = 1f - Mathf.Clamp01(blockStunTimer / blockStunTime);
        float eased = 1f - (1f - p) * (1f - p);
        float delta = pushBack * eased - pushTraveled;
        if (delta > 0f && controller != null && controller.enabled)
        {
            controller.Move(pushDir * delta);
            pushTraveled += delta;
        }

        if (blockStunTimer <= 0f && combat != null) combat.InputLocked = false;
    }

    void End(bool restoreControl)
    {
        guarding = false;
        if (!restoreControl) return;

        if (blockStunTimer > 0f && combat != null) combat.InputLocked = false;
        blockStunTimer = 0f;
        if (locomotion != null) locomotion.MovementLocked = false;
        animator.CrossFadeInFixedTime(LocomotionHash, endCrossFade, 0, 0f);
        if (logGuard) Debug.Log("[Guard] 가드 해제");
    }
}
