using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 우클릭 하나로 세 가지 (가드 대신):
//  누른 순간 적 공격 직전이면 → 패링 (대기·이동 중)
//  [변경] 콤보 중 누르면      → △를 바로 예약, △ 초반에 적 공격이 닿으면 간파 (PlayerKanpa)
//  짧게 눌렀다 떼면       → 강공격 △ (콤보 분기: □□△, □□□△ …)
//  길게 누르고 있으면     → 차지 자세, 단계(1~3)가 오르다 떼는 순간 차지 강타 (오공 차지 공격 느낌)
public class PlayerChargeAttack : MonoBehaviour
{
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
    static readonly int ChargeStartHash = Animator.StringToHash("Charge_Start");

    [SerializeField] InputActionReference heavyAction;
    [SerializeField] AttackData releaseAttack; // 떼는 순간 나가는 내려치기 (Charge_Release 상태)

    [SerializeField] Animator animator;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerParry parry;
    [SerializeField] PlayerKanpa kanpa; // [변경]
    [SerializeField] PlayerDodge dodge;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerHealth health;
    [SerializeField] ThirdPersonCamera followCamera;

    [Header("Input")]
    [SerializeField] float holdThreshold = 0.2f; // 이보다 오래 누르면 차지, 짧으면 강공격

    // levelTimes: 차지 자세 진입 후 각 단계에 도달하는 시간 (1단계는 바로)
    [Header("Charge")]
    [SerializeField] float[] levelTimes = { 0f, 0.55f, 1.15f };
    [SerializeField] float[] damageMultipliers = { 1.4f, 2f, 3f };
    [SerializeField] float turnSpeed = 360f;
    [SerializeField] bool logCharge = true;

    bool pressed;
    bool consumed;   // 누른 순간 간파가 나갔으면 이번 입력은 끝
    float holdTime;
    bool charging;
    float chargeTime;
    int level;
    bool heavyQueued;          // [변경] 콤보 중 누른 순간 이미 △를 예약했으면 뗄 때 다시 넣지 않음
    AttackData pressedDuring;  // [변경] 누를 때 진행 중이던 공격 → △로 넘어가기 전엔 차지로 끊지 않음

    public bool IsHeld => pressed && !consumed;
    public bool IsCharging => charging;
    public int Level => level;
    public int MaxLevel => levelTimes.Length;
    // HUD용 0~1 (마지막 단계 도달 시 1)
    public float ChargeRatio => charging ? Mathf.Clamp01(chargeTime / Mathf.Max(0.01f, levelTimes[levelTimes.Length - 1])) : 0f;

    public event Action ChargeStarted;
    public event Action<int> ChargeLevelChanged;
    public event Action ChargeEnded;

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
        pressed = false;
    }

    void OnPressed(InputAction.CallbackContext ctx)
    {
        pressed = true;
        holdTime = 0f;
        heavyQueued = false;
        pressedDuring = null;

        // [변경] 콤보 중이면 패링 대신 △를 누른 순간 바로 예약 (뗄 때까지 기다리면 간파 타이밍이 밀림)
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
        heavyQueued = false; // [변경]
    }

    void Update()
    {
        float dt = Time.deltaTime;

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
        if (locomotion != null && !locomotion.IsGrounded) return false;
        if (skill != null && skill.IsActive) return false;
        if (parry != null && parry.IsActive) return false;
        if (dodge != null && dodge.IsDodging && !dodge.CanCancel) return false;
        if (health != null && (health.IsStunned || health.IsDead)) return false;
        // 콤보 중이면 판정이 끝난 뒤부터 차지로 끊고 들어감
        if (combat == null || !combat.IsAttacking) return true;
        if (heavyQueued && combat.CurrentAttack == pressedDuring) return false; // [변경] 예약한 △가 나가기 전에는 차지 금지
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

    // 핵심: 떼는 순간 단계 배율을 실어 내려치기. 판정·캔슬·이펙트는 PlayerCombat의 일반 공격 흐름을 그대로 탐
    void Release()
    {
        int releasedLevel = level;
        if (combat == null || releaseAttack == null)
        {
            Debug.LogWarning("[Charge] Release Attack이 비어 있습니다. Tools/Setup/Charge Attack을 실행하세요.");
            EndCharge(true);
            return;
        }
        EndCharge(false);

        combat.InputLocked = false;
        float mul = damageMultipliers[Mathf.Clamp(releasedLevel - 1, 0, damageMultipliers.Length - 1)];
        combat.StartChargedAttack(releaseAttack, mul, releasedLevel);
        if (followCamera != null && releasedLevel >= MaxLevel) followCamera.Zoom(0.12f, 0.35f);
        if (logCharge) Debug.Log($"[Charge] 차지 강타 {releasedLevel}단계");
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

    // 차지 중에는 제자리에서 입력 방향으로 천천히 돌아 조준
    void FaceInput(float dt)
    {
        if (locomotion == null) return;
        Vector3 dir = locomotion.GetInputDirection();
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
    }
}
