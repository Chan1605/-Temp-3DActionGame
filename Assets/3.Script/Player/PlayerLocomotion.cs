using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerLocomotion : MonoBehaviour
{
    static readonly int MoveBlendHash = Animator.StringToHash("MoveBlend");
    static readonly int MoveAnimSpeedHash = Animator.StringToHash("MoveAnimSpeed");
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion"); // 점프 상태
    static readonly int JumpStartHash = Animator.StringToHash("Jump_Start");
    static readonly int JumpLoopHash = Animator.StringToHash("Jump_Loop");
    static readonly int JumpLandHash = Animator.StringToHash("Jump_Land");

    [Header("Input")]
    [SerializeField] InputActionReference moveAction;
    [SerializeField] InputActionReference sprintAction;
    [SerializeField] InputActionReference jumpAction; // Space 점프

    [Header("References")]
    [SerializeField] Transform cameraTransform;
    [SerializeField] Animator animator;

    [Header("Speed")]
    [SerializeField] float slowWalkSpeed = 2f;
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float sprintSpeed = 9f;
    [SerializeField] float acceleration = 25f;
    [SerializeField] float deceleration = 35f;

    [Header("Turn")]
    [SerializeField] float turnSmoothTime = 0.06f;
    [SerializeField] float sprintTurnSmoothTime = 0.1f;
    [SerializeField, Range(0f, 1f)] float sharpTurnSpeedKeep = 0.5f;

    [SerializeField] float gravity = -20f;

    // 점프: 공중 공격은 v1 범위 밖이라 이동·피하기용. 착지 시 이 속도 이상으로 달리고 있으면 착지 모션 없이 바로 로코모션
    [Header("Jump")]
    [SerializeField] float jumpHeight = 1.2f;
    [SerializeField] float airborneDelay = 0.15f;
    [SerializeField] float landMoveThreshold = 1.5f;
    [SerializeField] float jumpCrossFade = 0.05f;
    [SerializeField] float landCrossFade = 0.08f;
    [SerializeField] float jumpBuffer = 0.15f; // [변경] 회피·공격 끝나기 직전에 누른 점프도 나가게

    [Header("Animation")]
    [SerializeField] float walkClipSpeed = 1.6f;
    [SerializeField] float runClipSpeed = 4.5f;
    [SerializeField] float sprintClipSpeed = 7f;
    [SerializeField] float blendDampTime = 0.08f;
    [SerializeField] Vector2 animSpeedRange = new Vector2(0.7f, 1.6f);

    CharacterController controller;
    float currentSpeed;
    float turnVelocity;
    float verticalVelocity;
    float airTime;
    bool airAnim;
    float lastJumpPress = float.NegativeInfinity; // [변경]

    public float CurrentSpeed => currentSpeed;
    public bool MovementLocked { get; set; }
    // 경사·계단에서 isGrounded가 잠깐 끊겨도 공중으로 치지 않도록 airborneDelay만큼 여유를 둠
    public bool IsGrounded => airTime < airborneDelay;
    public bool HasBufferedJump => Time.time - lastJumpPress <= jumpBuffer; // [변경]

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        // RM 클립을 제자리 재생시키고 이동은 CharacterController가 담당
        if (animator != null) animator.applyRootMotion = false;
    }

    void OnEnable()
    {
        if (moveAction != null) moveAction.action.Enable();
        if (sprintAction != null) sprintAction.action.Enable();
        if (jumpAction != null) jumpAction.action.Enable();
    }

    void Update()
    {
        // 스킬 돌진 중처럼 컨트롤러가 꺼져 있으면 이동·중력 모두 건너뜀
        if (!controller.enabled) return;

        float dt = Time.deltaTime;

        Vector2 input = ReadMoveInput();
        bool sprint = sprintAction != null && sprintAction.action.IsPressed();
        Vector3 moveDir = MovementLocked ? Vector3.zero : ToCameraRelative(input);

        if (MovementLocked)
        {
            currentSpeed = 0f;
            turnVelocity = 0f; // [변경] 잠금 전 회전 속도가 남아 풀린 직후 휙 도는 것 방지
        }
        else
        {
            float targetSpeed = moveDir.sqrMagnitude > 0.0001f ? TargetSpeed(Mathf.Clamp01(input.magnitude), sprint) : 0f;

            if (moveDir.sqrMagnitude > 0.0001f)
            {
                float targetYaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
                float angleGap = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, targetYaw));

                // 속도는 항상 바라보는 방향으로만 낸다 → 방향 전환 시 옆으로 밀리지 않고 호를 그리며 돈다
                // 급선회(45~180도)일수록 속도를 잠깐 줄여 원을 작게 만든다
                targetSpeed *= Mathf.Lerp(1f, sharpTurnSpeedKeep, Mathf.InverseLerp(45f, 180f, angleGap));

                float smooth = sprint ? sprintTurnSmoothTime : turnSmoothTime;
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref turnVelocity, smooth);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            float rate = targetSpeed > currentSpeed ? acceleration : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * dt);
        }

        // 점프 입력 → 이동 → 착지 판정 순서
        bool wasGrounded = IsGrounded;
        if (jumpAction != null && jumpAction.action.WasPressedThisFrame()) lastJumpPress = Time.time; // [변경] 버퍼로 기록
        if (!MovementLocked && wasGrounded && HasBufferedJump) Jump();

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * dt;

        CollisionFlags flags = controller.Move((transform.forward * currentSpeed + Vector3.up * verticalVelocity) * dt);
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f; // 천장에 닿으면 바로 낙하

        UpdateAirState(dt, wasGrounded);
        UpdateAnimator(dt);
    }

    public Vector3 GetInputDirection() => ToCameraRelative(ReadMoveInput());

    // [변경] 회피 등이 끝날 때 호출: 입력 중이면 목표 속도로 바로 시작하고 블렌드도 즉시 맞춤
    // 바라보는 방향과 입력이 어긋난 만큼은 급선회 규칙(sharpTurnSpeedKeep)대로 줄여서 옆으로 미끄러지지 않게 함
    public void CarryMomentum()
    {
        Vector2 input = ReadMoveInput();
        Vector3 dir = ToCameraRelative(input);
        if (dir.sqrMagnitude < 0.0001f) return;

        bool sprint = sprintAction != null && sprintAction.action.IsPressed();
        float align = Mathf.Clamp01(Vector3.Dot(transform.forward, dir));
        currentSpeed = TargetSpeed(Mathf.Clamp01(input.magnitude), sprint) * Mathf.Lerp(sharpTurnSpeedKeep, 1f, align);
        if (animator != null) animator.SetFloat(MoveBlendHash, SpeedToBlend(currentSpeed));
    }

    // ===== 점프 =====
    void Jump()
    {
        lastJumpPress = float.NegativeInfinity; // [변경]
        verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
        airTime = airborneDelay;
        airAnim = true;
        if (animator != null) animator.CrossFadeInFixedTime(JumpStartHash, jumpCrossFade, 0, 0f);
    }

    void UpdateAirState(float dt, bool wasGrounded)
    {
        if (controller.isGrounded) airTime = 0f;
        else airTime += dt;

        if (wasGrounded && !IsGrounded)
        {
            // 점프 없이 턱에서 떨어진 경우 → 낙하 모션
            if (!airAnim && !MovementLocked && animator != null)
            {
                airAnim = true;
                animator.CrossFadeInFixedTime(JumpLoopHash, 0.15f, 0, 0f);
            }
        }
        else if (!wasGrounded && IsGrounded)
        {
            Land();
        }
    }

    void Land()
    {
        if (!airAnim) return;
        airAnim = false;

        // 공중에서 피격 등 다른 행동이 애니메이션을 가져갔으면 건드리지 않음
        if (animator == null || !IsInAirState()) return;

        if (currentSpeed > landMoveThreshold) animator.CrossFadeInFixedTime(LocomotionHash, 0.12f, 0, 0f);
        else animator.CrossFadeInFixedTime(JumpLandHash, landCrossFade, 0, 0f);
    }

    bool IsInAirState()
    {
        int current = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        int next = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0).shortNameHash : 0;
        return current == JumpStartHash || current == JumpLoopHash || next == JumpStartHash || next == JumpLoopHash;
    }

    float TargetSpeed(float inputAmount, bool sprint)
    {
        if (sprint) return sprintSpeed;
        // 패드 스틱을 살짝 기울이면 천천히, 끝까지 밀면 기본 속도
        return Mathf.Lerp(slowWalkSpeed, moveSpeed, Mathf.InverseLerp(0.3f, 0.9f, inputAmount));
    }

    void UpdateAnimator(float dt)
    {
        if (animator == null) return;

        animator.SetFloat(MoveBlendHash, SpeedToBlend(currentSpeed), blendDampTime, dt);

        // 블렌드된 클립의 원래 이동 속도 대비 실제 속도로 재생 배속을 맞춰 발 미끄러짐을 줄임
        float clipSpeed = BlendToClipSpeed(animator.GetFloat(MoveBlendHash));
        float playRate = clipSpeed > 0.05f ? Mathf.Clamp(currentSpeed / clipSpeed, animSpeedRange.x, animSpeedRange.y) : 1f;
        animator.SetFloat(MoveAnimSpeedHash, playRate);
    }

    // 블렌드 트리 임계값: 0 Idle, 1 Walk, 2 Run, 3 Sprint
    float SpeedToBlend(float speed)
    {
        if (speed <= slowWalkSpeed) return speed / slowWalkSpeed;
        if (speed <= moveSpeed) return 1f + (speed - slowWalkSpeed) / (moveSpeed - slowWalkSpeed);
        return 2f + Mathf.Clamp01((speed - moveSpeed) / (sprintSpeed - moveSpeed));
    }

    float BlendToClipSpeed(float blend)
    {
        if (blend <= 1f) return Mathf.Lerp(0f, walkClipSpeed, blend);
        if (blend <= 2f) return Mathf.Lerp(walkClipSpeed, runClipSpeed, blend - 1f);
        return Mathf.Lerp(runClipSpeed, sprintClipSpeed, blend - 2f);
    }

    Vector2 ReadMoveInput() => moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;

    Vector3 ToCameraRelative(Vector2 input)
    {
        if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }

        return (forward * input.y + right * input.x).normalized;
    }
}
