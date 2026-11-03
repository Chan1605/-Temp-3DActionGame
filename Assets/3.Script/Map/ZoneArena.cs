using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 구역 진행: 플레이어가 아레나 안으로 들어오면 입구 결계를 닫고, 안의 적이 모두 쓰러지면 출구 결계를 연다
// [변경] 한 씬에 구역을 이어 붙이므로, 구역에 다가가면 하늘·안개·해를 이 구역 테마로 바꿈
public class ZoneArena : MonoBehaviour
{
    enum State { Waiting, Fighting, Cleared }

    [SerializeField] float radius = 16f;
    [SerializeField] float enterMargin = 2f;
    [SerializeField] GameObject entranceBarrier;
    [SerializeField] GameObject exitBarrier;
    [SerializeField] bool logState = true;

    [Header("Atmosphere")] // [변경]
    [SerializeField] ZoneTheme theme;
    [SerializeField] float atmosphereRadius = 29f;
    [SerializeField] float atmosphereBlendTime = 1.5f;

    static ZoneArena activeAtmosphere; // [변경] 지금 분위기를 쥐고 있는 구역
    static Light sun;

    readonly List<EnemySwordsman> enemies = new List<EnemySwordsman>();
    Transform player;
    State state;

    public event Action Cleared;

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;

        // 시작 시점에 아레나 안에 있는 적만 이 구역의 웨이브로 침 (이후 웨이브 스폰은 별도 작업)
        foreach (EnemySwordsman e in FindObjectsByType<EnemySwordsman>(FindObjectsSortMode.None))
            if (FlatDistance(e.transform.position) <= radius + 2f) enemies.Add(e);

        SetActive(entranceBarrier, false);
        SetActive(exitBarrier, true);
        state = State.Waiting;

        // [변경] 시작 위치가 이 구역이면 전환 없이 바로 적용
        if (player != null && FlatDistance(player.position) <= atmosphereRadius) ApplyAtmosphere(0f);
    }

    void Update()
    {
        if (activeAtmosphere != this && player != null && FlatDistance(player.position) <= atmosphereRadius) ApplyAtmosphere(atmosphereBlendTime); // [변경]

        switch (state)
        {
            case State.Waiting:
                if (player != null && FlatDistance(player.position) <= radius - enterMargin) BeginFight();
                break;
            case State.Fighting:
                if (AllDefeated()) Clear();
                break;
        }
    }

    void BeginFight()
    {
        state = State.Fighting;
        SetActive(entranceBarrier, true);
        if (logState) Debug.Log($"[Zone] {name} 전투 시작 · 적 {enemies.Count}명");
    }

    void Clear()
    {
        state = State.Cleared;
        SetActive(exitBarrier, false);
        Cleared?.Invoke();
        if (logState) Debug.Log($"[Zone] {name} 클리어 · 출구 열림");
    }

    bool AllDefeated()
    {
        foreach (EnemySwordsman e in enemies)
            if (e != null && e.CanBeTargeted) return false;
        return true;
    }

    float FlatDistance(Vector3 position)
    {
        Vector3 d = position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    // [변경] ===== 분위기 전환 =====
    void ApplyAtmosphere(float blendTime)
    {
        if (theme == null) return;
        if (activeAtmosphere != null) activeAtmosphere.StopAllCoroutines();
        activeAtmosphere = this;
        StartCoroutine(BlendAtmosphere(blendTime));
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
