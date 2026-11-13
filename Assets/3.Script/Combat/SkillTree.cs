using System;
using System.Collections.Generic;

// 해금할 수 있는 기능 (이미 구현된 기능을 처음엔 잠가 두고 화톳불에서 연다)
public enum SkillId
{
    ChainStrike1,  // 연격 Ⅰ (탭 Q 2연)
    ChainStrike2,  // 연격 Ⅱ (3연)
    ChainStrike3,  // 연격 Ⅲ (4연)
    StanceMarks,   // 발도 표식 3 → 6
    EnchantDraw,   // 각성 중 Q 발도
    Launcher,      // 삭제됨 (공중 콤보). 뒤 번호가 바뀌지 않게 자리만 남김 — 스킬트리에선 숨김
    ChargeLevel3,  // 차지 강타 3단계 (풀차지 섬격)
    KanpaSmash,    // 간파 뒤 뇌락
    DodgeDraw,     // 저스트 회피 뒤 무료 발도
    // 수련(패시브)
    Vitality1,     // 최대 체력 +
    Vitality2,
    ThunderSlot1,  // 뇌기 칸 +1
    ThunderSlot2,
    // 궁극기 (V). 뇌운낙뢰진은 처음부터 열려 있음
    UltimateStorm,
    UltimateHeavens,
    UltimateDescent
}

// 스킬 포인트 · 해금 상태 (한 판 동안만 유지)
// 핵심: 스킬트리(SkillTreeMenu)가 씬에 없으면 Active=false → 모든 기능이 열린 상태 (기존 테스트 씬이 그대로 동작)
public static class SkillTree
{
    static readonly HashSet<SkillId> unlocked = new HashSet<SkillId>();

    public static bool Active { get; private set; }
    public static int Points { get; private set; }
    public static event Action Changed;

    public static bool IsUnlocked(SkillId id) => !Active || id == SkillId.UltimateStorm || unlocked.Contains(id); // 1칸 궁극기는 기본 기술

    // V로 고를 수 있는 가장 높은 궁극기 단계 (1~3)
    public static int UltimateTier => IsUnlocked(SkillId.UltimateDescent) ? 3 : IsUnlocked(SkillId.UltimateHeavens) ? 2 : 1;
    public static IEnumerable<SkillId> Unlocked => unlocked; // 씬 이동 때 RunState가 저장

    // 탭 Q 연격 단계 (0 = 연격 없음, 1~3)
    public static int ChainLevel =>
        IsUnlocked(SkillId.ChainStrike3) ? 3 : IsUnlocked(SkillId.ChainStrike2) ? 2 : IsUnlocked(SkillId.ChainStrike1) ? 1 : 0;

    public static void Activate(int startPoints)
    {
        Active = true;
        unlocked.Clear();
        Points = startPoints;
        Changed?.Invoke();
    }

    // 씬 이동 뒤 이어받기 (RunState)
    public static void Restore(int points, IEnumerable<SkillId> ids)
    {
        Active = true;
        unlocked.Clear();
        unlocked.UnionWith(ids);
        Points = points;
        Changed?.Invoke();
    }

    public static void AddPoints(int amount)
    {
        Points += amount;
        Changed?.Invoke();
    }

    public static bool TryUnlock(SkillId id, int cost)
    {
        if (unlocked.Contains(id) || Points < cost) return false;
        Points -= cost;
        unlocked.Add(id);
        Changed?.Invoke();
        return true;
    }

    // [변경] 스킬 초기화: 해금을 모두 지우고 돌려줄 포인트를 더함 (돌려줄 양은 비용을 아는 SkillTreeMenu가 계산)
    public static void Respec(int refund)
    {
        unlocked.Clear();
        Points += refund;
        Changed?.Invoke();
    }

    public static void UnlockAll()
    {
        foreach (SkillId id in Enum.GetValues(typeof(SkillId))) unlocked.Add(id);
        Changed?.Invoke();
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        Active = false;
        Points = 0;
        unlocked.Clear();
        Changed = null;
    }
}
