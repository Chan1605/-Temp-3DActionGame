using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum TutorialGoal
{
    ReachStation,   // 단계의 수련터 가운데로 이동
    LightCombo,     // □ 연속 n타
    HeavyInCombo,   // □ 콤보 중 △
    Dodge,
    JustDodge,
    Parry,
    Kanpa,
    TapStrike,      // 탭 Q로 벤 횟수
    StanceDraw,     // 발도 자세 한 번에 벤 수
    EnchantStrike,  // 각성 평타 낙뢰 횟수
    LockOn,
    Execute,
    ShrineRest,     // 화톳불에서 스킬트리 열기
    UnlockSkill,
}

// 동선형 튜토리얼 진행기 (수련장 씬, Tools/Setup/Tutorial Scene이 배치)
// 수련터(구역)마다 단계를 차례로 깨면 그 수련터의 출구 결계가 열림. 완료 판정은 플레이어 컴포넌트 이벤트로만 확인
// 단계 문구·순서·횟수는 인스펙터 Steps에서 수정
public class TutorialDirector : MonoBehaviour
{
    [Serializable]
    public class Station
    {
        public string name;
        public Transform center;
        public float radius = 13f;
        public GameObject exitBarrier;
    }

    [Serializable]
    public class Step
    {
        public int station;
        public TutorialGoal goal;
        public int count = 1;
        public string title;
        [TextArea(1, 3)] public string description;
        public string keys;
        public bool fillGauge;   // 단계 동안 뇌기를 계속 채워 줌
        public bool skippable;   // skipAfter초가 지나면 건너뛰기 안내
    }

    [SerializeField] List<Station> stations = new List<Station>();
    [SerializeField] List<Step> steps = DefaultSteps();
    [SerializeField] Font titleFont;
    [SerializeField] Font bodyFont;
    [SerializeField] float comboGap = 1.2f;       // 이 시간 안에 이어 눌러야 콤보로 셈
    [SerializeField] float reachRatio = 0.6f;     // 수련터 반경의 이 비율 안으로 들어오면 도착
    [SerializeField] float skipAfter = 30f;       // 어려운 단계에서 건너뛰기 안내까지 (실제 시간)
    [SerializeField] float stepDelay = 0.9f;      // 완료 표시 뒤 다음 단계까지 (실제 시간)
    [SerializeField] Key skipKey = Key.Enter;

    const string DoneKey = "Tutorial.Done";

    public static TutorialDirector Instance { get; private set; }
    public static bool CompletedBefore => PlayerPrefs.GetInt(DoneKey, 0) == 1;
    public bool Finished { get; private set; }

    Transform player;
    PlayerCombat combat;
    PlayerDodge dodge;
    PlayerParry parry;
    PlayerKanpa kanpa;
    PlayerSkill skill;
    PlayerEnchant enchant;
    PlayerLockOn lockOn;
    PlayerExecution execution;
    PlayerThunder thunder;
    TutorialGuide guide;

    int index = -1;
    int progress;
    float stepTimer;
    float doneTimer = -1f;
    int lightStreak;
    float lastAttackTime = -99f;
    int stanceCuts;
    int unlockedAtStart;

    Step Current => index >= 0 && index < steps.Count ? steps[index] : null;

    void Awake() => Instance = this;

    void Start()
    {
        combat = FindAnyObjectByType<PlayerCombat>();
        dodge = FindAnyObjectByType<PlayerDodge>();
        parry = FindAnyObjectByType<PlayerParry>();
        kanpa = FindAnyObjectByType<PlayerKanpa>();
        skill = FindAnyObjectByType<PlayerSkill>();
        enchant = FindAnyObjectByType<PlayerEnchant>();
        lockOn = FindAnyObjectByType<PlayerLockOn>();
        execution = FindAnyObjectByType<PlayerExecution>();
        thunder = FindAnyObjectByType<PlayerThunder>();
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;

        if (combat != null) combat.AttackStarted += OnAttack;
        if (dodge != null) { dodge.Dodged += OnDodged; dodge.JustDodged += OnJustDodged; }
        if (parry != null) parry.Parried += OnParried;
        if (kanpa != null) kanpa.Countered += OnCountered;
        if (skill != null) { skill.HoldStarted += OnHoldStarted; skill.TargetCut += OnTargetCut; }
        if (enchant != null) enchant.Struck += OnEnchantStruck;
        if (lockOn != null) lockOn.TargetChanged += OnLockOn;
        if (execution != null) execution.Executed += OnExecuted;
        SkillTree.Changed += OnSkillTreeChanged;

        foreach (Station s in stations)
            if (s.exitBarrier != null) s.exitBarrier.SetActive(true);

        guide = TutorialGuide.Create(transform, titleFont, bodyFont);
        NextStep();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (combat != null) combat.AttackStarted -= OnAttack;
        if (dodge != null) { dodge.Dodged -= OnDodged; dodge.JustDodged -= OnJustDodged; }
        if (parry != null) parry.Parried -= OnParried;
        if (kanpa != null) kanpa.Countered -= OnCountered;
        if (skill != null) { skill.HoldStarted -= OnHoldStarted; skill.TargetCut -= OnTargetCut; }
        if (enchant != null) enchant.Struck -= OnEnchantStruck;
        if (lockOn != null) lockOn.TargetChanged -= OnLockOn;
        if (execution != null) execution.Executed -= OnExecuted;
        SkillTree.Changed -= OnSkillTreeChanged;
    }

    void Update()
    {
        Step step = Current;
        if (Finished || step == null) return;

        // 화톳불은 스킬트리가 게임을 멈추므로 일시정지 확인보다 먼저
        if (step.goal == TutorialGoal.ShrineRest && SkillTreeMenu.Instance != null && SkillTreeMenu.Instance.IsOpen) Add(TutorialGoal.ShrineRest);
        if (GamePause.IsPaused) return;

        float dt = Time.unscaledDeltaTime;
        if (doneTimer >= 0f)
        {
            doneTimer -= dt;
            if (doneTimer < 0f) CompleteStep();
            return;
        }

        if (step.goal == TutorialGoal.ReachStation && Reached(step.station)) Add(TutorialGoal.ReachStation);

        if (step.fillGauge && thunder != null && skill != null && !skill.IsActive && thunder.Gauge < thunder.MaxGauge)
            thunder.Add(thunder.MaxGauge - thunder.Gauge);

        stepTimer += dt;
        if (step.skippable && stepTimer >= skipAfter)
        {
            guide.ShowSkipHint(skipKey);
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[skipKey].wasPressedThisFrame) MarkDone();
        }
    }

    // ===== 진행 =====
    void NextStep()
    {
        index++;
        progress = 0;
        stepTimer = 0f;
        lightStreak = 0;
        stanceCuts = 0;

        Step step = Current;
        if (step == null)
        {
            Finish();
            return;
        }
        if (step.goal == TutorialGoal.UnlockSkill) unlockedAtStart = CountUnlocked();

        int first = steps.FindIndex(s => s.station == step.station);
        int total = steps.FindAll(s => s.station == step.station).Count;
        guide.ShowStep(StationLabel(step.station), index - first + 1, total, step.title, step.description, step.keys, step.count);
    }

    void Add(TutorialGoal goal) => SetProgress(goal, progress + 1);

    void SetProgress(TutorialGoal goal, int value)
    {
        Step step = Current;
        if (step == null || step.goal != goal || doneTimer >= 0f || value <= progress) return;
        progress = Mathf.Min(value, step.count);
        guide.SetProgress(progress, step.count);
        if (progress >= step.count) MarkDone();
    }

    void MarkDone()
    {
        if (doneTimer >= 0f) return;
        doneTimer = stepDelay;
        guide.ShowDone();
        if (Current.goal == TutorialGoal.ReachStation) guide.ShowBanner(StationLabel(Current.station));
    }

    // 핵심: 단계를 끝낼 때 다음 단계가 다른 수련터면 지금 수련터의 출구 결계를 엶
    void CompleteStep()
    {
        doneTimer = -1f;
        Step step = Current;
        Step next = index + 1 < steps.Count ? steps[index + 1] : null;
        if (next == null || next.station != step.station) OpenBarrier(step.station);
        NextStep();
    }

    void Finish()
    {
        Finished = true;
        foreach (Station s in stations)
            if (s.exitBarrier != null) s.exitBarrier.SetActive(false);
        PlayerPrefs.SetInt(DoneKey, 1);
        guide.ShowFinish("수련 완료", "출구 결계가 열렸습니다. 북쪽 길 끝에서 산문으로 이어집니다."); // [변경]
    }

    // ESC 메뉴 "SKIP TUTORIAL"
    public void SkipAll()
    {
        if (Finished) return;
        index = steps.Count;
        doneTimer = -1f;
        Finish();
    }

    void OpenBarrier(int station)
    {
        if (station < 0 || station >= stations.Count) return;
        GameObject barrier = stations[station].exitBarrier;
        if (barrier != null && barrier.activeSelf)
        {
            barrier.SetActive(false);
            guide.ShowBanner("결계가 열렸습니다");
        }
    }

    bool Reached(int station)
    {
        if (player == null || station < 0 || station >= stations.Count || stations[station].center == null) return false;
        Vector3 d = player.position - stations[station].center.position;
        d.y = 0f;
        float r = stations[station].radius * reachRatio;
        return d.sqrMagnitude <= r * r;
    }

    string StationLabel(int station) =>
        station >= 0 && station < stations.Count && !string.IsNullOrEmpty(stations[station].name)
            ? $"수련 {station + 1} · {stations[station].name}" : $"수련 {station + 1}";

    static int CountUnlocked()
    {
        int n = 0;
        foreach (SkillId id in Enum.GetValues(typeof(SkillId)))
            if (SkillTree.Active && SkillTree.IsUnlocked(id)) n++;
        return n;
    }

    // ===== 이벤트 → 진행도 =====
    void OnAttack(AttackData data, AttackInput input)
    {
        float now = Time.time;
        bool chained = now - lastAttackTime <= comboGap;
        lastAttackTime = now;
        if (input == AttackInput.Light)
        {
            lightStreak = chained ? lightStreak + 1 : 1;
            SetProgress(TutorialGoal.LightCombo, lightStreak);
        }
        else
        {
            if (chained && lightStreak > 0) Add(TutorialGoal.HeavyInCombo);
            lightStreak = 0;
        }
    }

    void OnDodged(Vector3 direction) => Add(TutorialGoal.Dodge);
    void OnJustDodged() => Add(TutorialGoal.JustDodge);
    void OnParried(GameObject enemy) => Add(TutorialGoal.Parry);
    void OnCountered(GameObject enemy) => Add(TutorialGoal.Kanpa);
    void OnHoldStarted() => stanceCuts = 0;
    void OnEnchantStruck(Vector3 point) => Add(TutorialGoal.EnchantStrike);
    void OnExecuted(EnemySwordsman enemy) => Add(TutorialGoal.Execute);
    void OnSkillTreeChanged() => SetProgress(TutorialGoal.UnlockSkill, CountUnlocked() - unlockedAtStart);

    void OnLockOn(Transform target)
    {
        if (target != null) Add(TutorialGoal.LockOn);
    }

    void OnTargetCut(Vector3 point)
    {
        if (skill != null && skill.ShowStanceMarks) SetProgress(TutorialGoal.StanceDraw, ++stanceCuts);
        else Add(TutorialGoal.TapStrike);
    }

    // ===== 기본 단계 =====
    public static List<Step> DefaultSteps() => new List<Step>
    {
        S(0, TutorialGoal.ReachStation, 1, "수련터로", "길을 따라 앞의 수련터 가운데로 가세요.", "WASD 이동 · Shift 홀드 달리기"),
        S(0, TutorialGoal.LightCombo, 3, "연속 베기", "좌클릭을 이어 눌러 3연속으로 베세요.", "좌클릭"),
        S(0, TutorialGoal.HeavyInCombo, 1, "강공격 섞기", "좌클릭 콤보 중에 우클릭을 섞으면 다른 갈래로 이어집니다.", "좌클릭 → 우클릭"),

        S(1, TutorialGoal.ReachStation, 1, "다음 수련터로", "결계가 열렸습니다. 다음 수련터로 가세요.", "WASD 이동"),
        S(1, TutorialGoal.Dodge, 2, "회피", "Shift를 짧게 눌러 공격을 피하세요.", "Shift"),
        S(1, TutorialGoal.JustDodge, 1, "저스트 회피", "공격이 닿기 직전에 회피하면 시간이 느려지고 뇌기 1칸을 얻습니다.", "Shift (타이밍)", skippable: true),
        S(1, TutorialGoal.Parry, 1, "패링", "가만히 선 채로 적의 칼이 닿기 직전에 우클릭. 뇌기 1칸을 씁니다.", "우클릭 (대기 중)", fillGauge: true, skippable: true),
        S(1, TutorialGoal.Kanpa, 1, "간파", "콤보 중 적이 공격하면 우클릭 강공격으로 맞받아치세요.", "좌클릭 콤보 → 우클릭", skippable: true),

        S(2, TutorialGoal.ReachStation, 1, "다음 수련터로", "다음 수련터로 가세요. 모은 뇌기로 벽력일섬을 씁니다.", "WASD 이동"),
        S(2, TutorialGoal.TapStrike, 1, "벽력일섬", "Q를 짧게 누르면 가까운 적에게 번개처럼 돌진해 벱니다. 뇌기 1칸.", "Q 탭", fillGauge: true),
        S(2, TutorialGoal.StanceDraw, 3, "발도 자세", "Q를 길게 누른 채 화면 가운데 조준점을 적에게 올려 표식 3개. 떼면 차례로 벱니다.", "Q 홀드 → 조준 → 떼기", fillGauge: true),

        S(3, TutorialGoal.ReachStation, 1, "마지막 수련터로", "마지막 수련터로 가세요.", "WASD 이동"),
        S(3, TutorialGoal.EnchantStrike, 3, "뇌검 각성", "뇌기 2칸 이상에서 R. 각성 중엔 평타마다 낙뢰가 떨어집니다.", "R → 좌클릭", fillGauge: true),
        S(3, TutorialGoal.LockOn, 1, "락온", "적을 시야에 두고 고정하세요.", "Tab / 휠 클릭"),
        S(3, TutorialGoal.Execute, 1, "처형", "패링으로 적을 무너뜨린 뒤 가까이에서 F.", "우클릭 패링 → F", fillGauge: true, skippable: true),
        S(3, TutorialGoal.ShrineRest, 1, "화톳불", "화톳불 앞에서 E. 체력이 회복되고 스킬트리가 열립니다.", "E"),
        S(3, TutorialGoal.UnlockSkill, 1, "스킬 해금", "스킬 포인트로 기술을 하나 해금하세요. 노드를 고르면 미리보기 영상이 나옵니다.", "클릭 → 해금"),
    };

    static Step S(int station, TutorialGoal goal, int count, string title, string description, string keys, bool fillGauge = false, bool skippable = false) =>
        new Step { station = station, goal = goal, count = count, title = title, description = description, keys = keys, fillGauge = fillGauge, skippable = skippable };
}
