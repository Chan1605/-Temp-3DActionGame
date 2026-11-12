using System.Collections;
using UnityEngine;

// 3칸 · 뇌신강림(雷神降臨): 하늘이 검게 물들고, 치켜든 칼에 낙뢰가 연달아 내리꽂힘 → 주변 모든 적을 표식해
//  벽력일섬 발도(PlayerSkill)로 차례차례 베고 → 납도하는 순간 일괄 낙뢰 (보스에게도 크게)
public partial class PlayerUltimate
{
    [Header("3 · 뇌신강림")]
    [SerializeField] float descentRange = 16f;
    [SerializeField] int descentStrikes = 5;
    [SerializeField] float descentStrikeGap = 0.2f;

    IEnumerator Descent()
    {
        fx.Letterbox(true);
        fx.DarkenSky(1f, new Color(0.05f, 0.04f, 0.1f), 0.045f);
        CombatTime.SetHold(0f);
        Play(ChargeState);
        Sound(chargeClip, 1f, 0.8f);
        if (followCamera != null) followCamera.BeginCinematic(transform.position + Vector3.up * 1.6f, Yaw + 180f, 2f, 5f); // 정면 아래에서 올려다봄

        yield return new WaitForSecondsRealtime(0.35f);
        fx.ShowTitle(Names[3], Readings[3], 1.8f);

        // 핵심: 하늘에서 칼끝으로 낙뢰가 연달아 (점점 세게)
        Vector3 tip = Chest + Vector3.up * 1.1f;
        for (int i = 0; i < descentStrikes; i++)
        {
            if (skyBolt != null) skyBolt.Trigger(tip + new Vector3(Random.Range(-3f, 3f), 30f, Random.Range(-3f, 3f)), tip);
            for (int k = 0; k < 4; k++)
            {
                Vector2 a = Random.insideUnitCircle.normalized * Random.Range(1f, 3f);
                Arc(tip, transform.position + new Vector3(a.x, 0.05f, a.y));
            }
            Thunder(0.6f + 0.1f * i);
            fx.Flash(Color.white, 0.5f + 0.1f * i);
            if (followCamera != null) followCamera.Shake(0.12f + 0.05f * i, 0.15f);
            yield return new WaitForSecondsRealtime(descentStrikeGap);
        }
        yield return new WaitForSecondsRealtime(0.25f);

        // 발도로 넘김: 표식 · 연쇄 돌진 · 납도 낙뢰는 PlayerSkill이 그대로 (뇌신강림 배율)
        CombatTime.ClearHold();
        if (followCamera != null) followCamera.EndCinematic();
        fx.Letterbox(false);
        bool drew = skill != null && skill.BeginUltimateDraw(descentRange);
        if (drew)
        {
            while (skill.IsActive) yield return null;
        }
        else
        {
            // 주변에 적이 없으면 둘레 낙뢰로 마무리
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                SkyStrike(transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 5f);
            }
            Thunder(1f);
            fx.Flash(new Color(0.75f, 0.85f, 1f), 0.9f);
            if (followCamera != null) followCamera.Shake(0.4f, 0.35f);
        }
        yield return new WaitForSecondsRealtime(0.4f);
    }
}
