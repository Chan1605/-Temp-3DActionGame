using System.Collections;
using UnityEngine;

// 1칸 · 뇌운낙뢰진(雷雲落雷陣): 칼을 땅에 꽂으면 하늘이 어두워지고, 주변에 낙뢰가 빗발침 (적이 있는 곳 위주) → 마지막에 둘레를 한꺼번에
public partial class PlayerUltimate
{
    [Header("1 · 뇌운낙뢰진")]
    [SerializeField] float stormRadius = 11f;
    [SerializeField] float stormDuration = 3.2f;
    [SerializeField] float stormInterval = 0.11f;
    [SerializeField] float stormStrikeRadius = 2.4f;
    [SerializeField] float stormDamage = 22f;
    [SerializeField] float stormPosture = 18f;
    [SerializeField, Range(0f, 1f)] float stormAimChance = 0.7f; // 이 확률로 적 위치에 떨어뜨림
    [SerializeField] float stormFinalDamage = 45f;
    [SerializeField, Range(0.3f, 1f)] float stormPoseHoldAt = 0.85f; // [변경] 칼 박는 모션을 이 지점에서 멈춰 끝까지 유지 (반복 · 대기 자세로 빠지지 않게)

    IEnumerator Storm()
    {
        fx.Letterbox(true);
        fx.DarkenSky(1f, new Color(0.1f, 0.11f, 0.18f), 0.03f);
        fx.ShowTitle(Names[1], Readings[1], 1.6f);
        string pose = animator != null && animator.HasState(0, Animator.StringToHash("Slam_End")) ? "Slam_End" : ChargeState; // 칼을 땅에 박는 자세
        Play(pose);
        StartCoroutine(HoldPose(pose, stormPoseHoldAt)); // [변경] 한 번만 재생하고 멈춤 (끝나면 Run이 속도를 되돌림)
        Sound(clashClip, 0.8f, 0.8f);
        if (followCamera != null) followCamera.BeginCinematic(Chest, Yaw + 35f, 28f, 11f); // 위에서 넓게 내려다봄

        yield return new WaitForSecondsRealtime(0.5f);
        Vector3 blade = transform.position + transform.forward * 0.8f;
        SkyStrike(blade, 22f, 3, 3f);
        Thunder();
        fx.Flash(Color.white, 0.6f);
        if (followCamera != null) followCamera.Shake(0.3f, 0.3f);

        // 핵심: 정해진 시간 동안 일정 간격으로 낙뢰. 적 위치를 우선 노리고 나머지는 주변 아무 데나
        float t = 0f, next = 0f;
        while (t < stormDuration)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            next -= dt;
            if (next <= 0f)
            {
                next = stormInterval;
                Vector3 p = StormPoint();
                SkyStrike(p, Random.Range(14f, 20f));
                Thunder(0.5f);
                HitArea(p, stormStrikeRadius, stormDamage, stormPosture, 0.6f);
                if (followCamera != null) followCamera.Shake(0.08f, 0.1f);
            }
            yield return null;
        }

        // 마무리: 둘레 여섯 곳 동시에 + 가운데 큰 한 방
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            SkyStrike(transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4f, 18f, 1, 2f);
        }
        SkyStrike(transform.position, 24f, 4, 5f); // 마무리는 여러 줄기가 한 점으로
        HitArea(transform.position, 7f, stormFinalDamage, stormFinalDamage, 0.6f);
        Thunder(1f);
        fx.Flash(new Color(0.75f, 0.85f, 1f), 0.9f);
        if (followCamera != null) followCamera.Shake(0.45f, 0.4f);
        yield return new WaitForSecondsRealtime(0.7f);
    }

    // [변경] 상태가 그 지점까지 재생되면 애니메이터를 멈춤 → 반복 재생 · 나가는 전환 없이 자세 유지
    IEnumerator HoldPose(string state, float at)
    {
        if (animator == null || string.IsNullOrEmpty(state)) yield break;
        int hash = Animator.StringToHash(state);
        yield return null; // CrossFade가 반영될 때까지
        while (Active)
        {
            AnimatorStateInfo info = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash != hash) yield break;
            if (info.normalizedTime >= at)
            {
                animator.speed = 0f;
                yield break;
            }
            yield return null;
        }
    }

    Vector3 StormPoint()
    {
        if (Random.value < stormAimChance)
        {
            var enemies = EnemySwordsman.Alive;
            int start = Random.Range(0, Mathf.Max(1, enemies.Count));
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemySwordsman e = enemies[(start + i) % enemies.Count];
                if (e == null || !e.CanBeTargeted) continue;
                if (Flat(e.transform.position - transform.position).sqrMagnitude > stormRadius * stormRadius) continue;
                Vector2 jitter = Random.insideUnitCircle * 0.8f;
                return e.transform.position + new Vector3(jitter.x, 0f, jitter.y);
            }
        }
        Vector2 r = Random.insideUnitCircle * stormRadius;
        Vector3 p = transform.position + new Vector3(r.x, 0f, r.y);
        if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
        return p;
    }
}
