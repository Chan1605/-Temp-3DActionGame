using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 2칸 · 천뢰일섬(天雷一閃): 세상이 멈추고 흑백 → 카메라가 돌며 칼에 번개가 모임 → 단 한 번, 정면을 길게 가름
//  → 옆에서 본 정지 화면에 흰 선이 그어지고 → 색이 돌아오는 순간 선 위의 모든 적에게 낙뢰
public partial class PlayerUltimate
{
    [Header("2 · 천뢰일섬")]
    [SerializeField] float heavensLength = 14f;
    [SerializeField] float heavensWidth = 3f;
    [SerializeField] float heavensGather = 1.3f;  // 기 모으는 시간 (카메라가 도는 동안)
    [SerializeField] float heavensHold = 0.55f;   // 베고 난 뒤 정지 화면
    [SerializeField] float heavensDamage = 260f;
    [SerializeField] float heavensPosture = 120f;
    [SerializeField] float heavensBossScale = 0.5f;

    IEnumerator Heavens()
    {
        fx.Letterbox(true);
        fx.Desaturate(1f);
        CombatTime.SetHold(0f);
        Play(ChargeState);
        Sound(chargeClip, 0.9f);

        // 기 모으기: 앞-오른쪽에서 뒤-왼쪽으로 가까이 돌며 칼로 번개가 끌려옴
        float arcTimer = 0f;
        bool titled = false;
        for (float t = 0f; t < heavensGather; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / heavensGather);
            if (followCamera != null) followCamera.BeginCinematic(Chest, Yaw + Mathf.Lerp(140f, -60f, k), Mathf.Lerp(4f, 12f, k), Mathf.Lerp(3.2f, 4.5f, k));
            arcTimer -= Time.unscaledDeltaTime;
            if (arcTimer <= 0f)
            {
                arcTimer = Mathf.Lerp(0.08f, 0.025f, k);
                Vector2 a = Random.insideUnitCircle.normalized * Random.Range(1.5f, 3f);
                Arc(transform.position + new Vector3(a.x, Random.Range(0.1f, 2.5f), a.y), Chest + transform.forward * 0.5f);
            }
            if (!titled && t >= heavensGather * 0.35f)
            {
                titled = true;
                fx.ShowTitle(Names[2], Readings[2], 1.4f);
            }
            yield return null;
        }

        // 일섬: 정면으로 장애물 앞까지 순간 이동, 지나간 띠 안의 적을 기억해 둠 (피해는 색이 돌아올 때)
        Vector3 start = transform.position;
        Vector3 dir = Flat(transform.forward).normalized;
        float length = ClearLength(start, dir, heavensLength);
        Vector3 end = start + dir * length;
        List<EnemySwordsman> targets = InStrip(start, end, heavensWidth);

        fx.Flash(Color.white, 1f);
        Teleport(end);
        Play(ReleaseState, 0.02f);
        Sound(slashClip, 1f, 0.9f);
        fx.Slash(-6f);
        if (followCamera != null) followCamera.BeginCinematic((start + end) * 0.5f + Vector3.up * 1.2f, Yaw + 90f, 8f, length * 0.75f + 4f); // 옆에서 본 정지 화면
        yield return new WaitForSecondsRealtime(heavensHold);

        // 갈라짐: 색 · 시간이 돌아오며 한꺼번에
        fx.Desaturate(0f);
        CombatTime.ClearHold();
        foreach (EnemySwordsman e in targets)
        {
            if (e == null || !e.CanBeTargeted) continue;
            SkyStrike(e.transform.position, 14f);
            Hit(e, heavensDamage, heavensPosture, heavensBossScale, true);
        }
        Thunder(1f);
        fx.Flash(new Color(0.7f, 0.82f, 1f), 0.8f);
        if (followCamera != null)
        {
            followCamera.Shake(0.5f, 0.45f);
            followCamera.EndCinematic();
        }
        yield return new WaitForSecondsRealtime(0.6f);
    }

    // 벽 · 지형 앞에서 멈춤 (적 · 자기 몸은 통과)
    float ClearLength(Vector3 start, Vector3 dir, float max)
    {
        Vector3 origin = start + Vector3.up * 1f;
        float best = max;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, dir, max, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;
            best = Mathf.Min(best, hit.distance - 1f);
        }
        return Mathf.Max(0f, best);
    }

    List<EnemySwordsman> InStrip(Vector3 start, Vector3 end, float halfWidth)
    {
        var list = new List<EnemySwordsman>();
        Vector3 seg = Flat(end - start);
        float len = seg.magnitude;
        Vector3 dir = len > 0.001f ? seg / len : transform.forward;
        foreach (EnemySwordsman e in EnemySwordsman.Alive)
        {
            if (e == null || !e.CanBeTargeted) continue;
            Vector3 to = Flat(e.transform.position - start);
            float along = Vector3.Dot(to, dir);
            if (along < -1f || along > len + 1.5f) continue;
            if ((to - dir * along).magnitude <= halfWidth) list.Add(e);
        }
        return list;
    }

    void Teleport(Vector3 position)
    {
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.position = position;
        if (cc != null) cc.enabled = true;
    }
}
