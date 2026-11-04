using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class PlayerCombat : MonoBehaviour
{
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    [FormerlySerializedAs("attackAction")]
    [SerializeField] InputActionReference lightAttackAction;
    // [변경] 우클릭(강공격·간파·차지)은 PlayerChargeAttack이 받아서 탭이면 PressHeavy()로 넘겨줌

    [SerializeField] AttackData lightStarter;
    [SerializeField] AttackData heavyStarter;

    [SerializeField] Animator animator;
    [SerializeField] WeaponHitbox weapon;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerTargeting targeting; // P2 소프트 타겟팅

    [SerializeField] float attackCrossFade = 0.08f;
    [SerializeField] float returnCrossFade = 0.2f;
    [SerializeField] float moveCancelCrossFade = 0.12f;
    [SerializeField] float idleInputBuffer = 0.2f;
    [FormerlySerializedAs("rootMotionWeight")]
    [SerializeField] float stepWeight = 1f;
    [SerializeField] bool logComboRoute = true;
    [SerializeField] float swingFxTail = 0.06f; // 판정이 끝난 뒤 궤적·검기를 조금 더 유지하는 정규화 시간

    CharacterController controller;

    AttackData current;
    float elapsed;
    float attackStartTime;
    float lastPressTime = float.NegativeInfinity;
    AttackInput lastPressInput;
    bool swingActive;
    bool swingFxActive;
    float stepTraveled;
    float stepTotal; // 이번 공격에서 내딛을 총 거리 (타겟팅 시 끌어당김 포함)
    float damageMultiplier = 1f; // [변경] 차지 강타 단계별 배율 (일반 공격은 1)
    int chargeLevel;             // [변경] 0 = 일반 공격
    string route = "";

    public bool IsAttacking => current != null;
    public AttackData CurrentAttack => current;
    public float AttackElapsed => current != null ? elapsed : 0f; // 회피가 공격을 끊을 수 있는 시점 판단용
    public bool InputLocked { get; set; }
    public bool CanCancel => current != null && elapsed / current.Duration >= current.ComboTransition;
    public bool HasBufferedPress => Time.time - lastPressTime <= idleInputBuffer; // 회피가 공격으로 끊어줄지 판단용
    public bool CanMoveCancel => current != null && elapsed / current.Duration >= current.MoveCancelStart; // 차지가 공격을 끊을 수 있는 시점
    public int CurrentChargeLevel => current != null ? chargeLevel : 0; // [변경] PlayerThunder가 차지 단계별 뇌인 계산에 사용

    // 이펙트(PlayerVfx)가 구독하는 이벤트 — 전투 코드는 이펙트를 직접 모름
    public event System.Action<AttackData, AttackInput> AttackStarted;
    public event System.Action AttackSwingFinished;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        controller = GetComponent<CharacterController>();
        if (targeting == null) targeting = GetComponent<PlayerTargeting>();
    }

    void OnEnable()
    {
        Subscribe(lightAttackAction, OnLightPressed);
    }

    void OnDisable()
    {
        Unsubscribe(lightAttackAction, OnLightPressed);
        if (IsAttacking) EndCombo(returnCrossFade);
    }

    void OnLightPressed(InputAction.CallbackContext ctx) => RegisterPress(AttackInput.Light);
    public void PressHeavy() => RegisterPress(AttackInput.Heavy); // [변경] 우클릭을 짧게 눌렀다 뗐을 때 (PlayerChargeAttack이 호출)

    // [변경] 차지 강타: 콤보와 같은 흐름(내딛기·판정·캔슬)으로 재생하되 피해만 단계 배율을 곱함
    public void StartChargedAttack(AttackData attack, float multiplier, int level)
    {
        if (attack == null) return;
        if (IsAttacking) CancelAttack();
        lastPressInput = AttackInput.Heavy;
        StartAttack(attack, true);
        damageMultiplier = multiplier;
        chargeLevel = level;
    }

    // 버퍼는 마지막 입력 하나만 기억: □ 누르고 바로 △ 누르면 △가 우선
    // 잠겨 있을 때(회피·피격·간파 중)도 기록 → 풀리는 순간 버퍼 안의 입력이 바로 나감
    void RegisterPress(AttackInput input)
    {
        lastPressTime = Time.time;
        lastPressInput = input;
    }

    void Update()
    {
        if (InputLocked && !IsAttacking) return;

        if (!IsAttacking)
        {
            if (HasBufferedPress && IsGrounded()) // 공중에서는 공격 시작 안 함 (착지 직전 입력은 버퍼로 착지 후 나감)
            {
                AttackData starter = lastPressInput == AttackInput.Light ? lightStarter : heavyStarter;
                if (starter != null) StartAttack(starter, true);
            }
            return;
        }

        elapsed += Time.deltaTime;
        float t = elapsed / current.Duration;

        ApplyStep(t);

        // 판정 구간 진입/이탈 시에만 히트박스를 켜고 끔
        bool inHitWindow = t >= current.HitStart && t <= current.HitEnd;
        if (inHitWindow && !swingActive) BeginSwing(current);
        else if (!inHitWindow && swingActive) EndSwing();

        if (swingFxActive && t >= current.HitEnd + swingFxTail) FinishSwingFx();

        // inputOpen 이후 눌린 입력만 인정하고, 그 입력에 연결된 다음 공격이 있으면 comboTransition에 분기
        bool buffered = lastPressTime >= attackStartTime + current.InputOpen * current.Duration;
        if (buffered && t >= current.ComboTransition)
        {
            AttackData next = current.GetNext(lastPressInput);
            if (next != null)
            {
                StartAttack(next, false);
                return;
            }
        }

        // 판정이 끝난 뒤에는 이동 입력이 들어오는 즉시 공격을 끊고 이동으로 넘어감
        if (t >= current.MoveCancelStart && HasMoveInput())
        {
            EndCombo(moveCancelCrossFade);
            return;
        }

        if (t >= current.RecoveryEnd) EndCombo(returnCrossFade);
    }

    void StartAttack(AttackData attack, bool isStarter)
    {
        if (swingActive) EndSwing();

        AttackInput input = lastPressInput;
        current = attack;
        damageMultiplier = 1f; // [변경]
        chargeLevel = 0;
        elapsed = 0f;
        stepTraveled = 0f;
        attackStartTime = Time.time;
        lastPressTime = float.NegativeInfinity;

        float baseStep = attack.StepDistance * stepWeight;
        stepTotal = baseStep;

        if (locomotion != null)
        {
            Vector3 dir = locomotion.GetInputDirection();

            // 대상이 있으면 그쪽으로 돌고, 멈출 거리(stopDistance)까지 최대 maxPull만큼 더 내딛음
            // 이미 가까우면 기본 내딛기도 줄여서 적을 파고들지 않게 함
            if (targeting != null && targeting.TryFindTarget(dir, out Transform target, out float distance))
            {
                Vector3 to = target.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                stepTotal = Mathf.Clamp(distance - targeting.StopDistance, 0f, baseStep + targeting.MaxPull);
            }
            else if (dir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

            locomotion.MovementLocked = true;
        }

        animator.CrossFadeInFixedTime(attack.StateHash, attackCrossFade, 0, 0f);

        swingFxActive = true;
        AttackStarted?.Invoke(attack, input);

        if (logComboRoute)
        {
            route = (isStarter ? "" : route) + (input == AttackInput.Light ? "□" : "△");
            Debug.Log($"[Combo] {route}  ({attack.name})");
        }
    }

    // 스킬 등 다른 행동이 이어받을 때 사용 → 애니메이션 전환은 호출한 쪽이 담당
    public void CancelAttack()
    {
        if (swingActive) EndSwing();
        FinishSwingFx();
        current = null;
        lastPressTime = float.NegativeInfinity;
    }

    void EndCombo(float crossFade)
    {
        if (swingActive) EndSwing();
        FinishSwingFx();
        current = null;
        if (locomotion != null) locomotion.MovementLocked = false;
        animator.CrossFadeInFixedTime(LocomotionHash, crossFade, 0, 0f);
    }

    // 궤적·검기 종료 알림 (한 번만)
    void FinishSwingFx()
    {
        if (!swingFxActive) return;
        swingFxActive = false;
        AttackSwingFinished?.Invoke();
    }

    bool IsGrounded() => locomotion == null || locomotion.IsGrounded;

    bool HasMoveInput() => locomotion != null && locomotion.GetInputDirection().sqrMagnitude > 0.0001f;

    // 공격 초반(0 ~ stepEnd)에만 정면으로 내딛음, ease-out이라 처음에 빠르고 끝에서 멈추듯 감속
    // 루트 모션 대신 코드로 처리 → Humanoid 몸 높이(bodyPosition)를 건드리지 않음
    void ApplyStep(float t)
    {
        if (controller == null || !controller.enabled) return;
        if (current.StepEnd <= 0f || stepTotal <= 0f) return;

        float p = Mathf.Clamp01(t / current.StepEnd);
        float eased = 1f - (1f - p) * (1f - p);
        float target = stepTotal * eased;

        float delta = target - stepTraveled;
        if (delta <= 0f) return;

        stepTraveled = target;
        controller.Move(transform.forward * delta);
    }

    void BeginSwing(AttackData attack)
    {
        swingActive = true;
        if (weapon == null) return;

        weapon.BeginSwing(new DamageInfo
        {
            Attacker = gameObject,
            Damage = attack.Damage * damageMultiplier, // [변경]
            PostureDamage = attack.PostureDamage * damageMultiplier,
            Parryable = attack.Parryable,
            Weight = attack.Weight
        });
    }

    void EndSwing()
    {
        swingActive = false;
        if (weapon != null) weapon.EndSwing();
    }

    static void Subscribe(InputActionReference reference, System.Action<InputAction.CallbackContext> handler)
    {
        if (reference == null) return;
        reference.action.Enable();
        reference.action.performed += handler;
    }

    static void Unsubscribe(InputActionReference reference, System.Action<InputAction.CallbackContext> handler)
    {
        if (reference != null) reference.action.performed -= handler;
    }
}
