using UnityEngine;

// 플레이어를 뺀 "적의 시간"만 따로 늦춘다. Time.timeScale(스킬 차지·히트스톱)과는 별개로 곱해진다.
// 여러 슬로가 겹치면 그 순간 가장 느린 배속 하나만 적용하고, 각자 끝나는 시간에 풀린다.
public static class CombatTime
{
    const int MaxSlots = 4;

    static readonly float[] scales = new float[MaxSlots];
    static readonly float[] ends = new float[MaxSlots];
    static readonly float[] rampOuts = new float[MaxSlots];
    static float hold = 1f; // [변경] 끝 시간 없이 켜고 끄는 슬로 (발도 자세)

    public static float EnemyScale
    {
        get
        {
            float now = Time.unscaledTime;
            float result = hold; // [변경]

            for (int i = 0; i < MaxSlots; i++)
            {
                float remain = ends[i] - now;
                if (remain <= 0f) continue;

                // 끝나기 직전 rampOut 동안은 원래 속도로 서서히 복귀
                float scale = remain >= rampOuts[i]
                    ? scales[i]
                    : Mathf.Lerp(1f, scales[i], remain / Mathf.Max(rampOuts[i], 0.0001f));
                result = Mathf.Min(result, scale);
            }

            return result;
        }
    }

    public static float EnemyDeltaTime => Time.deltaTime * EnemyScale;

    // duration은 실제 시간(초)
    public static void SlowEnemies(float scale, float duration, float rampOutTime = 0.15f)
    {
        float now = Time.unscaledTime;
        int slot = 0;

        // 비어 있는 칸, 없으면 가장 먼저 끝나는 칸을 덮어씀
        for (int i = 0; i < MaxSlots; i++)
        {
            if (ends[i] <= now)
            {
                slot = i;
                break;
            }
            if (ends[i] < ends[slot]) slot = i;
        }

        scales[slot] = Mathf.Clamp01(scale);
        ends[slot] = now + duration;
        rampOuts[slot] = Mathf.Min(rampOutTime, duration);
    }

    // [변경] 해제할 때까지 유지되는 슬로. 다른 슬로와 겹치면 더 느린 쪽
    public static void SetHold(float scale) => hold = Mathf.Clamp01(scale);
    public static void ClearHold() => hold = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        for (int i = 0; i < MaxSlots; i++) ends[i] = 0f;
        hold = 1f; // [변경]
    }
}
