using System;
using UnityEngine;

// 피해가 들어간 순간을 UI(데미지 숫자) 등에 알리는 전역 이벤트. 맞는 쪽(적·허수아비)이 호출
public static class DamageEvents
{
    // 표시 위치(머리 위), 피해 정보
    public static event Action<Vector3, DamageInfo> Dealt;

    public static void Raise(Vector3 position, DamageInfo info) => Dealt?.Invoke(position, info);

    // [변경] 숫자 대신 글자 (방패병 "막음"·"가드 붕괴!", 돌격병 "탈진")
    public static event Action<Vector3, string, Color> Labeled;
    public static readonly Color BlockColor = new Color(0.7f, 0.82f, 1f);
    public static readonly Color BreakColor = new Color(1f, 0.45f, 0.85f);
    public static readonly Color ExhaustColor = new Color(0.75f, 0.75f, 0.75f);

    public static void RaiseLabel(Vector3 position, string text, Color color) => Labeled?.Invoke(position, text, color);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        Dealt = null;
        Labeled = null;
    }
}
