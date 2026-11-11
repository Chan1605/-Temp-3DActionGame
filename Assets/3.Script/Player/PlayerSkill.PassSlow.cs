using UnityEngine;

// 일섬 "지나가며 베는 순간" 정지 (타격감)
// 슬로모션 → 점찍기: 대상 옆을 지나는 순간 화면 전체를 거의 멈춘 정지 컷으로 짧게 잡고, 풀리면서 다음 대상으로 튕겨 나감
//  - 정지 동안 돌진 진행 0 · 세상 timeScale 거의 0 · 카메라도 흔들지 않음(정지 컷이 또렷하게) → 풀리는 순간 짧게 흔들림
//  - 마지막 대상은 조금 더 길게 + 카메라 당김 → 바로 이어지는 납도·낙뢰로 연결
//  - 진입 방식별 세기: 홀드 발도 1 · 각성 발도 0.7 · 회피 직후 0.6 · 탭 Q 0
//  - 한 번의 일섬에서 늘어나는 시간은 data.FreezeBudget(실제 시간)을 넘지 않음
// 돌진 이동은 실제 시간 기준이라 timeScale만으로는 플레이어가 안 멈춤 → 돌진 진행 자체를 따로 멈춤
// 수치는 SkillData(ThunderFlash.asset)의 Pass Freeze 항목
public partial class PlayerSkill
{
    [Header("Pass Freeze")] // [변경] 수치는 SkillData로 옮김
    [SerializeField] bool passFreeze = true;

    // 대상 옆을 지나는 순간 (위치, 마지막 대상인지) — 효과음·연출용
    public event System.Action<Vector3, bool> TargetPassed;

    bool passArmed;
    bool passIsLast;
    float passAt;        // 돌진 진행도(0~1)에서 대상 옆을 지나는 지점
    Vector3 passPoint;
    float passTimer;     // 남은 정지 시간 (실제 시간)
    float passSpent;
    bool freezingLast;

    // StartDash에서 호출: 이번 돌진에서 대상 옆을 지나는 지점 기록
    void ArmPass(IDamageable target, Vector3 start, Vector3 end)
    {
        passArmed = false;
        if (dashCount == 1) passSpent = 0f; // 새 일섬 시작
        if (target == null) return;

        Vector3 targetPos = ((Component)target).transform.position;
        float total = Flat(end - start).magnitude;
        if (total < 0.01f) return;

        passAt = Mathf.Clamp01(Flat(targetPos - start).magnitude / total);
        passPoint = targetPos;
        passIsLast = !stanceChain || dashCount >= ChainLimit() || !HasNextStanceTarget(); // 6명 넘게 벨 때 6번째를 마지막으로 착각하지 않게
        passArmed = true;
    }

    // UpdateDash에서 호출: 정지 중이면 돌진을 멈춤 (0을 돌려줌)
    float PassDashDelta(float dt)
    {
        if (passTimer > 0f)
        {
            passTimer -= dt;
            if (passTimer <= 0f) EndPassFreeze();
            return 0f;
        }

        if (passArmed && (dashTimer + dt) / data.DashDuration >= passAt)
        {
            // 핵심: 정확히 대상 옆 지점에서 멈추도록 그 지점까지만 진행
            float toPass = Mathf.Max(0f, passAt * data.DashDuration - dashTimer);
            TriggerPass();
            return toPass;
        }
        return dt;
    }

    // 지나가는 순간 → 이벤트(소리), 진입별 세기 × 남은 예산만큼 정지
    void TriggerPass()
    {
        passArmed = false;
        TargetPassed?.Invoke(passPoint, passIsLast);
        if (!passFreeze) return;

        float duration = (passIsLast ? data.LastFreezeTime : data.FreezeTime) * PassEntryScale();
        duration = Mathf.Min(duration, data.FreezeBudget - passSpent);
        if (duration <= 0.005f) return;

        passSpent += duration;
        passTimer = duration;
        freezingLast = passIsLast;
        SetTimeScale(data.FreezeWorldScale);
        if (passIsLast && data.LastFreezePunch > 0f && followCamera != null) followCamera.Punch(data.LastFreezePunch, 0.02f, duration, 0.25f, 0f);
    }

    // 앞 돌진 경로에서 이미 베인 표식은 빼고 남은 표식이 있는지
    bool HasNextStanceTarget()
    {
        foreach (IDamageable next in stanceQueue)
            if (IsValidTarget(next) && !IsPending(next)) return true;
        return false;
    }

    float PassEntryScale()
    {
        switch (stanceEntry)
        {
            case StanceEntry.Hold: return data.HoldFreezeScale;
            case StanceEntry.Enchant: return data.EnchantFreezeScale;
            case StanceEntry.Dodge: return data.DodgeFreezeScale;
            default: return data.TapFreezeScale;
        }
    }

    void EndPassFreeze()
    {
        passTimer = 0f;
        if (hitStopTimer <= 0f) RestoreTimeScale(); // 히트스톱이 걸려 있으면 그쪽이 복구
        if (followCamera != null && data.ReleaseShake > 0f) followCamera.Shake(freezingLast ? data.ReleaseShake * 1.6f : data.ReleaseShake, 0.1f);
    }

    // 돌진이 중간에 끝나거나 스킬이 끊겨도 정지가 남지 않게
    void ClearPassSlow()
    {
        passArmed = false;
        if (passTimer > 0f)
        {
            passTimer = 0f;
            if (hitStopTimer <= 0f) RestoreTimeScale();
        }
    }
}
