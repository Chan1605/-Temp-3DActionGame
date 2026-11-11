using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 씬을 넘어갈 때 이어받는 진행 상태 (한 판 동안만, 저장 파일 없음)
//  - 스킬트리: 남은 포인트 · 해금 목록 → SkillTreeMenu.Awake가 초기화 대신 복원, 수련 패시브는 Start에서 다시 적용
//  - 레벨 · 경험치 · 추가 스탯 → 새 씬이 로드되면 PlayerStats에 바로 넣음 (레벨업 이벤트 없이)
// SceneTransition.Load가 떠나기 직전에 Capture. 에디터에서 씬을 바로 플레이하면 비어 있어 예전과 같음
public static class RunState
{
    static bool hasSkills, hasStats;
    static int points;
    static readonly List<SkillId> unlocked = new List<SkillId>();
    static int level, xp;
    static Vector4 bonus;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // 타이틀의 "새 게임" 등 처음부터 시작할 때
    public static void Clear()
    {
        hasSkills = hasStats = false;
        unlocked.Clear();
    }

    public static void Capture()
    {
        hasSkills = SkillTree.Active;
        if (hasSkills)
        {
            points = SkillTree.Points;
            unlocked.Clear();
            unlocked.AddRange(SkillTree.Unlocked);
        }

        PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
        hasStats = stats != null;
        if (hasStats)
        {
            level = stats.Level;
            xp = stats.Xp;
            bonus = stats.Bonus;
        }
    }

    // SkillTreeMenu.Awake에서 호출. 복원했으면 true (그땐 Activate로 초기화하지 않음)
    public static bool TryRestoreSkills()
    {
        if (!hasSkills) return false;
        SkillTree.Restore(points, unlocked);
        return true;
    }

    // 씬 오브젝트의 Awake·OnEnable 뒤, Start 전에 불림
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!hasStats) return;
        PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
        if (stats != null) stats.Restore(level, xp, bonus);
    }
}
