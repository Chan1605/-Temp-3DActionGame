using System.Collections.Generic;
using SickscoreGames.HUDNavigationSystem;
using UnityEngine;
using UnityEngine.UI;

// 던전(뇌운 협곡) 진행 — Tools/Setup/Dungeon Scene이 배치
//  가운데 허브에서 동 · 서 · 북 갈래 방(각자 ZoneArena)을 원하는 순서로 깸 → 갈래마다 스킬 포인트
//  셋 다 깨면 허브 구역(ZoneArena, 처음엔 꺼 둠)이 깨어나 수문장(중간보스) 전투 → 이기면 남쪽 출구(본편 보스 구역 입구로)
//  [변경] 목표 안내: 오른쪽 위에 남은 목표 + 거리, 나침반 · 화면 밖 화살표에도 같은 목표 표식 (클리어한 갈래는 빠짐)
public class DungeonDirector : MonoBehaviour
{
    public const string ClearedFlag = "DungeonCleared";

    [SerializeField] List<ZoneArena> branches = new List<ZoneArena>();
    [SerializeField] ZoneArena hub;
    [SerializeField] GameObject exitMarker;       // 남쪽 출구(SceneExit). 수문장을 이기면 켬
    [SerializeField] GameObject miniBossPrefab;   // 허브 웨이브의 정예 = 수문장
    [SerializeField] string miniBossName = "뇌운의 수문장";
    [SerializeField] Vector3 miniBossScale = new Vector3(5f, 1.25f, 6f); // 체력 · 공격력 · 경험치 배율
    [SerializeField] int pointsPerBranch = 1;
    [SerializeField] Font font;
    [SerializeField] HUDNavigationElementSettings markerSettings; // 본편 구역 목표와 같은 설정 (Nav_Objective)
    [SerializeField] float refreshInterval = 0.2f;
    [SerializeField] float roomRadius = 21f; // [변경] 이 안에 들어오면 그 방에 온 것으로 봄 (벽 반지름 19 + 여유)

    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);

    readonly HashSet<ZoneArena> done = new HashSet<ZoneArena>();
    readonly Dictionary<ZoneArena, GameObject> markers = new Dictionary<ZoneArena, GameObject>();
    GameObject exitNav;
    Transform player;
    float refreshTimer;
    Text objective, banner;
    CanvasGroup bannerGroup;
    float bannerTimer;

    void Start()
    {
        if (hub != null) hub.enabled = false; // 봉인을 다 풀기 전엔 허브에서 전투가 시작되지 않음
        if (exitMarker != null) exitMarker.SetActive(false);
        foreach (ZoneArena b in branches)
            if (b != null) b.Cleared += () => OnBranchCleared(b);
        if (hub != null) hub.Cleared += OnHubCleared;
        ZoneArena.AnyEnemyCreated += OnEnemyCreated;

        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;
        foreach (ZoneArena b in branches)
            if (b != null) markers[b] = CreateMarker($"Nav_{b.DisplayName}", b.transform.position);
        if (hub != null) markers[hub] = CreateMarker("Nav_Hub", hub.transform.position);
        if (exitMarker != null) exitNav = CreateMarker("Nav_Exit", exitMarker.transform.position);

        BuildUi();
        UpdateObjective();
    }

    // 나침반 · 화면 밖 화살표용 표식. 꺼 둔 채 만들고 설정을 넣은 뒤 켜야 표식이 제대로 생김
    GameObject CreateMarker(string name, Vector3 position)
    {
        if (markerSettings == null) return null;
        var go = new GameObject(name);
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        go.transform.position = position + Vector3.up * 2.5f;
        go.AddComponent<HUDNavigationElement>().Settings = markerSettings;
        go.AddComponent<NavTarget>().Color = Gold;
        return go;
    }

    void OnDestroy() => ZoneArena.AnyEnemyCreated -= OnEnemyCreated;

    void OnBranchCleared(ZoneArena branch)
    {
        if (!done.Add(branch)) return;
        if (SkillTree.Active && pointsPerBranch > 0) SkillTree.AddPoints(pointsPerBranch);
        UpdateObjective();

        if (done.Count < branches.Count)
        {
            ShowBanner($"봉인 해제  {done.Count} / {branches.Count}   ·   스킬 포인트 +{pointsPerBranch}");
            return;
        }
        if (hub != null) hub.enabled = true; // 핵심: 마지막 봉인 → 허브가 전투 구역으로 깨어남
        ShowBanner("모든 봉인이 풀렸다 — 협곡 중앙으로");
    }

    // 허브 웨이브에서 정예가 생기는 순간(Start 전) 수문장으로 만듦
    void OnEnemyCreated(ZoneArena arena, EnemySwordsman enemy)
    {
        if (arena != hub || enemy == null || miniBossPrefab == null) return;
        if (!enemy.name.StartsWith(miniBossPrefab.name)) return;
        enemy.ApplyScaling(miniBossScale.x, miniBossScale.y, miniBossScale.z);
        enemy.ShowAsBoss(miniBossName);
    }

    void OnHubCleared()
    {
        RunState.SetFlag(ClearedFlag);
        if (exitMarker != null) exitMarker.SetActive(true);
        UpdateObjective();
        ShowBanner($"{miniBossName} 격파 — 남쪽 길이 열렸다");
    }

    // 핵심: 지금 갈 수 있는 목표만 보여 줌 (깬 갈래는 빼고, 남은 곳은 거리와 함께)
    // [변경] 아직 안 깬 갈래 방에 들어와 있거나 어디서든 웨이브 중이면 그곳만 보여 주고 다른 갈림길 표식은 숨김
    void UpdateObjective()
    {
        if (objective == null) return;
        bool finished = hub != null && RunState.HasFlag(ClearedFlag);
        bool hubOpen = done.Count >= branches.Count && !finished;
        ZoneArena fighting = ZoneArena.Fighting;
        ZoneArena focus = fighting != null ? fighting : CurrentRoom();

        var lines = new System.Text.StringBuilder();
        if (finished)
        {
            lines.Append("남쪽 출구로 나아가라");
            if (exitMarker != null) lines.Append($"   {Distance(exitMarker.transform.position)}");
        }
        else if (hubOpen)
        {
            lines.Append($"협곡 중앙의 {miniBossName}을 쓰러뜨려라");
            if (fighting != hub) lines.Append($"   {Distance(hub.transform.position)}");
        }
        else if (focus != null) // [변경] 그 방의 목표만
        {
            lines.Append($"봉인 해제  {done.Count} / {branches.Count}");
            lines.Append(focus == fighting ? $"\n{focus.DisplayName}   전투 중" : $"\n{focus.DisplayName}");
        }
        else
        {
            lines.Append($"봉인 해제  {done.Count} / {branches.Count}");
            foreach (ZoneArena b in branches)
                if (b != null && !done.Contains(b))
                    lines.Append($"\n{b.DisplayName}   {Distance(b.transform.position)}");
        }
        objective.text = lines.ToString();

        foreach (KeyValuePair<ZoneArena, GameObject> pair in markers)
        {
            if (pair.Value == null) continue;
            bool show = pair.Key == hub ? hubOpen : !done.Contains(pair.Key) && !finished && !hubOpen;
            show &= focus == null; // [변경] 방 안 · 웨이브 중엔 나침반 · 화살표 표식 전부 숨김
            if (pair.Value.activeSelf != show) pair.Value.SetActive(show);
        }
        if (exitNav != null && exitNav.activeSelf != finished) exitNav.SetActive(finished);
    }

    // [변경] 아직 안 깬 갈래 방 안에 서 있으면 그 방
    ZoneArena CurrentRoom()
    {
        if (player == null) return null;
        foreach (ZoneArena b in branches)
        {
            if (b == null || done.Contains(b)) continue;
            Vector3 d = player.position - b.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude <= roomRadius * roomRadius) return b;
        }
        return null;
    }

    string Distance(Vector3 target)
    {
        if (player == null) return "";
        Vector3 d = target - player.position;
        d.y = 0f;
        return $"{Mathf.RoundToInt(d.magnitude)}m";
    }

    void ShowBanner(string text)
    {
        banner.text = text;
        bannerTimer = 3f;
    }

    void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f) // 거리 · 전투 중 표시 갱신
        {
            refreshTimer = refreshInterval;
            UpdateObjective();
        }
        if (bannerTimer > 0f) bannerTimer -= Time.unscaledDeltaTime;
        float t = bannerTimer;
        bannerGroup.alpha = t <= 0f ? 0f : Mathf.Clamp01(Mathf.Min(t / 0.6f, (3f - t) / 0.25f));
    }

    void BuildUi()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "DungeonUi", 25);
        objective = UIKit.Text(canvas.transform, "", font, 26, Gold, TextAnchor.UpperRight);
        objective.lineSpacing = 1.25f; // 여러 줄 (남은 갈래 목록)
        RectTransform o = objective.rectTransform;
        o.anchorMin = o.anchorMax = new Vector2(1f, 1f);
        o.pivot = new Vector2(1f, 1f);
        o.anchoredPosition = new Vector2(-48f, -160f);
        o.sizeDelta = new Vector2(900f, 160f);
        objective.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

        banner = UIKit.Text(canvas.transform, "", font, 44, Color.white, TextAnchor.MiddleCenter);
        RectTransform b = banner.rectTransform;
        b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.66f);
        b.sizeDelta = new Vector2(1500f, 70f);
        banner.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
        bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 0f;
    }
}
