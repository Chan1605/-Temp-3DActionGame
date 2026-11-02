using UnityEngine;

// 소프트 타겟팅: 공격을 시작할 때 입력 방향 원뿔 안에서 가장 알맞은 적을 골라 몸을 돌리고,
// 멀면 끌어당기며 내딛게 해서 허공을 베지 않게 한다. (락온과 달리 카메라는 건드리지 않음)
public class PlayerTargeting : MonoBehaviour
{
    [SerializeField] bool softTargeting = true;
    [SerializeField, Range(10f, 180f)] float coneAngle = 60f;
    [SerializeField] float searchRadius = 4f;
    [SerializeField] float stopDistance = 1.3f;
    [SerializeField] float maxPull = 1.5f;

    // 방향 입력 없이 연타할 때는 직전 대상을 이 시간 동안 유지
    [SerializeField] float keepTargetTime = 1.5f;

    // 점수 = 각도 비중 + 거리 비중 (낮을수록 우선)
    [SerializeField, Range(0f, 1f)] float angleWeight = 0.6f;
    [SerializeField] bool logTarget;

    readonly Collider[] overlapBuffer = new Collider[32];

    Transform lastTarget;
    float lastTargetTime = float.NegativeInfinity;
    Vector3 debugDirection;

    public bool IsEnabled => softTargeting;
    public float StopDistance => stopDistance;
    public float MaxPull => maxPull;
    public Transform LastTarget => lastTarget;

    public bool TryFindTarget(Vector3 inputDirection, out Transform target, out float distance)
    {
        target = null;
        distance = 0f;
        if (!softTargeting) return false;

        bool hasInput = inputDirection.sqrMagnitude > 0.0001f;

        if (!hasInput && IsValid(lastTarget) && Time.time - lastTargetTime <= keepTargetTime)
        {
            float d = Flat(lastTarget.position - transform.position).magnitude;
            if (d <= searchRadius)
            {
                Remember(lastTarget, d, out target, out distance);
                return true;
            }
        }

        Vector3 reference = hasInput ? Flat(inputDirection).normalized : Flat(transform.forward).normalized;
        debugDirection = reference;

        int count = Physics.OverlapSphereNonAlloc(transform.position, searchRadius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        float halfAngle = coneAngle * 0.5f;
        float bestScore = float.MaxValue;
        Transform best = null;
        float bestDist = 0f;

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col.transform.IsChildOf(transform)) continue;

            IDamageable damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.CanBeTargeted) continue;

            Transform t = ((Component)damageable).transform;
            Vector3 to = Flat(t.position - transform.position);
            float d = to.magnitude;
            if (d < 0.01f || d > searchRadius) continue;

            float angle = Vector3.Angle(reference, to);
            if (angle > halfAngle) continue;

            float score = angleWeight * (angle / halfAngle) + (1f - angleWeight) * (d / searchRadius);
            if (score < bestScore)
            {
                bestScore = score;
                best = t;
                bestDist = d;
            }
        }

        if (best == null) return false;

        Remember(best, bestDist, out target, out distance);
        return true;
    }

    void Remember(Transform t, float d, out Transform target, out float distance)
    {
        lastTarget = t;
        lastTargetTime = Time.time;
        target = t;
        distance = d;
        if (logTarget) Debug.Log($"[Target] {t.name} ({d:F1}m)");
    }

    static bool IsValid(Transform t)
    {
        if (t == null) return false;
        IDamageable d = t.GetComponent<IDamageable>();
        return d != null && d.CanBeTargeted;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    void OnDrawGizmosSelected()
    {
        if (!softTargeting) return;

        Vector3 origin = transform.position + Vector3.up * 0.1f;
        Vector3 dir = debugDirection.sqrMagnitude > 0.0001f ? debugDirection : transform.forward;
        float half = coneAngle * 0.5f;

        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
        Gizmos.DrawLine(origin, origin + Quaternion.Euler(0f, -half, 0f) * dir * searchRadius);
        Gizmos.DrawLine(origin, origin + Quaternion.Euler(0f, half, 0f) * dir * searchRadius);

        if (lastTarget != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, lastTarget.position + Vector3.up * 0.1f);
        }
    }
}
