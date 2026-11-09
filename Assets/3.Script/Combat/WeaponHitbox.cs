using System;
using System.Collections.Generic;
using UnityEngine;

public class WeaponHitbox : MonoBehaviour
{
    [SerializeField] BoxCollider box;
    [SerializeField] Transform owner;
    [SerializeField] LayerMask hitMask = ~0;
    [SerializeField] string targetTag = "";

    // [변경] 빠른 휘두르기가 프레임 사이에서 몸을 건너뛰지 않게, 이전 프레임 → 이번 프레임 사이를 나눠서 검사
    [Header("Sweep")]
    [SerializeField] bool sweep = true;
    [SerializeField] float sweepStep = 0.2f;  // 칼 끝이 이 거리(m)만큼 움직일 때마다 한 번 검사
    [SerializeField] int maxSubsteps = 10;

    readonly Collider[] overlapBuffer = new Collider[16];
    readonly HashSet<IDamageable> hitTargets = new HashSet<IDamageable>();

    DamageInfo pendingInfo;
    bool active;
    Vector3 lastCenter; // [변경] 직전 검사 때의 칼 위치·회전
    Quaternion lastRotation;
    bool hasLastPose;

    public event Action<IDamageable, DamageInfo, HitResult> OnHit;
    // 대상마다 피해를 한 번 더 가공 (PlayerStats: 공격력 배율 · 치명타). 비어 있으면 그대로
    public Func<DamageInfo, DamageInfo> ModifyDamage { get; set; }
    public int HitCount => hitTargets.Count; // [변경] 이번 휘두르기에 맞힌 대상 수 (빗나감 로그용)

    void Awake()
    {
        if (box == null) box = GetComponent<BoxCollider>();
        if (owner == null) owner = transform.root;
        box.isTrigger = true;
    }

    public void BeginSwing(DamageInfo info)
    {
        pendingInfo = info;
        hitTargets.Clear();
        active = true;

        // [변경] 판정이 켜지는 시점(Update)의 칼 위치 = 직전 프레임 포즈 → 첫 검사부터 그 사이를 훑음
        GetPose(out lastCenter, out lastRotation);
        hasLastPose = true;
    }

    public void EndSwing()
    {
        active = false;
        hasLastPose = false; // [변경]
    }

    // Animator가 포즈를 갱신한 뒤(LateUpdate)에 검사해야 이번 프레임의 칼 위치를 쓴다
    void LateUpdate()
    {
        if (!active) return;

        GetPose(out Vector3 center, out Quaternion rotation);
        Vector3 halfExtents = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;

        // [변경] 칼 양 끝이 이번 프레임에 움직인 거리만큼 나눠, 사이 포즈를 보간해 검사
        int steps = 1;
        if (sweep && hasLastPose)
        {
            float moved = MaxEndDistance(lastCenter, lastRotation, center, rotation, halfExtents);
            steps = Mathf.Clamp(Mathf.CeilToInt(moved / Mathf.Max(sweepStep, 0.01f)), 1, Mathf.Max(maxSubsteps, 1));
        }

        for (int s = 1; s <= steps; s++)
        {
            float k = (float)s / steps;
            CheckAt(Vector3.Lerp(lastCenter, center, k), halfExtents, Quaternion.Slerp(lastRotation, rotation, k));
        }

        lastCenter = center;
        lastRotation = rotation;
        hasLastPose = true;
    }

    // [변경] 기존 검사 본문을 위치·회전을 받는 형태로 분리 (판정 규칙은 그대로)
    void CheckAt(Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, rotation, hitMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col.transform.IsChildOf(owner)) continue;

            // 상대 칼의 판정 박스에 닿은 것은 몸에 맞은 것으로 치지 않음
            if (col.isTrigger && col.TryGetComponent(out WeaponHitbox _)) continue;

            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null) continue;

            // 태그를 지정하면 그 태그의 대상만 때림 (적끼리 서로 베지 않게)
            if (!string.IsNullOrEmpty(targetTag) && !((Component)target).CompareTag(targetTag)) continue;
            if (!hitTargets.Add(target)) continue;

            DamageInfo info = pendingInfo;
            info.HitPoint = col.ClosestPoint(center);
            info.Direction = Vector3.ProjectOnPlane(col.transform.position - owner.position, Vector3.up).normalized;
            if (ModifyDamage != null) info = ModifyDamage(info);

            HitResult result = target.ReceiveDamage(info);
            OnHit?.Invoke(target, info, result);
        }
    }

    void GetPose(out Vector3 center, out Quaternion rotation)
    {
        Transform t = box.transform;
        center = t.TransformPoint(box.center);
        rotation = t.rotation;
    }

    // 박스의 가장 긴 축 양 끝(칼끝·손잡이 쪽)이 움직인 거리 중 큰 값
    static float MaxEndDistance(Vector3 c0, Quaternion r0, Vector3 c1, Quaternion r1, Vector3 half)
    {
        Vector3 axis = half.x >= half.y && half.x >= half.z ? new Vector3(half.x, 0f, 0f)
                     : half.y >= half.z ? new Vector3(0f, half.y, 0f) : new Vector3(0f, 0f, half.z);
        float a = ((c1 + r1 * axis) - (c0 + r0 * axis)).magnitude;
        float b = ((c1 - r1 * axis) - (c0 - r0 * axis)).magnitude;
        return Mathf.Max(a, b);
    }

    // 칼 판정 밖에서 직접 때릴 때 (내려찍기 충격파) — 같은 배율·치명타·OnHit 구독자(타격감·뇌인)를 그대로 거침
    public HitResult HitDirect(IDamageable target, DamageInfo info)
    {
        if (ModifyDamage != null) info = ModifyDamage(info);
        HitResult result = target.ReceiveDamage(info);
        OnHit?.Invoke(target, info, result);
        return result;
    }

    void OnDrawGizmos()
    {
        if (box == null) return;
        Gizmos.matrix = box.transform.localToWorldMatrix;
        Gizmos.color = active ? new Color(1f, 0.2f, 0.2f, 0.6f) : new Color(1f, 0.9f, 0.2f, 0.25f);
        Gizmos.DrawCube(box.center, box.size);
    }
}
