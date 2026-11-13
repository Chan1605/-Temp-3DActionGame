using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 스킬 미리보기 녹화용 (수련장 씬에 배치됨)
// F10: 화면 UI 전부 숨김 + 스킬 전부 해금 + 뇌기 항상 가득 + 체력 유지. 다시 누르면 UI만 돌아옴 (해금은 유지)
// ESC 설정·스킬트리 창, 발도 자세 조준점·표식, 궁극기 연출 캔버스는 숨기지 않음
public class RecordingMode : MonoBehaviour
{
    [SerializeField] Key toggleKey = Key.F10;
    [SerializeField] bool startActive;
    [SerializeField] bool infiniteThunder = true;
    [SerializeField] bool keepHealth = true;
    [SerializeField] float rescanInterval = 0.5f;
    [SerializeField] string[] keepCanvasNames = { "DrawStanceCanvas" };

    // [변경] 궁극기 연출(레터박스·기술명·화면 베기)은 씬에 저장된 keepCanvasNames 값과 상관없이 항상 유지
    static readonly string[] AlwaysKeep = { "DrawStanceCanvas", "UltimateCanvas" };

    public static bool Active { get; private set; }

    readonly HashSet<Canvas> hidden = new HashSet<Canvas>();
    PlayerThunder thunder;
    PlayerHealth health;
    float rescanTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => Active = false;

    void Start()
    {
        thunder = FindAnyObjectByType<PlayerThunder>();
        health = FindAnyObjectByType<PlayerHealth>();
        if (startActive) SetActive(true);
    }

    void OnDisable()
    {
        if (Active) SetActive(false);
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame) SetActive(!Active);
    }

    void LateUpdate()
    {
        if (!Active) return;

        rescanTimer -= Time.unscaledDeltaTime;
        if (rescanTimer <= 0f) HideCanvases();

        if (infiniteThunder && thunder != null && thunder.Gauge < thunder.MaxGauge) thunder.Add(thunder.MaxGauge - thunder.Gauge);
        if (keepHealth && health != null && !health.IsDead && health.Health < health.MaxHealth) health.HealFull();
    }

    void SetActive(bool on)
    {
        Active = on;
        if (on)
        {
            SkillTree.UnlockAll();
            HideCanvases();
        }
        else
        {
            foreach (Canvas c in hidden)
                if (c != null) c.enabled = true;
            hidden.Clear();
        }
        Debug.Log($"[Recording] {(on ? "켜짐 · UI 숨김, 스킬 전부 해금" : "꺼짐 · UI 복구")}");
    }

    // 핵심: 메뉴 창을 뺀 최상위 캔버스를 모두 끔. 나중에 생기는 캔버스도 잡도록 주기적으로 다시 훑음
    void HideCanvases()
    {
        rescanTimer = rescanInterval;
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || !c.enabled) continue;
            if (c.GetComponentInParent<SettingsMenu>(true) != null || c.GetComponentInParent<SkillTreeMenu>(true) != null) continue;
            if (System.Array.IndexOf(keepCanvasNames, c.gameObject.name) >= 0) continue;
            if (System.Array.IndexOf(AlwaysKeep, c.gameObject.name) >= 0) continue; // [변경]
            c.enabled = false;
            hidden.Add(c);
        }
    }
}
