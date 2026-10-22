using System;
using UnityEngine;

namespace MidnightMouse
{
    /// <summary>밤의 종류별 단계 구성과 부탁·문구를 보관하는 데이터 에셋.</summary>
    [CreateAssetMenu(menuName = "한밤의 쥐/밤")]
    public sealed class NightDefinition : ScriptableObject
    {
        [Serializable]
        public struct Roster
        {
            public CatDefinition kind;
            public int[] counts;
        }
        [SerializeField, Tooltip("저장용 이름 ID. 순서가 바뀌어도 유지.")] private string _id;
        [SerializeField, Tooltip("밤 선택 화면 이름.")] private string _title;
        [SerializeField, Range(1, 7), Tooltip("캠페인 표시 순서. 저장 ID와 독립.")] private int _order = 1;
        [SerializeField, Min(1), Tooltip("단계 길이(초). 길수록 한 판 증가. 1일째 45, 이후 60.")] private float _stageSeconds = 60;
        [SerializeField, Tooltip("종류별 다섯 단계 구성. 수는 줄지 않으며 합계 최대 7.")] private Roster[] _roster;
        [SerializeField, Tooltip("이번 밤에 청소기 사용 가능.")] private bool _vacuum;
        [SerializeField, Min(0), Tooltip("미끼 내성(초). 높을수록 재유인 어려움. 1일째 0, 이후 30.")] private float _baitImmunity = 30;
        [SerializeField, Min(0.1f), Tooltip("추적 속도 배율. 기본 1, 보름달 1.1.")] private float _chaseMultiplier = 1;
        [SerializeField, Min(1), Tooltip("세 번째 별에 필요한 퇴장 수. 초기 1일째 3, 2일째 4.")] private int _banishTarget = 4;
        [SerializeField, Tooltip("부탁을 판정할 함정 종류.")] private TrapKind _requestTrap;
        [SerializeField, Tooltip("부탁 이름.")] private string _requestName;
        [SerializeField, Tooltip("자장가 부탁: 미끼로 잠든 게으름냥을 맞힌 횟수를 센다.")] private bool _lullabyRequest;
        [SerializeField, Min(1), Tooltip("한 작동에서 맞혀야 하는 서로 다른 고양이 수. 초기 2.")] private int _requestHits = 2;
        [SerializeField, TextArea, Tooltip("처음 소개할 요소 문구.")] private string _introduction;
        [SerializeField, Tooltip("다섯 단계 문구.")] private string[] _stageLines;
        public string Id => _id;
        public string Title => _title;
        public int Order => _order;
        public float StageSeconds => _stageSeconds;
        public float Duration => _stageSeconds * 5;
        public Roster[] Cats => _roster;
        public bool Vacuum => _vacuum;
        public float BaitImmunity => _baitImmunity;
        public float ChaseMultiplier => _chaseMultiplier;
        public int BanishTarget => _banishTarget;
        public TrapKind RequestTrap => _requestTrap;
        public string RequestName => _requestName;
        public bool LullabyRequest => _lullabyRequest;
        public int RequestHits => _requestHits;
        public string Introduction => _introduction;

        /// <summary>게임 시계로 단계 문구를 고른다.</summary>
        public string StageLine(float elapsed) => _stageLines[Mathf.Clamp(Mathf.FloorToInt(elapsed / _stageSeconds), 0, 4)];

        /// <summary>해당 종류의 현재 단계 목표 마릿수를 구한다.</summary>
        public int Count(CatDefinition kind, float elapsed)
        {
            int stage = Mathf.Clamp(Mathf.FloorToInt(elapsed / _stageSeconds), 0, 4);
            foreach (var entry in _roster) if (entry.kind == kind) return entry.counts[stage];
            return 0;
        }

        /// <summary>슬롯 상한과 종류별 단조 증가·필수 문구를 검증한다.</summary>
        public bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(_id) || string.IsNullOrWhiteSpace(_title) || _order < 1 || _order > 7 ||
                !float.IsFinite(_chaseMultiplier) || _chaseMultiplier <= 0 || !float.IsFinite(_baitImmunity) || _baitImmunity < 0 ||
                _banishTarget < 1 || _requestHits < 1 || _requestHits > 7 || string.IsNullOrWhiteSpace(_requestName) || _stageSeconds <= 0 || !float.IsFinite(_stageSeconds) ||
                _roster == null || _roster.Length == 0 || _stageLines == null || _stageLines.Length != 5) return false;
            var totals = new int[5];
            for (int index = 0; index < _roster.Length; index++)
            {
                var entry = _roster[index];
                if (entry.kind == null || !entry.kind.IsValid() || entry.counts == null || entry.counts.Length != 5) return false;
                for (int earlier = 0; earlier < index; earlier++) if (_roster[earlier].kind == entry.kind) return false;
                for (int stage = 0; stage < 5; stage++)
                {
                    if (entry.counts[stage] < 0 || (stage > 0 && entry.counts[stage] < entry.counts[stage - 1])) return false;
                    totals[stage] += entry.counts[stage];
                    if (totals[stage] > 7 || string.IsNullOrWhiteSpace(_stageLines[stage])) return false;
                }
            }
            return true;
        }
    }
}
