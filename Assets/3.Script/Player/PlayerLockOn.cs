using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 락온: 휠 클릭 또는 Tab으로 켜고 끔. 켜면 화면 가운데에 가까운 적을 잡고
//  - 카메라가 대상을 따라 돌고(ThirdPersonCamera), 플레이어는 대상을 바라본 채 옆걸음·뒷걸음(PlayerLocomotion)
//  - 공격은 입력 방향과 상관없이 대상에게 붙음(PlayerTargeting)
// 대상 바꾸기: 휠 위·아래 (마우스 휙 전환은 옵션, 기본 끔). 대상이 쓰러지면 가까운 적으로 자동 전환, 없으면 해제
public class PlayerLockOn : MonoBehaviour
{
    [SerializeField] ThirdPersonCamera followCamera;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerHealth health;

    [SerializeField] float lockRange = 18f;
    [SerializeField] float breakRange = 24f;
    [SerializeField] bool switchByMouseFlick;      // 마우스를 휙 움직여도 바뀌면 어지러워서 기본은 휠만
    [SerializeField] float flickThreshold = 60f;  // 이만큼(픽셀) 빠르게 휙 움직이면 옆 대상으로
    [SerializeField] float flickDecay = 10f;
    [SerializeField] float switchCooldown = 0.3f;
    [SerializeField] bool logLockOn;

    readonly Collider[] buffer = new Collider[256];

    Transform target;
    IDamageable targetDamageable;
    Camera cam;
    float flick;
    float lastSwitch = float.NegativeInfinity;

    public Transform Target => target;
    public bool IsLocked => target != null;
    public event Action<Transform> TargetChanged; // null = 해제 (락온 표시 UI용)

    public void Release() => SetTarget(null); // 발도 자세 조준처럼 다른 기능이 락온을 풀 때

    void Awake()
    {
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (health == null) health = GetComponent<PlayerHealth>();
    }

    void OnDisable() => SetTarget(null);

    void Update()
    {
        if (GamePause.IsPaused) return; // [변경] 일시정지 중엔 입력·진행을 멈춤
        if (cam == null) cam = Camera.main;
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        if ((mouse != null && mouse.middleButton.wasPressedThisFrame) || (kb != null && kb.tabKey.wasPressedThisFrame))
        {
            if (IsLocked) SetTarget(null);
            else SetTarget(FindBest());
        }

        if (!IsLocked) return;

        if (health != null && health.IsDead)
        {
            SetTarget(null);
            return;
        }

        // 쓰러졌거나 너무 멀어지면 가장 가까운 적으로, 없으면 해제
        if (!targetDamageable.CanBeTargeted || Flat(target.position - transform.position).magnitude > breakRange)
        {
            SetTarget(FindNearest());
            return;
        }

        if (mouse != null) UpdateSwitchInput(mouse);
    }

    void UpdateSwitchInput(Mouse mouse)
    {
        float scroll = mouse.scroll.ReadValue().y;
        flick = Mathf.Lerp(flick, 0f, 1f - Mathf.Exp(-flickDecay * Time.unscaledDeltaTime)) + mouse.delta.ReadValue().x;

        int side = 0;
        if (switchByMouseFlick && Mathf.Abs(flick) >= flickThreshold) side = flick > 0f ? 1 : -1;
        else if (Mathf.Abs(scroll) > 0.01f) side = scroll > 0f ? 1 : -1;
        if (side == 0 || Time.unscaledTime - lastSwitch < switchCooldown) return;

        flick = 0f;
        Transform next = FindSide(side);
        if (next == null) return;
        lastSwitch = Time.unscaledTime;
        SetTarget(next);
    }

    void SetTarget(Transform t)
    {
        if (t == target) return;
        target = t;
        targetDamageable = t != null ? t.GetComponent<IDamageable>() : null;
        if (followCamera != null) followCamera.SetLockTarget(t);
        if (locomotion != null) locomotion.StrafeTarget = t;
        TargetChanged?.Invoke(t);
        if (logLockOn) Debug.Log(t != null ? $"[LockOn] {t.name}" : "[LockOn] 해제");
    }

    // ===== 대상 고르기 =====
    // 화면에 보이는 적 중 화면 가운데에 가깝고(주) 가까운(보조) 적
    Transform FindBest()
    {
        Transform best = null;
        float bestScore = float.MaxValue;
        foreach (Transform t in Candidates())
        {
            if (!OnScreen(t, out Vector3 vp)) continue;
            float score = new Vector2(vp.x - 0.5f, vp.y - 0.5f).magnitude + 0.5f * Flat(t.position - transform.position).magnitude / lockRange;
            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }

    Transform FindNearest()
    {
        Transform best = null;
        float bestDist = float.MaxValue;
        foreach (Transform t in Candidates())
        {
            float d = Flat(t.position - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = t;
            }
        }
        return best;
    }

    // 지금 대상 기준 화면 오른쪽(side 1)·왼쪽(-1)에서 가장 가까운 적
    Transform FindSide(int side)
    {
        if (!OnScreen(target, out Vector3 current)) return null;
        Transform best = null;
        float bestDx = float.MaxValue;
        foreach (Transform t in Candidates())
        {
            if (t == target || !OnScreen(t, out Vector3 vp)) continue;
            float dx = (vp.x - current.x) * side;
            if (dx <= 0f || dx >= bestDx) continue;
            bestDx = dx;
            best = t;
        }
        return best;
    }

    System.Collections.Generic.IEnumerable<Transform> Candidates()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, lockRange, buffer, ~0, QueryTriggerInteraction.Ignore);
        var seen = new System.Collections.Generic.HashSet<Transform>();
        for (int i = 0; i < count; i++)
        {
            if (buffer[i].transform.IsChildOf(transform)) continue;
            IDamageable d = buffer[i].GetComponentInParent<IDamageable>();
            if (d == null || !d.CanBeTargeted) continue;
            Transform t = ((Component)d).transform;
            if (seen.Add(t)) yield return t;
        }
    }

    bool OnScreen(Transform t, out Vector3 viewport)
    {
        viewport = cam != null ? cam.WorldToViewportPoint(t.position + Vector3.up) : new Vector3(0.5f, 0.5f, 1f);
        return viewport.z > 0f && viewport.x > -0.05f && viewport.x < 1.05f && viewport.y > -0.05f && viewport.y < 1.05f;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
