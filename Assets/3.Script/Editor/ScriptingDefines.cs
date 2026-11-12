using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// 에디터가 켜질 때(스크립트 컴파일 뒤) 필요한 스크립팅 심볼을 PC(Standalone)에 자동으로 추가
//  TMP_PRESENT: HUD Navigation System이 TextMeshPro 글자를 고칠 때 이 심볼로 확인함
//               없으면 나침반 · 화면 밖 화살표의 거리 글자가 프리팹 기본값(1234m) 그대로 멈춤 (Unity 6은 TMP가 기본 포함)
[InitializeOnLoad]
static class ScriptingDefines
{
    static readonly string[] Required = { "TMP_PRESENT" };

    static ScriptingDefines()
    {
        NamedBuildTarget target = NamedBuildTarget.Standalone;
        string current = PlayerSettings.GetScriptingDefineSymbols(target);
        string updated = current;
        foreach (string symbol in Required)
            if (System.Array.IndexOf(updated.Split(';'), symbol) < 0)
                updated = string.IsNullOrEmpty(updated) ? symbol : updated + ";" + symbol;
        if (updated == current) return;

        PlayerSettings.SetScriptingDefineSymbols(target, updated); // 바꾸면 스크립트를 한 번 다시 컴파일함
        Debug.Log($"[Defines] PC 스크립팅 심볼 추가: {updated}");
    }
}
