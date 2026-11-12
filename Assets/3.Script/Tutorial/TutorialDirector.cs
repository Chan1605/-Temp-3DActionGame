using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum TutorialGoal
{
    ReachCenter,    // 수련장 가운데로 이동
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
    Ultimate,       // [변경] 궁극기 한 번 쓰기 (연출이 끝나면 완료). 값이 밀리지 않게 맨 뒤에 둠
}

// 한 구역 수련장 진행기 (Tools/Setup/Tutorial Scene이 배치)
// 수련장 하나에서 단계를 차례로 배움. 단계마다 필요한 상대 무리(lineup)만 꺼내 놓고 나머지는 숨김
// 모든 단계를 끝내면 북쪽 출구 결계가 열림. 완료 판정은 플레이어 컴포넌트 이벤트로만 확인
// 단계 문구·순서·횟수·상대 무리는 인스펙터 Lessons에서 수정
public class TutorialDirector : MonoBehaviour
{
    [Serializable]
    public class Lesson
    {
        public int chapter;      // chapters 번호 (안내 패널 머리말)
        public int lineup = -1;  // lineups 번호, -1이면 상대 없음
        public TutorialGoal goal;
        public int count = 1;
        public string title;
        [TextArea(1, 3)] public string description;
        public string keys;
        public bool fillGauge;   // 단계 동안 뇌기를 계속 채워 줌 (궁극기 단계에선 궁극기 게이지도)
        public bool skippable;   // skipAfter초가 지나면 건너뛰기 안내
    }

    [SerializeField] Transform center;
    [SerializeField] float centerRadius = 4f;     // 이 안으로 들어오면 가운데 도착
    [SerializeField] GameObject exitBarrier;
    [SerializeField] List<GameObject> lineups = new List<GameObject>();
    [SerializeField] string[] chapters = { "기본기", "방어", "벽력일섬", "각성", "성장" };
    [SerializeField] List<Lesson> lessons = DefaultLessons();
    [SerializeField] Font titleFont;
    [SerializeField] Font bodyFont;
    [SerializeField] float comboGap = 1.2f;       // 이 시간 안에 이어 눌러야 콤보로 셈
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
    PlayerUltimate ultimate; // [변경]
    TutorialGuide guide;

    int index = -1;
    int progress;
    float stepTimer;
    float doneTimer = -1f;
    int lightStreak;
    float lastAttackTime = -99f;
    int stanceCuts;
    int unlockedAtStart;
    int activeLineup = -2; // 처음엔 무엇도 아님 → 첫 단계에서 반드시 정리

    Lesson Current => index >= 0 && index < lessons.Count ? lessons[index] : null;

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
        ultimate = FindAnyObjectByType<PlayerUltimate>();
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
        if (ultimate != null) ultimate.Finished += OnUltimateFinished;
        SkillTree.Changed += OnSkillTreeChanged;
        AddMissingLessons();

        if (exitBarrier != null) exitBarrier.SetActive(true);

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
        if (ultimate != null) ultimate.Finished -= OnUltimateFinished;
        SkillTree.Changed -= OnSkillTreeChanged;
    }

    // [변경] 이미 씬에 저장된 단계 목록엔 새 기본 단계가 없음 → 빠진 목표만 기본값에서 찾아 맨 뒤에 붙임 (인스펙터에서 고친 단계는 그대로)
    void AddMissingLessons()
    {
        foreach (Lesson lesson in DefaultLessons())
            if (!lessons.Exists(l => l.goal == lesson.goal)) lessons.Add(lesson);
    }

    void Update()
    {
        Lesson step = Current;
        if (Finished || step == null) return;

        // 화톳불은 스킬트리가 게임을 멈추므로 일시정지 확인보다 먼저
        if (step.goal == TutorialGoal.ShrineRest && SkillTreeMenu.Instance != null && SkillTreeMenu.Instance.IsOpen) Add(TutorialGoal.ShrineRest);
        if (GamePause.IsPaused) return;

        float dt = Time.unscaledDeltaTime;
        if (doneTimer >= 0f)
        {
            doneTimer -= dt;
            if (doneTimer < 0f) NextStep();
            return;
        }

        if (step.goal == TutorialGoal.ReachCenter && ReachedCenter()) Add(TutorialGoal.ReachCenter);

        if (step.fillGauge && thunder != null && skill != null && !skill.IsActive && thunder.Gauge < thunder.MaxGauge)
            thunder.Add(thunder.MaxGauge - thunder.Gauge);
        if (step.fillGauge && step.goal == TutorialGoal.Ultimate && ultimate != null && !ultimate.Active && ultimate.Gauge < PlayerUltimate.MaxBars)
            ultimate.SetGauge(PlayerUltimate.MaxBars); // [변경] 궁극기 단계는 3칸 채워 둠

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
        doneTimer = -1f;
        int previousChapter = Current != null ? Current.chapter : -1;
        TutorialGoal previousGoal = Current != null ? Current.goal : TutorialGoal.ReachCenter;
        index++;
        progress = 0;
        stepTimer = 0f;
        lightStreak = 0;
        stanceCuts = 0;

        Lesson step = Current;
        if (step == null)
        {
            Finish();
            return;
        }
        // 해금 수는 화톳불 단계부터 셈 → 스킬트리를 열자마자 해금해도 다음 해금 단계에서 인정
        if (step.goal == TutorialGoal.ShrineRest || (step.goal == TutorialGoal.UnlockSkill && previousGoal != TutorialGoal.ShrineRest))
            unlockedAtStart = CountUnlocked();
        SetLineup(step.lineup);
        if (step.chapter != previousChapter && index > 0) guide.ShowBanner(ChapterName(step.chapter));

        int first = lessons.FindIndex(s => s.chapter == step.chapter);
        int total = lessons.FindAll(s => s.chapter == step.chapter).Count;
        guide.ShowStep(ChapterLabel(step.chapter), index - first + 1, total, step.title, step.description, step.keys, step.count);

        if (step.goal == TutorialGoal.UnlockSkill) // 이미 해금했으면 바로 완료, 포인트가 없으면 막히지 않게 완료
        {
            OnSkillTreeChanged();
            if (doneTimer < 0f && SkillTree.Active && SkillTree.Points <= 0) MarkDone();
        }
    }

    // 핵심: 이번 단계에 필요한 상대 무리만 켬. 새로 나타나는 상대는 낙뢰와 함께 등장(ZoneFx)
    void SetLineup(int lineup)
    {
        if (lineup == activeLineup) return;
        activeLineup = lineup;
        if (lockOn != null) lockOn.Release(); // 숨겨지는 상대에 락온이 남지 않게

        for (int i = 0; i < lineups.Count; i++)
        {
            GameObject group = lineups[i];
            if (group == null) continue;
            bool show = i == lineup;
            if (group.activeSelf == show) continue;
            group.SetActive(show);
            if (!show) continue;
            foreach (EnemySwordsman e in group.GetComponentsInChildren<EnemySwordsman>())
                ZoneArena.NotifyEnemySpawned(e.transform.position);
        }
    }

    void Add(TutorialGoal goal) => SetProgress(goal, progress + 1);

    void SetProgress(TutorialGoal goal, int value)
    {
        Lesson step = Current;
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
    }

    void Finish()
    {
        Finished = true;
        SetLineup(-1);
        if (exitBarrier != null) exitBarrier.SetActive(false);
        PlayerPrefs.SetInt(DoneKey, 1);
        guide.ShowFinish("수련 완료", "북쪽 결계가 열렸습니다. 길을 따라 나가면 산문으로 이어집니다.");
    }

    // ESC 메뉴 "SKIP TUTORIAL"
    public void SkipAll()
    {
        if (Finished) return;
        index = lessons.Count;
        doneTimer = -1f;
        Finish();
    }

    bool ReachedCenter()
    {
        if (player == null || center == null) return false;
        Vector3 d = player.position - center.position;
        d.y = 0f;
        return d.sqrMagnitude <= centerRadius * centerRadius;
    }

    string ChapterName(int chapter) =>
        chapters != null && chapter >= 0 && chapter < chapters.Length ? chapters[chapter] : "";

    string ChapterLabel(int chapter)
    {
        string name = ChapterName(chapter);
        return string.IsNullOrEmpty(name) ? "수련장" : $"수련장 · {name}";
    }

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
    void OnUltimateFinished() => Add(TutorialGoal.Ultimate); // [변경]

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
    // 상대 무리 번호: 0 허수아비 2 · 1 수련 상대 · 2 허수아비 4 (셋업이 만드는 순서)
    public static List<Lesson> DefaultLessons() => new List<Lesson>
    {
        L(0, 0, TutorialGoal.ReachCenter, 1, "수련장으로", "수련장 가운데로 걸어가세요.", "WASD 이동 · Shift 홀드 달리기"),
        L(0, 0, TutorialGoal.LightCombo, 3, "연속 베기", "허수아비를 좌클릭으로 3연속 베세요.", "좌클릭"),
        L(0, 0, TutorialGoal.HeavyInCombo, 1, "강공격 섞기", "좌클릭 콤보 중에 우클릭을 섞으면 다른 갈래로 이어집니다.", "좌클릭 → 우클릭"),
        L(0, 0, TutorialGoal.Dodge, 2, "회피", "Shift를 짧게 눌러 원하는 방향으로 구르세요.", "Shift"),
        L(0, 0, TutorialGoal.LockOn, 1, "락온", "허수아비를 시야에 두고 고정하세요. 다시 누르면 풀립니다.", "Tab / 휠 클릭"),

        L(1, 1, TutorialGoal.JustDodge, 1, "저스트 회피", "수련 상대가 나왔습니다. 공격이 닿기 직전에 회피하면 시간이 느려지고 뇌기 1칸을 얻습니다.", "Shift (타이밍)", skippable: true),
        L(1, 1, TutorialGoal.Parry, 1, "패링", "가만히 선 채로 적의 칼이 닿기 직전에 우클릭. 뇌기 1칸을 씁니다.", "우클릭 (대기 중)", fillGauge: true, skippable: true),
        L(1, 1, TutorialGoal.Kanpa, 1, "간파", "콤보 중 적이 공격하면 우클릭 강공격으로 맞받아치세요.", "좌클릭 콤보 → 우클릭", skippable: true),
        L(1, 1, TutorialGoal.Execute, 1, "처형", "패링으로 적을 무너뜨린 뒤 가까이에서 F.", "우클릭 패링 → F", fillGauge: true, skippable: true),

        L(2, 2, TutorialGoal.TapStrike, 1, "벽력일섬", "Q를 짧게 누르면 가까운 적에게 번개처럼 돌진해 벱니다. 뇌기 1칸.", "Q 탭", fillGauge: true),
        L(2, 2, TutorialGoal.StanceDraw, 3, "발도 자세", "Q를 길게 누른 채 화면 가운데 조준점을 적에게 올려 표식 3개. 떼면 차례로 벱니다.", "Q 홀드 → 조준 → 떼기", fillGauge: true),

        L(3, 2, TutorialGoal.EnchantStrike, 3, "뇌검 각성", "뇌기 2칸 이상에서 R. 각성 중엔 평타마다 낙뢰가 떨어집니다.", "R → 좌클릭", fillGauge: true),

        L(4, -1, TutorialGoal.ShrineRest, 1, "화톳불", "수련장 가장자리의 화톳불 앞에서 E. 체력이 회복되고 스킬트리가 열립니다.", "E"),
        L(4, -1, TutorialGoal.UnlockSkill, 1, "스킬 해금", "스킬 포인트로 기술을 하나 해금하세요. 노드를 고르면 미리보기 영상이 나옵니다.", "클릭 → 해금"),
        L(4, 2, TutorialGoal.Ultimate, 1, "궁극기",
            "패링 · 간파 · 저스트 회피 · 처형으로 오른쪽 아래 궁극기 게이지가 찹니다. 지금은 가득 채워 뒀어요. V를 짧게 누르면 낙뢰진, 누르고 있으면 천뢰일섬 → 뇌신강림.",
            "V 짧게 / 길게 누르고 떼기", fillGauge: true), // [변경]
    };

    static Lesson L(int chapter, int lineup, TutorialGoal goal, int count, string title, string description, string keys, bool fillGauge = false, bool skippable = false) =>
        new Lesson { chapter = chapter, lineup = lineup, goal = goal, count = count, title = title, description = description, keys = keys, fillGauge = fillGauge, skippable = skippable };
}
