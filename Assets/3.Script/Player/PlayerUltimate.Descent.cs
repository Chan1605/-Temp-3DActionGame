using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 3칸 · 뇌신강림(雷神降臨)
// 벽력일섬 발도를 빌려 쓰던 흐름을 자체 연출로 바꿈 (R→Q 발도와 같은 느낌이라)
//  ① 하늘이 검게 물들고, 치켜든 칼에 낙뢰가 연달아 내리꽂힘
//  ② 세상이 완전히 멈춘 채, 돌진 없이 깜빡이듯 주변 모든 적을 벰 — 잔상과 번개 줄이 지나간 길로 남고, 벤 자리마다 빛나는 칼자국
//  ③ 처음 자리로 돌아와 천천히 납도 (정적) → 칼이 들어가는 순간 칼자국이 한꺼번에 터지며 적마다 낙뢰 기둥
public partial class PlayerUltimate
{
    [Header("3 · 뇌신강림")]
    [SerializeField] float descentRange = 16f;
    [SerializeField] int descentStrikes = 5;
    [SerializeField] float descentStrikeGap = 0.2f;
    [SerializeField] Vector2 descentBlinkGap = new Vector2(0.13f, 0.05f); // 첫 베기 → 마지막 베기 간격 (점점 빨라짐)
    [SerializeField] float descentBehind = 1.6f;     // 벤 뒤 적을 지나쳐 서는 거리
    [SerializeField] float descentSheatheSpeed = 0.55f;
    [SerializeField] float descentSheatheAt = 0.5f;  // 납도 모션의 이 지점 = 칼이 들어감
    [SerializeField] float descentDamage = 320f;
    [SerializeField] float descentPosture = 160f;
    [SerializeField] float descentBossScale = 0.9f;

    readonly List<LineRenderer> scars = new List<LineRenderer>();
    static Material scarMaterial;

    IEnumerator Descent()
    {
        Vector3 home = transform.position;
        Quaternion homeRot = transform.rotation;

        // ① 강림
        fx.Letterbox(true);
        fx.DarkenSky(1f, new Color(0.09f, 0.08f, 0.16f), 0.03f); // [변경] 덜 어둡고 덜 짙게 (화질 깨짐)
        CombatTime.SetHold(0f);
        Play(ChargeState);
        Sound(chargeClip, 1f, 0.8f);
        if (followCamera != null) followCamera.BeginCinematic(home + Vector3.up * 1.6f, Yaw + 180f, 2f, 5f); // 정면 아래에서 올려다봄
        yield return new WaitForSecondsRealtime(0.35f);
        fx.ShowTitle(Names[3], Readings[3], 1.6f);

        Vector3 tip = Chest + Vector3.up * 1.1f;
        for (int i = 0; i < descentStrikes; i++)
        {
            for (int n = 0; n < 2; n++) // [변경] 더 비스듬히 · 가늘게 (곧은 기둥처럼 보이지 않게)
                ThunderBolt3D.Strike(tip + new Vector3(Random.Range(-9f, 9f), 24f, Random.Range(-9f, 9f)), tip, n == 0 ? 0.85f : 0.55f, 2);
            for (int k = 0; k < 4; k++)
            {
                Vector2 a = Random.insideUnitCircle.normalized * Random.Range(1f, 3f);
                Arc(tip, home + new Vector3(a.x, 0.05f, a.y));
            }
            Thunder(0.45f + 0.08f * i);
            fx.Flash(Color.white, 0.4f + 0.08f * i);
            if (followCamera != null) followCamera.Shake(0.1f + 0.04f * i, 0.15f);
            yield return new WaitForSecondsRealtime(descentStrikeGap);
        }
        yield return new WaitForSecondsRealtime(0.2f);

        // ② 멈춘 세상에서 순간 베기
        List<EnemySwordsman> targets = DescentTargets(home);
        if (targets.Count == 0)
        {
            // 주변에 적이 없으면 둘레 낙뢰로 마무리
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                SkyStrike(home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 5f);
            }
            Thunder(1f);
            fx.Flash(new Color(0.75f, 0.85f, 1f), 0.9f);
            if (followCamera != null) followCamera.Shake(0.4f, 0.35f);
            CombatTime.ClearHold();
            yield return new WaitForSecondsRealtime(0.6f);
            yield break;
        }

        fx.Desaturate(0.75f);
        Vector3 center = home;
        float spread = 0f;
        foreach (EnemySwordsman e in targets) center += e.transform.position;
        center /= targets.Count + 1;
        foreach (EnemySwordsman e in targets) spread = Mathf.Max(spread, Flat(e.transform.position - center).magnitude);
        if (followCamera != null) followCamera.BeginCinematic(center + Vector3.up, Yaw + 25f, 40f, Mathf.Clamp(spread * 1.7f + 9f, 11f, 28f)); // 전부 보이게 위에서
        yield return new WaitForSecondsRealtime(0.25f);

        Play(ReleaseState, 0.02f);
        for (int i = 0; i < targets.Count; i++)
        {
            EnemySwordsman e = targets[i];
            if (e == null) continue;
            Vector3 from = transform.position;
            Vector3 dir = Flat(e.transform.position - from);
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
            Vector3 to = e.transform.position + dir * descentBehind;
            to.y = from.y;

            // 핵심: 이동이 아니라 깜빡임 — 떠난 자리에 잔상, 지나간 길에 번개 줄, 적 몸에 칼자국
            if (afterimage != null) afterimage.Spawn();
            Teleport(to);
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            Arc(from + Vector3.up * 1.1f, to + Vector3.up * 1.1f);
            AddScar(e.transform.position + Vector3.up * 1.15f, dir);
            float k = targets.Count > 1 ? i / (float)(targets.Count - 1) : 1f;
            Sound(slashClip, 0.7f, Mathf.Lerp(0.95f, 1.5f, k)); // 뒤로 갈수록 높고 빠르게
            if (followCamera != null) followCamera.Shake(0.06f, 0.06f);
            yield return new WaitForSecondsRealtime(Mathf.Lerp(descentBlinkGap.x, descentBlinkGap.y, k));
        }

        // ③ 납도: 처음 자리로 돌아와 가까이에서 천천히, 소리는 정적
        if (afterimage != null) afterimage.Spawn();
        Arc(transform.position + Vector3.up * 1.1f, home + Vector3.up * 1.1f);
        Teleport(home);
        transform.rotation = homeRot;
        Duck(0.05f);
        if (followCamera != null) followCamera.BeginCinematic(home + Vector3.up * 1.3f, Yaw + 150f, 6f, 3.4f);
        bool sheathing = stance != null && stance.SheatheNow(0.1f, true);
        if (sheathing && animator != null) animator.speed = descentSheatheSpeed;
        if (!sheathing) Play(ChargeState, 0.15f);
        for (float t = 0f; t < 2.2f; t += Time.unscaledDeltaTime)
        {
            if (!sheathing && t >= 0.7f) break;
            if (sheathing && t > 0.15f)
            {
                float p = stance.SheatheProgress;
                if (p < 0f || p >= descentSheatheAt) break; // 칼이 들어감 (또는 모션이 끝남)
            }
            yield return null;
        }
        if (animator != null) animator.speed = 1f;

        // 찰칵 → 한꺼번에
        Duck(1f);
        Sound(clashClip, 0.9f, 1.6f);
        yield return new WaitForSecondsRealtime(0.12f);
        fx.Desaturate(0f);
        CombatTime.ClearHold();
        if (followCamera != null) followCamera.BeginCinematic(center + Vector3.up, Yaw + 25f, 34f, Mathf.Clamp(spread * 1.7f + 11f, 13f, 30f));
        StartCoroutine(BurstScars());
        foreach (EnemySwordsman e in targets)
        {
            if (e == null || !e.CanBeTargeted) continue;
            SkyStrike(e.transform.position, 22f, 2, 3.2f); // [변경] 4 → 2줄기 (겹친 굵은 막대처럼 보이던 문제)
            Hit(e, descentDamage, descentPosture, descentBossScale, true);
        }
        Thunder(1f);
        if (thunderClips != null && thunderClips.Length > 0) Sound(thunderClips[0], 0.9f, 0.5f);
        fx.Flash(Color.white, 1f);
        if (followCamera != null) followCamera.Shake(0.6f, 0.5f);
        yield return new WaitForSecondsRealtime(0.9f);
    }

    // 가까운 적부터, 그다음은 방금 벤 적에서 가장 가까운 적 순 → 지나간 길이 지그재그로 이어짐
    List<EnemySwordsman> DescentTargets(Vector3 origin)
    {
        var pool = new List<EnemySwordsman>();
        foreach (EnemySwordsman e in EnemySwordsman.Alive)
            if (e != null && e.CanBeTargeted && Flat(e.transform.position - origin).sqrMagnitude <= descentRange * descentRange) pool.Add(e);

        var ordered = new List<EnemySwordsman>();
        Vector3 at = origin;
        while (pool.Count > 0)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            {
                float d = Flat(pool[i].transform.position - at).sqrMagnitude;
                if (d >= bestDist) continue;
                bestDist = d;
                best = i;
            }
            at = pool[best].transform.position;
            ordered.Add(pool[best]);
            pool.RemoveAt(best);
        }
        return ordered;
    }

    // 벤 방향으로 비스듬히 남는 가는 빛줄 (양 끝이 뾰족)
    void AddScar(Vector3 center, Vector3 dir)
    {
        if (scarMaterial == null) scarMaterial = new Material(Shader.Find("Sprites/Default"));
        var line = new GameObject("UltimateScar").AddComponent<LineRenderer>();
        line.sharedMaterial = scarMaterial;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 3;
        Vector3 tilt = (dir + Vector3.up * Random.Range(-0.45f, 0.45f)).normalized;
        line.SetPosition(0, center - tilt * 1.7f);
        line.SetPosition(1, center);
        line.SetPosition(2, center + tilt * 1.7f);
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
        line.widthMultiplier = 0.05f;
        line.startColor = line.endColor = new Color(0.85f, 0.92f, 1f, 1f);
        scars.Add(line);
    }

    IEnumerator BurstScars()
    {
        for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
        {
            float k = t / 0.3f;
            foreach (LineRenderer line in scars)
            {
                if (line == null) continue;
                line.widthMultiplier = Mathf.Lerp(0.05f, 0.5f, k);
                line.startColor = line.endColor = new Color(0.85f, 0.92f, 1f, 1f - k);
            }
            yield return null;
        }
        ClearScars();
    }

    void ClearScars()
    {
        foreach (LineRenderer line in scars)
            if (line != null) Destroy(line.gameObject);
        scars.Clear();
    }
}
