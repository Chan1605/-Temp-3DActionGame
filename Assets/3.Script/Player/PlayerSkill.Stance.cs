using System.Collections.Generic;
using UnityEngine;

// 발도 자세 (첫 작품 SummonerForest3D의 조준형 일섬을 재해석)
//  - Q 홀드(2칸): 발도 자세. 적만 거의 멈추고 화면이 흑백, 조준점을 적에게 올려두면 표식 → 떼면 표식 순서대로 자동 연쇄
//  - 저스트 회피 직후 Q: 비용 없이, 조준 없이 주변 적을 자동 표식해 바로 발도 // 조준 단계 생략
//  - 뇌검 각성 중 Q: 각성 시간 n초를 떼어 "발도 시간"을 열고, 그동안 Q마다 비용 없이 발도 자세 (탭 Q 대신)
// 탭 Q(1칸)는 한 명만 벰 (tapChain) // 11/9 테스트 결과 반영
public partial class PlayerSkill
{
    enum StanceEntry { None, Hold, Dodge, Enchant } // [변경] 궁극기 진입(Ultimate) 제거 — 뇌신강림이 자체 연출로 바뀜

    [Header("Draw Stance")] // 수치는 SkillData(ThunderFlash.asset)로 옮김. 여기엔 켜기/끄기와 연결만
    [SerializeField] bool drawStance = true;
    [SerializeField] bool dodgeStance = true;
    [SerializeField] bool enchantStance = true;
    [SerializeField] PlayerEnchant enchant;
    [SerializeField] PlayerDodge dodge;

    readonly List<IDamageable> stanceMarks = new List<IDamageable>();
    readonly Queue<IDamageable> stanceQueue = new Queue<IDamageable>();
    StanceEntry stanceEntry;
    float stanceTimer;
    IDamageable markingTarget;
    float markProgress;
    float lastJustDodgeTime = -10f;
    bool stanceChain; // 표식 연쇄 진행 중 (QQQQ 입력 없이 자동)
    Camera stanceCam;
    float enchantWindow; // 남은 발도 시간 (실제 시간)

    // ===== 연출(DrawStanceFx)이 읽는 상태 =====
    public bool InDrawStance => phase == Phase.Stance;
    public bool ShowStanceMarks => phase == Phase.Stance || stanceChain;
    public float StanceRemaining01 => phase == Phase.Stance ? Mathf.Clamp01(1f - stanceTimer / data.StanceDuration) : 0f;
    public int StanceMarkCount => stanceMarks.Count;
    public int StanceMarkScale => Mathf.Max(1, Mathf.Min(StanceMaxMarks, data.MaxMarks)); // 사운드 등 "얼마나 찼나" 계산용 (제한 없음이면 data.MaxMarks 기준)
    // 각성 발도는 따로: 기본 제한 없음 → 일반 Q 차징(3명, 표식 확장 6명)과 차별
    public int StanceMaxMarks => stanceEntry == StanceEntry.Enchant
        ? (data.EnchantMaxMarks > 0 ? data.EnchantMaxMarks : int.MaxValue)
        : SkillTree.IsUnlocked(SkillId.StanceMarks) ? data.MaxMarks : Mathf.Min(data.BaseMarks, data.MaxMarks);
    public Transform StanceMark(int i) => i < stanceMarks.Count && IsValidTarget(stanceMarks[i]) ? ((Component)stanceMarks[i]).transform : null;
    public bool StanceMarkDone(int i) => i < stanceMarks.Count && IsPending(stanceMarks[i]); // 이미 벤 표식
    public Transform StanceMarkingTarget => phase == Phase.Stance && IsValidTarget(markingTarget) ? ((Component)markingTarget).transform : null;
    public float StanceMarkProgress => markProgress;
    public float StanceReticleRadius => data.ReticleRadius;
    public bool StanceByHold => stanceEntry == StanceEntry.Hold; // 안내 문구: 떼면 발도 / Q로 발도
    public float EnchantStanceWindow => enchantWindow; // 안내 문구용
    public bool StanceAutoDraw => stanceEntry == StanceEntry.Dodge; // 자동 발도는 조준 UI 없음

    void OnJustDodged() => lastJustDodgeTime = Time.unscaledTime;

    // 누르는 순간 바로 발도 자세로 들어갈 조건 (각성 중 · 저스트 회피 직후)
    StanceEntry InstantStanceEntry()
    {
        // 각성 발도 · 회피 발도는 스킬트리에서 해금해야 함
        if (enchantStance && SkillTree.IsUnlocked(SkillId.EnchantDraw) && enchant != null && (enchantWindow > 0f || enchant.IsActive)) return StanceEntry.Enchant;
        // 저스트 회피 직후는 비용 0이 기본 (값을 올리면 그만큼 확인)
        if (dodgeStance && SkillTree.IsUnlocked(SkillId.DodgeDraw) && Time.unscaledTime - lastJustDodgeTime <= data.DodgeStanceWindow
            && (!UsesGauge || thunder.CanSpend(data.DodgeStanceCost))) return StanceEntry.Dodge;
        return StanceEntry.None;
    }

    // 각성 중 첫 Q: 각성 시간을 떼어 발도 시간을 열고, 그동안 각성 시계는 멈춤 → 발도 시간이 끝나면 다시 흐름
    void OpenEnchantWindow()
    {
        if (enchantWindow > 0f || enchant == null) return;
        enchantWindow = enchant.ConsumeTime(data.EnchantStanceSeconds);
        if (enchantWindow > 0f) enchant.Paused = true;
        Debug.Log($"[Skill] 발도 시간 {enchantWindow:0.0}초");
    }

    void UpdateEnchantWindow(float dt)
    {
        if (enchantWindow <= 0f) return;
        enchantWindow -= dt;
        if (enchantWindow <= 0f || enchant == null || !enchant.IsActive) CloseEnchantWindow();
    }

    void CloseEnchantWindow()
    {
        enchantWindow = 0f;
        if (enchant != null) enchant.Paused = false;
    }

    bool CanHoldIntoStance => drawStance && UsesGauge && thunder.CanSpend(data.StanceCost);

    PlayerLockOn lockOn;
    void EnterStance(StanceEntry entry)
    {
        phase = Phase.Stance;
        stanceEntry = entry;
        stanceTimer = 0f;
        markingTarget = null;
        markProgress = 0f;
        stanceMarks.Clear();
        stanceQueue.Clear();
        stanceChain = false;
        stanceCam = Camera.main;

        // 조준하는 자세(홀드·각성)는 락온을 풂 → 카메라가 대상에 묶여 있으면 다른 적을 표식하기 어려움. 회피 직후 자동 발도는 조준이 없어 유지
        if (entry != StanceEntry.Dodge)
        {
            if (lockOn == null) lockOn = GetComponent<PlayerLockOn>();
            if (lockOn != null && lockOn.IsLocked) lockOn.Release();
        }

        CombatTime.SetHold(data.StanceEnemyScale); // 적만 멈춤 → 카메라·조준은 실시간 그대로
        animator.CrossFadeInFixedTime(chargeHash, 0.1f, 0, 0f);
        if (entry != StanceEntry.Hold) HoldStarted?.Invoke(); // 홀드 진입은 이미 HoldStarted가 나감
        if (followCamera != null) followCamera.Shake(0.06f, 0.12f);
        if (entry == StanceEntry.Enchant) OpenEnchantWindow();
        if (entry == StanceEntry.Dodge)
        {
            lastJustDodgeTime = -10f; // 한 번만
            ChargeLevelChanged?.Invoke(data.MaxChargeLevel); // 회피 직후는 처음부터 풀차지 연출
            if (followCamera != null) followCamera.Shake(0.12f, 0.15f);
            AutoMarkNearest(); // 조준 대신 가까운 적부터 자동 표식
        }
        Debug.Log($"[Skill] 발도 자세 ({entry})");
    }

    void UpdateStance(float dt)
    {
        stanceTimer += dt;

        // 회피 직후 자동 발도: 짧은 풀차지 정지 뒤 바로 돌진 (조준·입력 없음)
        if (stanceEntry == StanceEntry.Dodge)
        {
            if (stanceTimer >= data.DodgeDrawDelay) ReleaseStance();
            return;
        }

        FaceCamera(dt);
        UpdateMarking(dt);

        // 홀드로 들어왔으면 떼는 순간, 바로 들어왔으면 Q를 한 번 더 누르는 순간 발도
        bool release = stanceEntry == StanceEntry.Hold
            ? !skillAction.action.IsPressed()
            : stanceTimer > 0.1f && skillAction.action.WasPressedThisFrame();

        if (release || stanceTimer >= data.StanceDuration || stanceMarks.Count >= StanceMaxMarks) ReleaseStance();
    }

    void FaceCamera(float dt)
    {
        if (stanceCam == null) return;
        Vector3 forward = Flat(stanceCam.transform.forward);
        if (forward.sqrMagnitude < 0.0001f) return;
        Quaternion want = Quaternion.LookRotation(forward.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, aimTurnSpeed * dt);
    }

    // 핵심: 조준점(화면 중앙)에 가장 가까운 적을 골라, 계속 올려두면 게이지가 차서 표식. 다른 적으로 옮기면 처음부터
    void UpdateMarking(float dt)
    {
        IDamageable candidate = ReticleCandidate();
        if (candidate != markingTarget)
        {
            markingTarget = candidate;
            markProgress = 0f;
        }
        if (markingTarget == null) return;

        markProgress += dt / Mathf.Max(data.MarkTime, 0.01f);
        if (markProgress < 1f) return;

        stanceMarks.Add(markingTarget);
        markingTarget = null;
        markProgress = 0f;
        ChargeLevelChanged?.Invoke(Mathf.Min(stanceMarks.Count, data.MaxChargeLevel)); // 표식마다 칼로 번개가 끌려 올라옴
        if (followCamera != null) followCamera.Shake(0.05f, 0.08f);
    }

    // 가까운 순으로 dodgeAutoMarks명까지 표식 (적이 없으면 ReleaseStance가 비용 없이 취소)
    void AutoMarkNearest()
    {
        int want = Mathf.Min(data.DodgeAutoMarks, StanceMaxMarks);
        while (stanceMarks.Count < want)
        {
            EnemySwordsman best = null;
            float bestDist = data.DodgeAutoRange;
            foreach (EnemySwordsman enemy in EnemySwordsman.Alive)
            {
                if (enemy == null || !enemy.CanBeTargeted || stanceMarks.Contains(enemy)) continue;
                float d = Flat(enemy.transform.position - transform.position).magnitude;
                if (d >= bestDist) continue;
                bestDist = d;
                best = enemy;
            }
            if (best == null) break;
            stanceMarks.Add(best);
        }
    }

    IDamageable ReticleCandidate()
    {
        if (stanceCam == null) return null;

        IDamageable best = null;
        float bestDist = data.ReticleRadius;
        float aspect = stanceCam.aspect;
        foreach (EnemySwordsman enemy in EnemySwordsman.Alive)
        {
            if (enemy == null || !enemy.CanBeTargeted || stanceMarks.Contains(enemy)) continue;
            if (Flat(enemy.transform.position - transform.position).magnitude > data.StanceRange) continue;

            Vector3 v = stanceCam.WorldToViewportPoint(enemy.transform.position + Vector3.up * 1.1f);
            if (v.z <= 0f) continue;
            float dx = (v.x - 0.5f) * aspect; // 가로·세로 같은 비율로
            float d = Mathf.Sqrt(dx * dx + (v.y - 0.5f) * (v.y - 0.5f));
            if (d >= bestDist) continue;
            bestDist = d;
            best = enemy;
        }
        return best;
    }

    void ReleaseStance()
    {
        CombatTime.ClearHold();
        HoldEnded?.Invoke();

        // 표식이 없으면 비용 없이 취소
        if (stanceMarks.Count == 0)
        {
            Debug.Log("[Skill] 발도 자세 취소 (표식 없음)");
            EndSkill();
            return;
        }

        PayStance();
        foreach (IDamageable mark in stanceMarks) stanceQueue.Enqueue(mark);
        stanceChain = true;

        IDamageable first = NextStanceTarget();
        if (first == null)
        {
            EndSkill();
            return;
        }
        StartDash(first, transform.forward);
    }

    void PayStance()
    {
        switch (stanceEntry)
        {
            case StanceEntry.Enchant:
                break; // 발도 시간 안에서는 무료 (시간은 열 때 이미 뗌)
            case StanceEntry.Dodge:
                if (UsesGauge && data.DodgeStanceCost > 0f) thunder.TrySpend(data.DodgeStanceCost); // 기본 무료
                break;
            default:
                thunder.TrySpend(data.StanceCost);
                break;
        }
    }

    // 이미 쓰러졌거나 앞 돌진 경로에서 베인 표식은 건너뜀
    IDamageable NextStanceTarget()
    {
        while (stanceQueue.Count > 0)
        {
            IDamageable next = stanceQueue.Dequeue();
            if (IsValidTarget(next) && !IsPending(next)) return next;
        }
        return null;
    }

    // 표식 연쇄: 착지 후 짧은 간격 뒤 다음 표식으로 자동 돌진
    void UpdateStanceChain()
    {
        if (windowTimer < data.StanceChainDelay) return;
        IDamageable next = NextStanceTarget();
        if (next != null && dashCount < ChainLimit()) StartDash(next, transform.forward); // 착지 판정과 같은 한도
        else BeginFinish();
    }

    void ClearStance()
    {
        CombatTime.ClearHold();
        stanceMarks.Clear();
        stanceQueue.Clear();
        stanceChain = false;
        markingTarget = null;
        markProgress = 0f;
        stanceEntry = StanceEntry.None;
    }
}
