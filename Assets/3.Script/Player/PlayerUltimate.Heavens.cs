using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 2칸 · 천뢰일섬(天雷一閃): 세상이 멈추고 흑백 → 카메라가 돌며 칼에 번개가 모임 → 단 한 번, 정면을 길게 가름
//  → 옆에서 본 정지 화면이 실선으로 베임 → [변경] 일섬처럼 납도, 칼이 들어가는 순간 두 조각으로 어긋나며(UltimateFx.Cut) 선 위의 모든 적에게 낙뢰
//  [변경] 베기 직전엔 소리를 눌러 정적 → 베는 순간 소리가 돌아오며 칼 소리 + 낮은 천둥
public partial class PlayerUltimate
{
    [Header("2 · 천뢰일섬")]
    [SerializeField] float heavensLength = 14f;
    [SerializeField] float heavensWidth = 3f;
    [SerializeField] float heavensGather = 1.3f;  // 기 모으는 시간 (카메라가 도는 동안)
    [SerializeField] float heavensHold = 0.32f;   // 선이 그어진 뒤 최소 정지 (그 뒤 칼이 들어가는 순간 어긋남)
    [SerializeField] float heavensSheatheSpeed = 0.8f;
    [SerializeField] float heavensSheatheAt = 0.5f; // 납도 모션의 이 지점 = 칼이 들어감
    [SerializeField] float heavensCutAngle = -7f;
    [SerializeField] float heavensSilence = 0.35f; // 베기 직전 정적 길이
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
                fx.ShowTitle(Names[2], Readings[2], 1.0f);
            }
            if (t >= heavensGather - heavensSilence) Duck(0.05f); // 숨을 멈추는 정적
            yield return null;
        }

        // 일섬: 정면으로 장애물 앞까지 순간 이동, 지나간 띠 안의 적을 기억해 둠 (피해는 색이 돌아올 때)
        Vector3 start = transform.position;
        Vector3 dir = Flat(transform.forward).normalized;
        float length = ClearLength(start, dir, heavensLength);
        Vector3 end = start + dir * length;
        List<EnemySwordsman> targets = InStrip(start, end, heavensWidth);

        if (afterimage != null) afterimage.Spawn();
        Teleport(end);
        // [변경] 베고 난 자리에서 바로 납도 (벽력일섬 마무리와 같은 모션). 칼이 들어가는 순간을 화면이 갈라지는 신호로 씀
        bool sheathing = stance != null && stance.SheatheNow(0.05f, true);
        if (sheathing && animator != null) animator.speed = heavensSheatheSpeed;
        else Play(ReleaseState, 0.02f);
        Duck(1f);
        Sound(slashClip, 1f, 0.85f);
        if (thunderClips != null && thunderClips.Length > 0) Sound(thunderClips[0], 0.7f, 0.55f); // 낮게 깔리는 천둥
        Arc(start + Vector3.up * 1.1f, end + Vector3.up * 1.1f);
        if (followCamera != null) followCamera.BeginCinematic((start + end) * 0.5f + Vector3.up * 1.2f, Yaw + 90f, 8f, length * 0.75f + 4f); // 옆에서 본 정지 화면
        yield return new WaitForSecondsRealtime(0.18f); // 카메라가 자리를 잡은 화면을 찍어야 함

        // 화면 베기: 조각이 어긋나는 순간 색 · 시간이 돌아오고 선 위의 적에게 한꺼번에
        bool split = false;
        fx.Cut(heavensCutAngle, heavensHold, () => split = true,
            () => !sheathing || stance.SheatheProgress < 0f || stance.SheatheProgress >= heavensSheatheAt); // [변경] 칼이 들어가는 순간
        while (!split) yield return null;
        if (animator != null) animator.speed = 1f;
        if (sheathing) Sound(clashClip, 0.8f, 1.6f); // 찰칵

        fx.Desaturate(0f);
        CombatTime.ClearHold();
        foreach (EnemySwordsman e in targets)
        {
            if (e == null || !e.CanBeTargeted) continue;
            SkyStrike(e.transform.position, 14f, 3, 2.6f);
            Hit(e, heavensDamage, heavensPosture, heavensBossScale, true);
        }
        Thunder(1f);
        if (followCamera != null) followCamera.Shake(0.5f, 0.45f);
        yield return new WaitForSecondsRealtime(0.45f);
        fx.Flash(new Color(0.7f, 0.82f, 1f), 0.35f);
        if (followCamera != null) followCamera.EndCinematic();
        yield return new WaitForSecondsRealtime(0.5f);
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
        // 도착 지점의 땅 높이에 맞춤 (적 · 자기 몸은 무시) → 경사에서 묻히거나 뜨지 않게
        float best = float.MaxValue;
        foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<IDamageable>() != null) continue;
            if (hit.distance >= best) continue;
            best = hit.distance;
            position.y = hit.point.y;
        }
        transform.position = position;
        if (cc != null) cc.enabled = true;
    }
}
