using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 게임 안에서 메뉴(설정 · 스킬트리)가 열려 있는 동안 다른 UI 캔버스를 잠시 꺼서 겹치지 않게 함
// 메뉴마다 Show(owner, 자기 캔버스) / Hide(owner). 열린 메뉴가 하나도 없을 때 꺼 둔 캔버스를 되살림
public static class MenuOverlay
{
    static readonly Dictionary<Object, Canvas> owners = new Dictionary<Object, Canvas>();
    static readonly List<Canvas> hidden = new List<Canvas>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        owners.Clear();
        hidden.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // 메뉴를 연 채로 씬이 바뀌면(타이틀로 나가기 등) 이전 기록은 버림
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        owners.Clear();
        hidden.Clear();
    }

    public static void Show(Object owner, Canvas menuCanvas)
    {
        if (owner == null || owners.ContainsKey(owner)) return;
        owners.Add(owner, menuCanvas);

        // 핵심: 켜져 있는 최상위 캔버스 중 열린 메뉴들 것이 아닌 것만 끄고 기억 (원래 꺼져 있던 건 건드리지 않음)
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || !c.enabled || owners.ContainsValue(c)) continue;
            c.enabled = false;
            hidden.Add(c);
        }
    }

    public static void Hide(Object owner)
    {
        if (owner == null || !owners.Remove(owner) || owners.Count > 0) return;
        foreach (Canvas c in hidden)
            if (c != null) c.enabled = true;
        hidden.Clear();
    }
}
