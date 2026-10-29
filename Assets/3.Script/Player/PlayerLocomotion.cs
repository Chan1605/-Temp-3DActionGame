using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerLocomotion : MonoBehaviour
{
    static readonly int MoveBlendHash = Animator.StringToHash("MoveBlend");
    static readonly int MoveAnimSpeedHash = Animator.StringToHash("MoveAnimSpeed");

    [Header("Input")]
    [SerializeField] InputActionReference moveAction;
    [SerializeField] InputActionReference sprintAction;

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

    public float CurrentSpeed => currentSpeed;
    public bool MovementLocked { get; set; }

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

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * dt;

        controller.Move((transform.forward * currentSpeed + Vector3.up * verticalVelocity) * dt);

        UpdateAnimator(dt);
    }

    public Vector3 GetInputDirection() => ToCameraRelative(ReadMoveInput());

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
