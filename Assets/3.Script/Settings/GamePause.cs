using System;
using UnityEngine;

// 일시정지 상태. 켜면 시간·소리를 멈추고, 플레이어 입력 스크립트들은 IsPaused면 Update를 건너뜀
// 풀 때는 timeScale을 1로 (멈춘 사이 히트스톱 등의 주인이 이미 복구를 끝냈을 수 있어 저장값은 믿지 않음)
public static class GamePause
{
    public static bool IsPaused { get; private set; }
    public static event Action<bool> Changed;

    public static void Set(bool paused)
    {
        if (IsPaused == paused) return;
        IsPaused = paused;
        Time.timeScale = paused ? 0f : 1f;
        AudioListener.pause = paused;
        Changed?.Invoke(paused);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        IsPaused = false;
        Changed = null;
        AudioListener.pause = false;
    }
}
