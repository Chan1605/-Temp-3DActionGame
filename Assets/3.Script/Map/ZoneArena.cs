using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 구역 진행: 플레이어가 아레나 안으로 들어오면 입구 결계를 닫고, 웨이브를 모두 처치하면 출구 결계를 연다
// 한 씬에 구역을 이어 붙이므로, 구역에 다가가면 하늘·안개·해를 이 구역 테마로 바꿈
// 웨이브 스폰 + 체크포인트: 전투 중 사망하면 입구 밖에서 부활하고 구역을 처음부터 다시 시작
public class ZoneArena : MonoBehaviour
{
    enum State { Waiting, Fighting, Cleared }

    [Serializable]
    public class Wave
    {
        public GameObject enemyPrefab;
        [Min(1)] public int count = 2;
        // 정예: 기본 적과 함께 웨이브 가운데에 세움 (0이면 없음)
        public GameObject elitePrefab;
        [Min(0)] public int eliteCount;
        // 추가 적(방패병·돌격병 등): 줄 양 끝에 번갈아 세움
        public List<Group> extras = new List<Group>();
    }

    [Serializable]
    public class Group
    {
        public GameObject prefab;
        [Min(1)] public int count = 1;
    }

    [SerializeField] float radius = 16f;
    [SerializeField] float enterMargin = 2f;
    [SerializeField] GameObject entranceBarrier;
    [SerializeField] GameObject exitBarrier;
    [SerializeField] bool logState = true;

    // 웨이브: 플레이어 반대편 벽 근처에 부채꼴로 등장, 전부 쓰러지면 waveInterval 뒤 다음 웨이브
    [Header("Waves")]
    [SerializeField] List<Wave> waves = new List<Wave>();
    [SerializeField] float firstWaveDelay = 0.8f;
    [SerializeField] float waveInterval = 2f;
    [SerializeField] float spawnRadius = 10f; // 중심에서 등장 거리 (14m → 10m, 걸어오는 공백 줄임)
    [SerializeField] float spawnHold = 0.6f;  // 등장 직후 무방비 시간
    [SerializeField] float spawnSpread = 35f;
    // 웨이브 기본 적 수 배율 + 추가 인원 (보스 웨이브는 제외). 시련도 적용, 인원이 많으면 등장 간격을 좁혀 200° 안에
    [SerializeField] float countScale = 1.5f;
    [SerializeField] int extraPerWave = 0;
    [SerializeField] float maxSpawnArc = 200f;

    // 시련 모드: 정해진 웨이브 대신 끝없이 이어짐. 웨이브마다 적 수·정예·체력·공격력·경험치가 오르고, 쓰러지면 기록을 남기고 처음부터
    [Header("Endless (시련)")]
    [SerializeField] bool endless;
    [SerializeField] GameObject endlessEnemy;
    [SerializeField] GameObject endlessElite;
    [SerializeField] int endlessBaseCount = 2;
    [SerializeField] float countPerWave = 0.5f;
    [SerializeField] int endlessMaxCount = 7;
    [SerializeField] int eliteEvery = 3;          // n웨이브마다 정예 1명 추가
    [SerializeField] float healthPerWave = 0.12f;
    [SerializeField] float damagePerWave = 0.06f;
    [SerializeField] float xpPerWave = 0.05f; // 0.1이면 시련 7웨이브 전에 Lv10 → 목표(8~10웨이브)에 맞춰 절반
    // 시련에 구역 3 적 섞기: specialFrom 웨이브부터 specialEvery마다 1명씩 늘림 (목록을 돌아가며)
    [SerializeField] GameObject[] endlessSpecials = new GameObject[0];
    [SerializeField] int specialFrom = 3;
    [SerializeField] int specialEvery = 2;

    [Header("Checkpoint")]
    [SerializeField] Transform respawnPoint;
    [SerializeField] float respawnDelay = 1.5f;
    [SerializeField] bool quickRetry; // 켜면 입구가 아니라 아레나 가장자리에서 부활 → 바로 재도전 (보스 구역)

    [Header("Atmosphere")]
    [SerializeField] ZoneTheme theme;
    [SerializeField] float atmosphereRadius = 29f;
    [SerializeField] float atmosphereBlendTime = 1.5f;

    static ZoneArena activeAtmosphere; // 지금 분위기를 쥐고 있는 구역
    static Light sun;

    readonly List<EnemySwordsman> preplaced = new List<EnemySwordsman>(); // 씬에 미리 둔 적 (첫 웨이브 앞에 처리)
    readonly List<EnemySwordsman> spawned = new List<EnemySwordsman>();   // 웨이브로 만든 적 (재시작 시 삭제)
    readonly List<EnemySwordsman> current = new List<EnemySwordsman>();   // 지금 웨이브
    Transform player;
    PlayerHealth playerHealth;
    State state;
    int waveIndex;        // 다음에 낼 웨이브 번호
    float waveTimer;      // 0보다 크면 다음 웨이브까지 대기 중
    bool respawning;
    Coroutine atmosphereRoutine;

    public event Action Cleared;
    public event Action<int> WaveStarted; // 이후 UI용 (1부터)

    // ===== HUD용 =====
    // 어느 구역인지 몰라도 구독할 수 있게 정적 이벤트로 알림
    public static event Action<ZoneArena> AnyEntered;
    public static event Action<ZoneArena> AnyCleared;
    // 연출용 (ZoneFx): 적 등장 위치 / 웨이브 마지막 적이 쓰러진 위치 · 구역 마지막 웨이브인지
    public static event Action<Vector3> AnyEnemySpawned;
    public static event Action<ZoneArena, EnemySwordsman> AnyEnemyCreated; // 생성 직후(Start 전) → 던전 중간보스 배율 · 이름
    public static event Action<ZoneArena, Vector3, bool> AnyWaveCleared;
    public static ZoneArena Fighting { get; private set; } // 지금 전투 중인 구역 (없으면 null)
    public static void NotifyEnemySpawned(Vector3 position) => AnyEnemySpawned?.Invoke(position); // 수련장 상대 등장도 같은 낙뢰 연출

    public string DisplayNumber => theme != null ? theme.displayNumber : "";
    public string DisplayName => theme != null && !string.IsNullOrEmpty(theme.displayName) ? theme.displayName : name;
    public int CurrentWave => waveIndex;
    public int WaveCount => endless ? 0 : waves.Count;
    // 시련 기록 (PlayerPrefs, 버틴 웨이브 수)
    const string BestKey = "TrialBestWave";
    public bool Endless => endless;
    // 성능: HUD가 매 프레임 읽으므로 처음 한 번만 PlayerPrefs에서 읽고 메모리 값 사용
    static int trialBest = -1;
    public static int TrialBest => trialBest >= 0 ? trialBest : trialBest = PlayerPrefs.GetInt(BestKey, 0);
    public static int LastTrialReached { get; private set; }
    public static event Action<int, bool> AnyTrialEnded; // 버틴 웨이브, 신기록 여부
    public int AliveCount
    {
        get
        {
            int n = 0;
            foreach (EnemySwordsman e in current)
                if (e != null && e.CanBeTargeted) n++;
            return n;
        }
    }

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
            if (playerHealth != null) playerHealth.Died += OnPlayerDied;
        }

        // 미리 둔 적은 이 구역 소속으로 묶고, 테스트용 자동 부활을 끔
        foreach (EnemySwordsman e in FindObjectsByType<EnemySwordsman>(FindObjectsSortMode.None))
        {
            if (FlatDistance(e.transform.position) > radius + 2f || e.IsTrainer) continue; // 수련용 적은 제외
            e.DisableRespawn();
            e.Died += OnEnemyDied;
            preplaced.Add(e);
        }

        ResetZone(false);

        // 시작 위치가 이 구역이면 전환 없이 바로 적용
        if (player != null && FlatDistance(player.position) <= atmosphereRadius) ApplyAtmosphere(0f);
    }

    void Update()
    {
        if (activeAtmosphere != this && player != null && FlatDistance(player.position) <= atmosphereRadius) ApplyAtmosphere(atmosphereBlendTime);

        switch (state)
        {
            case State.Waiting:
                if (player != null && FlatDistance(player.position) <= radius - enterMargin) BeginFight();
                break;
            case State.Fighting:
                if (!respawning) UpdateWaves();
                break;
        }
    }

    void OnDestroy()
    {
        if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
    }

    void BeginFight()
    {
        state = State.Fighting;
        Fighting = this;
        SetActive(entranceBarrier, true);

        // 미리 둔 적이 있으면 그들이 첫 상대, 없으면 잠시 뒤 1웨이브
        current.Clear();
        foreach (EnemySwordsman e in preplaced)
        {
            if (e == null) continue;
            e.Alert();
            current.Add(e);
        }
        waveTimer = current.Count == 0 && (waves.Count > 0 || endless) ? firstWaveDelay : 0f;
        if (logState) Debug.Log($"[Zone] {name} 전투 시작 · 웨이브 {waves.Count}개");
    }

    // 핵심: 지금 무리가 모두 쓰러지면 → 대기 → 다음 웨이브, 웨이브가 더 없으면 클리어
    void UpdateWaves()
    {
        if (waveTimer > 0f)
        {
            waveTimer -= Time.deltaTime;
            if (waveTimer <= 0f) SpawnWave(endless ? BuildEndlessWave(waveIndex++) : waves[waveIndex++]);
            return;
        }

        if (!AllDefeated()) return;

        if (endless || waveIndex < waves.Count) waveTimer = current.Count > 0 ? waveInterval : firstWaveDelay; // 시련은 클리어 없음
        else Clear();
    }

    // 핵심: n번째(0부터) 시련 웨이브 구성. 적 수는 천천히 늘고 상한에서 멈추며, 늘어난 자리를 정예가 채움
    Wave BuildEndlessWave(int n)
    {
        int total = Mathf.Min(endlessMaxCount, endlessBaseCount + Mathf.FloorToInt(n * countPerWave));
        int elites = endlessElite != null && eliteEvery > 0 ? Mathf.Min(total / 2, (n + 1) / eliteEvery) : 0;
        var wave = new Wave { enemyPrefab = endlessEnemy, count = Mathf.Max(1, total - elites), elitePrefab = endlessElite, eliteCount = elites };

        // 특수 적은 기본 적 자리를 대신함 (총원 유지, 기본 적은 최소 1명)
        var specials = System.Array.FindAll(endlessSpecials, p => p != null);
        int specialCount = specials.Length > 0 && specialEvery > 0 && n + 1 >= specialFrom ? 1 + (n + 1 - specialFrom) / specialEvery : 0;
        specialCount = Mathf.Min(specialCount, wave.count - 1);
        for (int i = 0; i < specialCount; i++)
        {
            wave.extras.Add(new Group { prefab = specials[(n + i) % specials.Length], count = 1 });
            wave.count--;
        }
        return wave;
    }

    // 플레이어 반대편 벽 쪽에 부채꼴로 세움 → 등 뒤에서 갑자기 나오지 않게
    // 보스 웨이브는 그대로, 나머지는 배율 + 추가
    int ScaledCount(Wave wave)
    {
        var enemy = wave.enemyPrefab.GetComponent<EnemySwordsman>();
        if (enemy != null && enemy.IsBoss) return wave.count;
        return Mathf.Max(1, Mathf.RoundToInt(wave.count * countScale) + extraPerWave);
    }

    void SpawnWave(Wave wave)
    {
        current.Clear();
        if (wave.enemyPrefab == null || wave.enemyPrefab.GetComponent<EnemySwordsman>() == null)
        {
            Debug.LogWarning($"[Zone] {name} {waveIndex}웨이브 프리팹이 비었거나 EnemySwordsman이 없습니다.");
            return;
        }

        Vector3 toPlayer = player != null ? player.position - transform.position : Vector3.forward;
        float baseAngle = Mathf.Atan2(toPlayer.z, toPlayer.x) + Mathf.PI;

        // 기본 적 사이 가운데 자리에 정예를 끼워 넣음
        var prefabs = new List<GameObject>();
        int baseCount = ScaledCount(wave);
        for (int i = 0; i < baseCount; i++) prefabs.Add(wave.enemyPrefab);
        if (wave.elitePrefab != null && wave.elitePrefab.GetComponent<EnemySwordsman>() != null)
            for (int i = 0; i < wave.eliteCount; i++) prefabs.Insert(prefabs.Count / 2, wave.elitePrefab);
        int side = 0; // 추가 적은 양 끝에 번갈아
        foreach (Group g in wave.extras)
        {
            if (g == null || g.prefab == null || g.prefab.GetComponent<EnemySwordsman>() == null) continue;
            for (int i = 0; i < g.count; i++, side++)
            {
                if (side % 2 == 0) prefabs.Add(g.prefab);
                else prefabs.Insert(0, g.prefab);
            }
        }

        int total = prefabs.Count;
        float step = Mathf.Min(spawnSpread, total > 1 ? maxSpawnArc / (total - 1) : spawnSpread) * Mathf.Deg2Rad;
        for (int i = 0; i < total; i++)
        {
            float a = baseAngle + (i - (total - 1) * 0.5f) * step;
            Vector3 pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spawnRadius;
            pos.y = GroundY(pos);

            Vector3 look = transform.position - pos;
            look.y = 0f;
            GameObject go = Instantiate(prefabs[i], pos, Quaternion.LookRotation(look.normalized, Vector3.up));
            var enemy = go.GetComponent<EnemySwordsman>();
            if (theme != null) enemy.MultiplyScaling(theme.strength); // [변경] 구역 강도 (중간보스 · 시련 배율은 아래에서 덮어씀)
            AnyEnemyCreated?.Invoke(this, enemy);
            enemy.DisableRespawn();
            enemy.MarkTemporary(); // 쓰러지면 가라앉아 사라짐 (미리 둔 적은 재시작 때 되살려야 하므로 제외)
            if (endless) // 웨이브 번호(0부터)만큼 배율
            {
                int n = waveIndex - 1;
                enemy.ApplyScaling(1f + healthPerWave * n, 1f + damagePerWave * n, 1f + xpPerWave * n);
            }
            enemy.Alert();
            enemy.HoldFor(spawnHold);
            enemy.Died += OnEnemyDied;
            AnyEnemySpawned?.Invoke(pos);
            spawned.Add(enemy);
            current.Add(enemy);
        }

        WaveStarted?.Invoke(waveIndex);
        if (logState) Debug.Log($"[Zone] {name} {waveIndex}/{waves.Count} 웨이브 · 적 {total}명 (기본 {wave.count} → {baseCount})");
    }

    float GroundY(Vector3 pos)
    {
        Ray ray = new Ray(pos + Vector3.up * 20f, Vector3.down);
        return Physics.Raycast(ray, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y + 0.05f : transform.position.y;
    }

    void Clear()
    {
        state = State.Cleared;
        SetActive(exitBarrier, false);
        if (exitBarrier == null) SetActive(entranceBarrier, false); // 출구 없는 막다른 방(던전 갈래)은 들어온 문을 다시 엶
        if (Fighting == this) Fighting = null;
        Cleared?.Invoke();
        AnyCleared?.Invoke(this);
        if (logState) Debug.Log($"[Zone] {name} 클리어 · 출구 열림");
    }

    // 지금 무리의 마지막 적이 쓰러진 순간 → 마무리 연출 신호 (웨이브 진행은 UpdateWaves가 담당)
    void OnEnemyDied(EnemySwordsman enemy)
    {
        if (state != State.Fighting || !current.Contains(enemy) || !AllDefeated()) return;
        AnyWaveCleared?.Invoke(this, enemy.transform.position, !endless && waveIndex >= waves.Count);
    }

    bool AllDefeated()
    {
        foreach (EnemySwordsman e in current)
            if (e != null && e.CanBeTargeted) return false;
        return true;
    }

    // ===== 체크포인트 =====
    void OnPlayerDied()
    {
        if (state != State.Fighting || respawning) return;
        if (endless) RecordTrial();
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        respawning = true;
        if (logState) Debug.Log($"[Zone] {name} 사망 → {respawnDelay}초 뒤 입구에서 다시 시작");
        yield return new WaitForSecondsRealtime(respawnDelay);

        ResetZone(true);
        TeleportPlayer();
        if (quickRetry) MoveInsideArena();
        if (playerHealth != null) playerHealth.Revive();
        respawning = false;
    }

    // 쓰러진 웨이브는 못 버틴 것으로 보고 직전 웨이브까지를 기록
    void RecordTrial()
    {
        int reached = Mathf.Max(0, waveIndex - 1);
        bool best = reached > TrialBest;
        if (best)
        {
            PlayerPrefs.SetInt(BestKey, reached);
            PlayerPrefs.Save();
            trialBest = reached;
        }
        LastTrialReached = reached;
        AnyTrialEnded?.Invoke(reached, best);
        if (logState) Debug.Log($"[Zone] 시련 종료 · {reached}웨이브 돌파{(best ? " (신기록)" : "")} · 최고 {TrialBest}");
    }

    [ContextMenu("Reset Trial Best")]
    void ResetTrialBest()
    {
        PlayerPrefs.DeleteKey(BestKey);
        trialBest = 0;
    }

    // 웨이브로 만든 적은 지우고, 미리 둔 적은 처음 자리로 되돌린 뒤 대기 상태로
    void ResetZone(bool resetPreplaced)
    {
        foreach (EnemySwordsman e in spawned)
            if (e != null) Destroy(e.gameObject);
        spawned.Clear();
        current.Clear();

        if (resetPreplaced)
            foreach (EnemySwordsman e in preplaced)
                if (e != null) e.ResetToHome();

        waveIndex = 0;
        waveTimer = 0f;
        SetActive(entranceBarrier, false);
        SetActive(exitBarrier, true);
        state = State.Waiting;
        if (Fighting == this) Fighting = null;
    }

    // CharacterController는 켜진 채로 위치를 바꾸면 덮어써지므로 잠깐 끄고 옮김
    void TeleportPlayer()
    {
        if (player == null || respawnPoint == null) return;

        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);
        if (cc != null) cc.enabled = true;
    }

    // 부활 지점 쪽 가장자리, 진입 판정보다 2m 안쪽에 세우고 중심을 바라보게 → 다음 프레임에 전투 시작
    void MoveInsideArena()
    {
        if (player == null) return;
        Vector3 from = respawnPoint != null ? respawnPoint.position : player.position;
        Vector3 dir = from - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : -transform.forward;
        Vector3 pos = transform.position + dir * Mathf.Max(1f, radius - enterMargin - 2f);
        if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;

        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir, Vector3.up));
        if (cc != null) cc.enabled = true;
        if (logState) Debug.Log($"[Zone] {name} 바로 재도전");
    }

    float FlatDistance(Vector3 position)
    {
        Vector3 d = position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    // ===== 분위기 전환 =====
    void ApplyAtmosphere(float blendTime)
    {
        if (theme == null) return;
        // StopAllCoroutines는 부활 코루틴까지 멈추므로 분위기 코루틴만 멈춤
        if (activeAtmosphere != null && activeAtmosphere.atmosphereRoutine != null) activeAtmosphere.StopCoroutine(activeAtmosphere.atmosphereRoutine);
        activeAtmosphere = this;
        atmosphereRoutine = StartCoroutine(BlendAtmosphere(blendTime));
        EnemySwordsman.MaxAttackers = theme != null ? Mathf.Max(1, theme.maxAttackers) : 2; // [변경] 구역별 동시 공격 수
        AnyEntered?.Invoke(this); // 구역 이름 배너
    }

    // 하늘은 섞을 수 없어서 시작할 때 교체, 안개·해는 blendTime 동안 보간
    IEnumerator BlendAtmosphere(float blendTime)
    {
        if (theme.skybox != null && RenderSettings.skybox != theme.skybox)
        {
            RenderSettings.skybox = theme.skybox;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            DynamicGI.UpdateEnvironment();
        }

        RenderSettings.fog = theme.fog;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        Light light = FindSun();

        Color fogFrom = RenderSettings.fogColor;
        float densityFrom = RenderSettings.fogDensity;
        Color sunFrom = light != null ? light.color : Color.white;
        Quaternion rotFrom = light != null ? light.transform.rotation : Quaternion.identity;
        Quaternion rotTo = Quaternion.Euler(theme.sunEuler);

        for (float t = 0f; t < blendTime; t += Time.unscaledDeltaTime)
        {
            SetAtmosphere(Mathf.SmoothStep(0f, 1f, t / blendTime), fogFrom, densityFrom, sunFrom, rotFrom, rotTo, light);
            yield return null;
        }
        SetAtmosphere(1f, fogFrom, densityFrom, sunFrom, rotFrom, rotTo, light);
        if (logState) Debug.Log($"[Zone] {name} 분위기 적용");
    }

    void SetAtmosphere(float k, Color fogFrom, float densityFrom, Color sunFrom, Quaternion rotFrom, Quaternion rotTo, Light light)
    {
        RenderSettings.fogColor = Color.Lerp(fogFrom, theme.fogColor, k);
        RenderSettings.fogDensity = Mathf.Lerp(densityFrom, theme.fogDensity, k);
        if (light == null) return;
        light.color = Color.Lerp(sunFrom, theme.sunColor, k);
        light.transform.rotation = Quaternion.Slerp(rotFrom, rotTo, k);
    }

    static Light FindSun()
    {
        if (sun != null) return sun;
        sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        return sun;
    }

    static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        const int segments = 48;
        Vector3 prev = transform.position + new Vector3(radius, 0.1f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 next = transform.position + new Vector3(Mathf.Cos(a) * radius, 0.1f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
