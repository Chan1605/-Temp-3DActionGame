using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;


[CreateAssetMenu(menuName = "Game/Skill Tree Data", fileName = "SkillTree")]
public class SkillTreeData : ScriptableObject
{
    [Serializable]
    public class Node
    {
        public SkillId id;
        public string displayName;
        [TextArea(2, 4)] public string description;
        public int cost = 1;
        public int tab;              // 0 기술 · 1 수련(패시브) · 2 궁극기
        public int branch;           // 기술: 0 일섬 · 1 검술 · 2 방어 / 수련: 0 체력 · 1 뇌기 / 궁극기: 0
        public SkillId[] requires = Array.Empty<SkillId>();
        public VideoClip preview;
    }

    public static readonly string[] TabNames = { "기술", "수련", "궁극기" }; // [변경] 궁극기 탭
    public static readonly string[][] BranchNames = { new[] { "일섬", "검술", "방어" }, new[] { "체력", "뇌기" }, new[] { "궁극기 (V)" } };

    public int startPoints = 1;
    public int pointsPerLevel = 1;
    public List<Node> nodes = Defaults();

    public static readonly SkillId[] Removed = { SkillId.Launcher }; // 공중 콤보 삭제

    public static List<Node> Defaults() => new List<Node>
    {
        N(SkillId.ChainStrike1, "연격 Ⅰ", "탭 Q 돌진 뒤 착지하자마자 Q를 다시 누르면 가까운 적에게 이어서 벤다. (2연)\n이어 벨 때마다 뇌기 0.5칸", 1, 0),
        N(SkillId.ChainStrike2, "연격 Ⅱ", "연격을 3연까지 잇는다.", 2, 0, SkillId.ChainStrike1),
        N(SkillId.ChainStrike3, "연격 Ⅲ", "연격을 4연까지 잇는다.", 2, 0, SkillId.ChainStrike2),
        N(SkillId.StanceMarks, "표식 확장", "발도 자세에서 찍을 수 있는 표식이 3명에서 6명으로 늘어난다.", 1, 0),
        N(SkillId.EnchantDraw, "각성 발도", "각성(R) 중 Q: 각성 시간을 떼어 그동안 비용 없이 발도 자세를 연달아 쓴다.", 2, 0, SkillId.StanceMarks),
        N(SkillId.ChargeLevel3, "차지 강타 Ⅲ", "우클릭 차지가 3단계까지 차오르고, 놓으면 멀리 돌진해 베는 섬격이 나간다.", 1, 1),
        N(SkillId.KanpaSmash, "뇌락", "역뢰 성공 직후 우클릭: 뛰어올라 내려찍어 주변을 경직시키고 낙뢰를 떨어뜨린다.", 1, 2), // 용어 교체 (간파 → 역뢰)
        N(SkillId.DodgeDraw, "회피 발도", "저스트 회피 직후 Q: 뇌기 없이 가까운 적을 자동으로 표식해 곧바로 발도한다.", 2, 2, SkillId.KanpaSmash),
        // 수련 탭 (패시브)
        P(SkillId.Vitality1, "체력 단련 Ⅰ", "최대 체력이 25 늘어난다.", 1, 0),
        P(SkillId.Vitality2, "체력 단련 Ⅱ", "최대 체력이 25 더 늘어난다.", 2, 0, SkillId.Vitality1),
        P(SkillId.ThunderSlot1, "뇌기 확장 Ⅰ", "뇌기 칸이 하나 늘어난다. 일섬 · 발도 · 패링을 더 자주 쓸 수 있다.", 2, 1),
        P(SkillId.ThunderSlot2, "뇌기 확장 Ⅱ", "뇌기 칸이 하나 더 늘어난다.", 2, 1, SkillId.ThunderSlot1),
        // [변경] 궁극기 탭: 게이지는 패링 · 역뢰 · 저스트 회피 · 처형으로 참. V를 누르고 있으면 해금한 단계까지 고를 수 있음
        U(SkillId.UltimateStorm, "뇌운낙뢰진", "게이지 1칸 · 기본 기술\n칼을 땅에 꽂아 3초 동안 주변 11m에 낙뢰를 퍼붓고, 마지막에 가운데로 큰 낙뢰를 떨어뜨린다.\n낙뢰 한 번 22 피해 (보스 60%) · 적이 적을수록 한 명에게 몰린다.", 0),
        U(SkillId.UltimateHeavens, "천뢰일섬", "게이지 2칸 · V를 누르고 있다가 2칸에서 뗀다\n멈춘 흑백 세상에서 앞쪽 14m를 단 한 번 베고, 납도하는 순간 화면이 갈라지며 낙뢰.\n일반 적 즉사 · 보스 130 피해 + 체간.", 2, SkillId.UltimateStorm),
        U(SkillId.UltimateDescent, "뇌신강림", "게이지 3칸 · V를 누르고 있다가 3칸에서 뗀다\n칼끝 낙뢰 5연타 뒤 주변 적을 깜빡이며 베고, 느린 납도와 함께 일제히 낙뢰.\n일반 적 즉사 · 보스 288 피해 + 큰 체간.", 3, SkillId.UltimateHeavens),
    };

    static Node U(SkillId id, string name, string desc, int cost, params SkillId[] requires)
    {
        Node node = N(id, name, desc, cost, 0, requires);
        node.tab = 2;
        return node;
    }

    static Node P(SkillId id, string name, string desc, int cost, int branch, params SkillId[] requires)
    {
        Node node = N(id, name, desc, cost, branch, requires);
        node.tab = 1;
        return node;
    }

    // 예전에 만든 에셋에 없는 기본 노드를 뒤에 덧붙임 (새 노드를 추가해도 에셋을 지울 필요 없음)
    public static List<Node> WithMissingDefaults(List<Node> nodes)
    {
        var result = new List<Node>(nodes);
        result.RemoveAll(n => Array.IndexOf(Removed, n.id) >= 0); // 삭제된 기술은 예전 에셋에 남아 있어도 숨김
        foreach (Node d in Defaults())
            if (!result.Exists(n => n.id == d.id)) result.Add(d);
        return result;
    }

    static Node N(SkillId id, string name, string desc, int cost, int branch, params SkillId[] requires) =>
        new Node { id = id, displayName = name, description = desc, cost = cost, branch = branch, requires = requires };
}
