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

    IEnumerator Storm()
    {
        fx.Letterbox(true);
        fx.DarkenSky(1f, new Color(0.1f, 0.11f, 0.18f), 0.03f);
        fx.ShowTitle(Names[1], Readings[1], 1.6f);
        if (animator != null && animator.HasState(0, Animator.StringToHash("Slam_End"))) Play("Slam_End"); // 칼을 땅에 박는 자세
        else Play(ChargeState);
        Sound(clashClip, 0.8f, 0.8f);
        if (followCamera != null) followCamera.BeginCinematic(Chest, Yaw + 35f, 28f, 11f); // 위에서 넓게 내려다봄

        yield return new WaitForSecondsRealtime(0.5f);
        Vector3 blade = transform.position + transform.forward * 0.8f;
        SkyStrike(blade, 22f);
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
                SkyStrike(p);
                Thunder(0.55f);
                HitArea(p, stormStrikeRadius, stormDamage, stormPosture, 0.6f);
                if (followCamera != null) followCamera.Shake(0.08f, 0.1f);
            }
            yield return null;
        }

        // 마무리: 둘레 여섯 곳 동시에 + 가운데 큰 한 방
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            SkyStrike(transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4f);
        }
        SkyStrike(transform.position, 24f);
        HitArea(transform.position, 7f, stormFinalDamage, stormFinalDamage, 0.6f);
        Thunder(1f);
        fx.Flash(new Color(0.75f, 0.85f, 1f), 0.9f);
        if (followCamera != null) followCamera.Shake(0.45f, 0.4f);
        yield return new WaitForSecondsRealtime(0.7f);
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
