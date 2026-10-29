using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class PlayerCombat : MonoBehaviour
{
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    [FormerlySerializedAs("attackAction")]
    [SerializeField] InputActionReference lightAttackAction;
    [SerializeField] InputActionReference heavyAttackAction;

    [SerializeField] AttackData lightStarter;
    [SerializeField] AttackData heavyStarter;

    [SerializeField] Animator animator;
    [SerializeField] WeaponHitbox weapon;
    [SerializeField] PlayerLocomotion locomotion;

    [SerializeField] float attackCrossFade = 0.08f;
    [SerializeField] float returnCrossFade = 0.2f;
    [SerializeField] float moveCancelCrossFade = 0.12f;
    [SerializeField] float idleInputBuffer = 0.2f;
    [SerializeField] float rootMotionWeight = 1f;
    [SerializeField] bool logComboRoute = true;

    CharacterController controller;
    AnimatorMoveRelay moveRelay;

    AttackData current;
    float elapsed;
    float attackStartTime;
    float lastPressTime = float.NegativeInfinity;
    AttackInput lastPressInput;
    bool swingActive;
    string route = "";

    public bool IsAttacking => current != null;
    public AttackData CurrentAttack => current;
    public bool InputLocked { get; set; }
    public bool CanCancel => current != null && elapsed / current.Duration >= current.ComboTransition;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        controller = GetComponent<CharacterController>();

        // OnAnimatorMove는 Animator가 붙은 모델 오브젝트에서만 호출되므로 중계 컴포넌트를 둠
        if (animator != null && !animator.TryGetComponent(out moveRelay))
            moveRelay = animator.gameObject.AddComponent<AnimatorMoveRelay>();
    }

    void OnEnable()
    {
        Subscribe(lightAttackAction, OnLightPressed);
        Subscribe(heavyAttackAction, OnHeavyPressed);
        if (moveRelay != null) moveRelay.Moved += OnAnimatorMoved;
    }

    void OnDisable()
    {
        Unsubscribe(lightAttackAction, OnLightPressed);
        Unsubscribe(heavyAttackAction, OnHeavyPressed);
        if (moveRelay != null) moveRelay.Moved -= OnAnimatorMoved;
        if (IsAttacking) EndCombo(returnCrossFade);
    }

    void OnLightPressed(InputAction.CallbackContext ctx) => RegisterPress(AttackInput.Light);
    void OnHeavyPressed(InputAction.CallbackContext ctx) => RegisterPress(AttackInput.Heavy);

    // 버퍼는 마지막 입력 하나만 기억: □ 누르고 바로 △ 누르면 △가 우선
    void RegisterPress(AttackInput input)
    {
        if (InputLocked) return;
        lastPressTime = Time.time;
        lastPressInput = input;
    }

    void Update()
    {
        if (InputLocked && !IsAttacking) return;

        if (!IsAttacking)
        {
            if (Time.time - lastPressTime <= idleInputBuffer)
            {
                AttackData starter = lastPressInput == AttackInput.Light ? lightStarter : heavyStarter;
                if (starter != null) StartAttack(starter, true);
            }
            return;
        }

        elapsed += Time.deltaTime;
        float t = elapsed / current.Duration;

        // 판정 구간 진입/이탈 시에만 히트박스를 켜고 끔
        bool inHitWindow = t >= current.HitStart && t <= current.HitEnd;
        if (inHitWindow && !swingActive) BeginSwing(current);
        else if (!inHitWindow && swingActive) EndSwing();

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
        elapsed = 0f;
        attackStartTime = Time.time;
        lastPressTime = float.NegativeInfinity;

        if (locomotion != null)
        {
            Vector3 dir = locomotion.GetInputDirection();
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            locomotion.MovementLocked = true;
        }

        animator.CrossFadeInFixedTime(attack.StateHash, attackCrossFade, 0, 0f);

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
        current = null;
        lastPressTime = float.NegativeInfinity;
    }

    void EndCombo(float crossFade)
    {
        if (swingActive) EndSwing();
        current = null;
        if (locomotion != null) locomotion.MovementLocked = false;
        animator.CrossFadeInFixedTime(LocomotionHash, crossFade, 0, 0f);
    }

    bool HasMoveInput() => locomotion != null && locomotion.GetInputDirection().sqrMagnitude > 0.0001f;

    // 공격 중에만 클립의 루트 모션을 수평으로 적용 → 베면서 앞으로 내딛는 느낌
    void OnAnimatorMoved(Vector3 deltaPosition, Quaternion deltaRotation)
    {
        if (!IsAttacking || controller == null || !controller.enabled) return;

        Vector3 move = Vector3.ProjectOnPlane(deltaPosition, Vector3.up) * (current.RootMotionScale * rootMotionWeight);
        if (move.sqrMagnitude > 0f) controller.Move(move);
    }

    void BeginSwing(AttackData attack)
    {
        swingActive = true;
        if (weapon == null) return;

        weapon.BeginSwing(new DamageInfo
        {
            Attacker = gameObject,
            Damage = attack.Damage,
            PostureDamage = attack.PostureDamage,
            Parryable = attack.Parryable
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
