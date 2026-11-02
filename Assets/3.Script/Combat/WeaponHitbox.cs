using System;
using System.Collections.Generic;
using UnityEngine;

public class WeaponHitbox : MonoBehaviour
{
    [SerializeField] BoxCollider box;
    [SerializeField] Transform owner;
    [SerializeField] LayerMask hitMask = ~0;
    [SerializeField] string targetTag = "";

    readonly Collider[] overlapBuffer = new Collider[16];
    readonly HashSet<IDamageable> hitTargets = new HashSet<IDamageable>();

    DamageInfo pendingInfo;
    bool active;

    public event Action<IDamageable, DamageInfo, HitResult> OnHit;

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
    }

    public void EndSwing()
    {
        active = false;
    }

    // Animator가 포즈를 갱신한 뒤(LateUpdate)에 검사해야 이번 프레임의 칼 위치를 쓴다
    void LateUpdate()
    {
        if (!active) return;

        Transform t = box.transform;
        Vector3 center = t.TransformPoint(box.center);
        Vector3 halfExtents = Vector3.Scale(box.size, t.lossyScale) * 0.5f;

        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, t.rotation, hitMask, QueryTriggerInteraction.Collide);
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

            HitResult result = target.ReceiveDamage(info);
            OnHit?.Invoke(target, info, result);
        }
    }

    void OnDrawGizmos()
    {
        if (box == null) return;
        Gizmos.matrix = box.transform.localToWorldMatrix;
        Gizmos.color = active ? new Color(1f, 0.2f, 0.2f, 0.6f) : new Color(1f, 0.9f, 0.2f, 0.25f);
        Gizmos.DrawCube(box.center, box.size);
    }
}
