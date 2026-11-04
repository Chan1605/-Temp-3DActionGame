using System;
using UnityEngine;

// 피해가 들어간 순간을 UI(데미지 숫자) 등에 알리는 전역 이벤트. 맞는 쪽(적·허수아비)이 호출
public static class DamageEvents
{
    // 표시 위치(머리 위), 피해 정보
    public static event Action<Vector3, DamageInfo> Dealt;

    public static void Raise(Vector3 position, DamageInfo info) => Dealt?.Invoke(position, info);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => Dealt = null;
}
