using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// 본편 한 판의 전투 기록 + 보스 처치 시 결과 화면 띄우기
// 구역(ZoneArena)이 있는 씬이 열리면 자동으로 생김 (수련장 · 타이틀 제외). 씬을 다시 불러오면 기록도 새로 시작
public class RunRecord : MonoBehaviour
{
    // ===== 랭크 기준 (100점 만점) =====
    // 시간 40점: parTime 이내면 만점, parTime의 2배에서 0점
    // 피격 30점: 한 번 맞을 때마다 -2 · 재도전 20점: 한 번에 -10 · 기술 10점: 패링·간파·저스트 회피·처형 합 20회면 만점
    public const float ParTime = 900f;
    const float TimePoints = 40f, HitPoints = 30f, HitPenalty = 2f, DeathPoints = 20f, DeathPenalty = 10f, SkillPoints = 10f;
    const int SkillGoal = 20;
    static readonly int[] RankCuts = { 85, 70, 50 }; // S · A · B, 나머지 C
    static readonly string[] RankNames = { "S", "A", "B", "C" };

    const string BestTimeKey = "Result.BestTime";
    const string BestScoreKey = "Result.BestScore";
    const float ResultDelay = 3f; // 보스가 쓰러지는 모습을 본 뒤에 띄움

    public static RunRecord Instance { get; private set; }

    public float PlayTime { get; private set; }
    public int Kills { get; private set; }
    public int Executions { get; private set; }
    public int Parries { get; private set; }
    public int Counters { get; private set; }
    public int JustDodges { get; private set; }
    public int SkillCuts { get; private set; }
    public int HitsTaken { get; private set; }
    public int Deaths { get; private set; }
    public int Level { get; private set; }
    public int Score { get; private set; }
    public string Rank { get; private set; }
    public bool NewBestTime { get; private set; }
    public string FinalZone { get; private set; }
    public bool Completed { get; private set; }

    public event Action Finished;

    PlayerHealth health;
    PlayerExecution execution;
    PlayerParry parry;
    PlayerKanpa kanpa;
    PlayerDodge dodge;
    PlayerSkill skill;
    PlayerStats stats;

    // [변경] 에디터에서 처음 연 씬은 sceneLoaded가 오지 않을 수 있어 첫 씬은 AfterSceneLoad에서 직접 확인, 이후 씬 전환은 sceneLoaded
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) TryCreate();
    }

    static void TryCreate()
    {
        if (Instance != null || TutorialDirector.Instance != null) return; // 이미 있음(첫 씬에서 두 경로가 겹칠 때) · 수련장
        if (FindAnyObjectByType<ZoneArena>() == null) return;
        var go = new GameObject("RunRecord");
        go.AddComponent<RunRecord>();
        go.AddComponent<ResultScreen>();
        Debug.Log("[Result] 기록 시작"); // [변경] 생성 확인용
    }

    void Awake() => Instance = this;

    void Start()
    {
        health = FindAnyObjectByType<PlayerHealth>();
        execution = FindAnyObjectByType<PlayerExecution>();
        parry = FindAnyObjectByType<PlayerParry>();
        kanpa = FindAnyObjectByType<PlayerKanpa>();
        dodge = FindAnyObjectByType<PlayerDodge>();
        skill = FindAnyObjectByType<PlayerSkill>();
        stats = FindAnyObjectByType<PlayerStats>();

        EnemySwordsman.AnyKilled += OnKilled;
        if (health != null) { health.Damaged += OnDamaged; health.Died += OnDied; }
        if (execution != null) execution.Executed += OnExecuted;
        if (parry != null) parry.Parried += OnParried;
        if (kanpa != null) kanpa.Countered += OnCountered;
        if (dodge != null) dodge.JustDodged += OnJustDodged;
        if (skill != null) skill.TargetCut += OnSkillCut;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        EnemySwordsman.AnyKilled -= OnKilled;
        if (health != null) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
        if (execution != null) execution.Executed -= OnExecuted;
        if (parry != null) parry.Parried -= OnParried;
        if (kanpa != null) kanpa.Countered -= OnCountered;
        if (dodge != null) dodge.JustDodged -= OnJustDodged;
        if (skill != null) skill.TargetCut -= OnSkillCut;
    }

    // 실제 흐른 시간 (히트스톱·슬로와 무관), 일시정지 · 메뉴 중엔 멈춤
    void Update()
    {
        if (!Completed && !GamePause.IsPaused) PlayTime += Time.unscaledDeltaTime;
    }

    void OnDamaged(DamageInfo info) { if (!Completed) HitsTaken++; }
    void OnDied() { if (!Completed) Deaths++; }
    void OnExecuted(EnemySwordsman e) { if (!Completed) Executions++; }
    void OnParried(GameObject e) { if (!Completed) Parries++; }
    void OnCountered(GameObject e) { if (!Completed) Counters++; }
    void OnJustDodged() { if (!Completed) JustDodges++; }
    void OnSkillCut(Vector3 p) { if (!Completed) SkillCuts++; }

    void OnKilled(EnemySwordsman e)
    {
        if (Completed || e == null || e.IsTrainer) return;
        Kills++;
        if (e.IsBoss) Complete();
    }

    void Complete()
    {
        Completed = true;
        FinalZone = ZoneArena.Fighting != null ? ZoneArena.Fighting.DisplayName : "";
        Level = stats != null ? stats.Level : 1;

        // 점수 → 랭크
        float time = TimePoints * Mathf.Clamp01((2f * ParTime - PlayTime) / ParTime);
        float hits = Mathf.Max(0f, HitPoints - HitPenalty * HitsTaken);
        float deaths = Mathf.Max(0f, DeathPoints - DeathPenalty * Deaths);
        float skills = SkillPoints * Mathf.Clamp01((Parries + Counters + JustDodges + Executions) / (float)SkillGoal);
        Score = Mathf.RoundToInt(time + hits + deaths + skills);
        int r = 0;
        while (r < RankCuts.Length && Score < RankCuts[r]) r++;
        Rank = RankNames[r];

        float best = PlayerPrefs.GetFloat(BestTimeKey, 0f);
        NewBestTime = best <= 0f || PlayTime < best;
        if (NewBestTime) PlayerPrefs.SetFloat(BestTimeKey, PlayTime);
        if (Score > PlayerPrefs.GetInt(BestScoreKey, 0)) PlayerPrefs.SetInt(BestScoreKey, Score);
        PlayerPrefs.Save();

        Debug.Log($"[Result] {FormatTime(PlayTime)} · 처치 {Kills} · 피격 {HitsTaken} · 재도전 {Deaths} · {Score}점 {Rank}");
        Invoke(nameof(RaiseFinished), ResultDelay);
    }

    void RaiseFinished() => Finished?.Invoke(); // Invoke는 timeScale을 따르므로 슬로 연출이 있으면 그만큼 늦게 뜸

    public static string FormatTime(float seconds)
    {
        int s = Mathf.FloorToInt(seconds);
        return $"{s / 60:00}:{s % 60:00}";
    }
}
