using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] InputActionReference lookAction;

    [SerializeField] Vector3 pivotOffset = new Vector3(0f, 1.5f, 0f);
    [SerializeField] float distance = 4f;
    [SerializeField] float sensitivity = 0.1f;
    [SerializeField] float minPitch = -30f;
    [SerializeField] float maxPitch = 70f;
    [SerializeField] bool lockCursor = true;
    [SerializeField] float catchUpSpeed = 12f;

    float yaw;
    float pitch = 15f;

    Vector3 pivot;
    bool pivotReady;
    float holdTimer;
    bool catchingUp;

    float shakeTimer;
    float shakeDuration;
    float shakeStrength;

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
        pitch = Mathf.Clamp(pitch - look.y * sensitivity, minPitch, maxPitch);

        UpdatePivot(target.position + pivotOffset, dt);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 position = pivot - rot * Vector3.forward * distance + ShakeOffset(dt);

        transform.SetPositionAndRotation(position, rot);
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

    Vector3 ShakeOffset(float dt)
    {
        if (shakeTimer <= 0f) return Vector3.zero;

        shakeTimer -= dt;
        float strength = shakeStrength * Mathf.Clamp01(shakeTimer / shakeDuration);
        return Random.insideUnitSphere * strength;
    }
}
