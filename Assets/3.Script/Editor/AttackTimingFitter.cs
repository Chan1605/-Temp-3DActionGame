using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// 공격 클립을 플레이어 모델에 프레임 단위로 재생해 칼(히트박스)의 이동 속도를 재고,
// 실제로 휘두르는 구간에 맞춰 AttackData의 hitStart / hitEnd를 정한다.
public static class AttackTimingFitter
{
    const float SampleRate = 60f;
    const float PeakRatio = 0.4f;
    const float LeadMargin = 0.02f;
    const float TailMargin = 0.03f;
    const float MinWindow = 0.06f;

    [MenuItem("Tools/Debug/Attack Timing Report")]
    static void Report() => Run(false);

    [MenuItem("Tools/Setup/Fit Attack Hit Windows")]
    static void Apply() => Run(true);

    static void Run(bool apply)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[AttackTiming] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        Animator animator = combat != null ? combat.GetComponentInChildren<Animator>() : null;
        WeaponHitbox weapon = combat != null ? combat.GetComponentInChildren<WeaponHitbox>(true) : null;
        BoxCollider box = weapon != null ? weapon.GetComponent<BoxCollider>() : null;
        if (animator == null || box == null)
        {
            Debug.LogError("[AttackTiming] 씬의 Player에서 Animator 또는 WeaponHitbox(BoxCollider)를 찾지 못했습니다.");
            return;
        }

        AttackData[] attacks = AssetDatabase.FindAssets("t:AttackData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<AttackData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(a => a != null && a.Clip != null)
            .OrderBy(a => a.name)
            .ToArray();

        var sb = new StringBuilder(apply ? "[AttackTiming] 판정 구간 적용\n" : "[AttackTiming] 리포트 (적용 안 함)\n");
        sb.AppendLine("이름 | 클립 길이 | 현재 판정 | 측정된 휘두르기 | 최고 속도 지점");

        AnimationMode.StartAnimationMode();
        try
        {
            foreach (AttackData attack in attacks)
            {
                float[] speed = SampleBladeSpeed(animator.gameObject, attack.Clip, box);
                FindSwingWindow(speed, out float start, out float end, out float peak);

                sb.AppendLine($"{attack.name} | {attack.Clip.length:F2}s | {attack.HitStart:F2}~{attack.HitEnd:F2} | {start:F2}~{end:F2} | {peak:F2}");

                if (apply) WriteWindow(attack, start, end);
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }

        if (apply) AssetDatabase.SaveAssets();
        Debug.Log(sb.ToString());
    }

    // 매 샘플마다 칼의 양 끝·중심 위치를 모델 기준으로 기록하고, 셋 중 가장 빠른 점의 속도를 사용
    static float[] SampleBladeSpeed(GameObject rig, AnimationClip clip, BoxCollider box)
    {
        int count = Mathf.Max(3, Mathf.CeilToInt(clip.length * SampleRate) + 1);
        float dt = clip.length / (count - 1);
        var speed = new float[count];
        Vector3[] last = null;

        for (int i = 0; i < count; i++)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(rig, clip, i * dt);
            AnimationMode.EndSampling();

            Vector3[] points = BladePoints(box, rig.transform);
            if (last != null)
            {
                float best = 0f;
                for (int k = 0; k < points.Length; k++)
                    best = Mathf.Max(best, (points[k] - last[k]).magnitude / dt);
                speed[i] = best;
            }
            last = points;
        }

        speed[0] = speed[1];
        return speed;
    }

    static Vector3[] BladePoints(BoxCollider box, Transform reference)
    {
        Vector3 size = box.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        Vector3 half = Vector3.zero;
        half[axis] = size[axis] * 0.5f;

        Transform t = box.transform;
        return new[]
        {
            reference.InverseTransformPoint(t.TransformPoint(box.center + half)),
            reference.InverseTransformPoint(t.TransformPoint(box.center)),
            reference.InverseTransformPoint(t.TransformPoint(box.center - half))
        };
    }

    // 최고 속도 지점에서 앞뒤로 최고 속도의 40% 이상인 구간을 휘두르기로 본다
    static void FindSwingWindow(float[] speed, out float start, out float end, out float peak)
    {
        int n = speed.Length;
        var smooth = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0f;
            int c = 0;
            for (int k = Mathf.Max(0, i - 2); k <= Mathf.Min(n - 1, i + 2); k++)
            {
                sum += speed[k];
                c++;
            }
            smooth[i] = sum / c;
        }

        int peakIndex = 0;
        for (int i = 1; i < n; i++)
            if (smooth[i] > smooth[peakIndex]) peakIndex = i;

        float threshold = smooth[peakIndex] * PeakRatio;
        int a = peakIndex;
        while (a > 0 && smooth[a - 1] >= threshold) a--;
        int b = peakIndex;
        while (b < n - 1 && smooth[b + 1] >= threshold) b++;

        float last = n - 1;
        peak = peakIndex / last;
        start = Mathf.Clamp01(a / last - LeadMargin);
        end = Mathf.Clamp01(b / last + TailMargin);
        if (end - start < MinWindow) end = Mathf.Min(1f, start + MinWindow);
    }

    static void WriteWindow(AttackData attack, float start, float end)
    {
        var so = new SerializedObject(attack);
        so.FindProperty("hitStart").floatValue = start;
        so.FindProperty("hitEnd").floatValue = end;

        // 입력 버퍼가 판정보다 늦게 열리면 다음 타 입력이 씹히므로 판정 시작 이전으로 당김
        SerializedProperty inputOpen = so.FindProperty("inputOpen");
        if (inputOpen.floatValue > start) inputOpen.floatValue = start;

        so.ApplyModifiedProperties();
    }
}
