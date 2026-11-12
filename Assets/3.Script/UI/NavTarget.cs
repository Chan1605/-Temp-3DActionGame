using SickscoreGames.HUDNavigationSystem;
using UnityEngine;

// HUD Navigation System 표식 보조: 아이콘 색을 정하고, 적이면 쓰러졌을 때 표식을 끔 (다시 살아나면 켬)
// 전투 코드는 내비게이션 에셋을 모르게, 표식 쪽이 적 상태를 읽기만 함
[RequireComponent(typeof(HUDNavigationElement))]
public class NavTarget : MonoBehaviour
{
    [SerializeField] Color color = new Color(1f, 0.3f, 0.25f);

    HUDNavigationElement element;

    public Color Color { get => color; set => color = value; } // [변경] 실행 중에 만드는 목표 표식(던전)용 — 켜기 전에 정함
    EnemySwordsman enemy;

    void Awake()
    {
        element = GetComponent<HUDNavigationElement>();
        enemy = GetComponentInParent<EnemySwordsman>();
        element.OnElementReady.AddListener(Tint); // 표식은 켤 때마다 새로 만들어지므로 매번 색을 입힘
    }

    void OnDestroy()
    {
        if (element != null) element.OnElementReady.RemoveListener(Tint);
    }

    void Update()
    {
        if (enemy == null) return;
        bool alive = enemy.CanBeTargeted;
        if (element.enabled != alive) element.enabled = alive;
    }

    void Tint(HUDNavigationElement e)
    {
        if (e.Radar != null) e.Radar.ChangeIconColor(color);
        if (e.CompassBar != null) e.CompassBar.ChangeIconColor(color);
        if (e.Minimap != null) e.Minimap.ChangeIconColor(color);
        if (e.Indicator != null)
        {
            e.Indicator.ChangeIconColor(color);
            e.Indicator.ChangeOffscreenIconColor(color);
        }
    }
}
