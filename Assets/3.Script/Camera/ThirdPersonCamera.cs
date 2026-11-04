using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] InputActionReference lookAction;

    [SerializeField] Vector3 pivotOffset = new Vector3(0f, 1.5f, 0f);
    [SerializeField] float distance = 4f;
    [SerializeField] float sensitivity = 0.1f;
    // 이름을 바꿔 씬에 저장된 -30을 버리고 새 기본값 적용 → 땅 아래에서 올려다보는 각도 방지
    [SerializeField] float minPitchAngle = -5f;
    [SerializeField] float maxPitch = 70f;
    [SerializeField] bool lockCursor = true;
    [SerializeField] float catchUpSpeed = 12f;

    // 카메라 충돌: 벽·바닥·기둥에 가리면 앞으로 당기고, 비면 천천히 원래 거리로
    [Header("Collision")]
    [SerializeField] LayerMask collisionMask = ~0;
    [SerializeField] float collisionRadius = 0.25f;
    [SerializeField] float collisionPadding = 0.1f;
    [SerializeField] float minDistance = 0.8f;
    [SerializeField] float recoverSpeed = 6f;

    readonly RaycastHit[] castBuffer = new RaycastHit[16];
    float currentDistance = -1f;

    float yaw;
    float pitch = 15f;

    Vector3 pivot;
    bool pivotReady;
    float holdTimer;
    bool catchingUp;

    float shakeTimer;
    float shakeDuration;
    float shakeStrength;

    float zoomTimer;    // 마무리 연출용 일시 당기기
    float zoomDuration;
    float zoomAmount;

    // [변경] 간파 v2: 빠르게 당겼다가 유지 후 풀리는 펀치 줌 + 살짝 기울기
    float punchElapsed = -1f;
    float punchAmount, punchIn, punchHold, punchOut, punchRoll;

    void OnEnable()
    {
        if (lookAction != null) lookAction.action.Enable();
    }

    void Start()
    {
        if (target != null) yaw = target.eulerAngles.y;
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // 잠시 그 자리에 멈췄다가 따라잡기 → 순간 이동이 카메라보다 빠른 느낌
    public void HoldFollow(float seconds)
    {
        holdTimer = Mathf.Max(holdTimer, seconds);
    }

    // 카메라를 amount 비율만큼 잠깐 당겼다가 duration 동안 원래 거리로 (0.25 = 25% 가까이)
    public void Zoom(float amount, float duration)
    {
        zoomAmount = Mathf.Clamp01(amount);
        zoomDuration = Mathf.Max(duration, 0.0001f);
        zoomTimer = zoomDuration;
    }

    // [변경] amount 비율만큼 inTime 동안 당기고, hold 유지, outTime 동안 복귀. roll은 최대 기울기(도)
    public void Punch(float amount, float inTime, float hold, float outTime, float roll)
    {
        punchAmount = Mathf.Clamp01(amount);
        punchIn = Mathf.Max(inTime, 0.0001f);
        punchHold = Mathf.Max(hold, 0f);
        punchOut = Mathf.Max(outTime, 0.0001f);
        punchRoll = roll;
        punchElapsed = 0f;
    }

    public void Shake(float strength, float duration)
    {
        shakeStrength = Mathf.Max(strength, shakeTimer > 0f ? shakeStrength : 0f);
        shakeDuration = Mathf.Max(duration, 0.0001f);
        shakeTimer = shakeDuration;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // 히트스톱(timeScale 저하) 중에도 카메라는 실시간으로 움직이도록 unscaled 사용
        float dt = Time.unscaledDeltaTime;

        Vector2 look = lookAction != null ? lookAction.action.ReadValue<Vector2>() : Vector2.zero;
        yaw += look.x * sensitivity;
        pitch = Mathf.Clamp(pitch - look.y * sensitivity, minPitchAngle, maxPitch);

        UpdatePivot(target.position + pivotOffset, dt);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 back = -(rot * Vector3.forward);
        float punch = PunchWeight(dt); // [변경]
        float allowed = CollisionDistance(pivot, back, distance * ZoomFactor(dt) * (1f - punchAmount * punch));
        Vector3 position = pivot + back * allowed + ShakeOffset(dt);

        transform.SetPositionAndRotation(position, rot * Quaternion.Euler(0f, 0f, punchRoll * punch));
    }

    // 가릴 때는 즉시 당기고, 풀릴 때는 recoverSpeed로 서서히 원래 거리로 돌아감
    float CollisionDistance(Vector3 origin, Vector3 back, float desired) // 줌이 반영된 거리 사용
    {
        float target = desired;
        int count = Physics.SphereCastNonAlloc(origin, collisionRadius, back, castBuffer, desired, collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = castBuffer[i].collider;
            if (IsIgnored(col)) continue;

            float d = castBuffer[i].distance - collisionPadding;
            if (castBuffer[i].distance <= 0f) d = minDistance;
            target = Mathf.Min(target, Mathf.Max(minDistance, d));
        }

        if (currentDistance < 0f || target < currentDistance) currentDistance = target;
        else currentDistance = Mathf.MoveTowards(currentDistance, target, recoverSpeed * Time.unscaledDeltaTime);
        return currentDistance;
    }

    // 플레이어 자신과 움직이는 캐릭터(CharacterController를 가진 적)는 무시 → 적이 지나갈 때마다 카메라가 튀지 않게
    bool IsIgnored(Collider col)
    {
        if (col.transform.IsChildOf(target)) return true;
        if (col is CharacterController) return true;
        return col.GetComponentInParent<CharacterController>() != null;
    }

    void UpdatePivot(Vector3 desired, float dt)
    {
        if (!pivotReady)
        {
            pivot = desired;
            pivotReady = true;
            return;
        }

        if (holdTimer > 0f)
        {
            holdTimer -= dt;
            catchingUp = true;
            return;
        }

        if (!catchingUp)
        {
            pivot = desired;
            return;
        }

        pivot = Vector3.Lerp(pivot, desired, 1f - Mathf.Exp(-catchUpSpeed * dt));
        if ((pivot - desired).sqrMagnitude < 0.0025f) catchingUp = false;
    }

    // 처음에 바로 당겨지고 서서히 풀림
    float ZoomFactor(float dt)
    {
        if (zoomTimer <= 0f) return 1f;
        zoomTimer -= dt;
        float k = Mathf.Clamp01(zoomTimer / zoomDuration);
        return 1f - zoomAmount * k * k;
    }

    // [변경] 0→1(ease-out) → 유지 → 1→0(smoothstep)
    float PunchWeight(float dt)
    {
        if (punchElapsed < 0f) return 0f;

        punchElapsed += dt;
        float e = punchElapsed;
        if (e < punchIn)
        {
            float k = e / punchIn;
            return 1f - (1f - k) * (1f - k);
        }
        e -= punchIn;
        if (e < punchHold) return 1f;
        e -= punchHold;
        if (e < punchOut) return 1f - Mathf.SmoothStep(0f, 1f, e / punchOut);

        punchElapsed = -1f;
        return 0f;
    }

    Vector3 ShakeOffset(float dt)
    {
        if (shakeTimer <= 0f) return Vector3.zero;

        shakeTimer -= dt;
        float strength = shakeStrength * Mathf.Clamp01(shakeTimer / shakeDuration);
        return Random.insideUnitSphere * strength;
    }
}
