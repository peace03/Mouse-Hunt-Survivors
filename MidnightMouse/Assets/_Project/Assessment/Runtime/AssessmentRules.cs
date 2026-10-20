using UnityEngine;

namespace MidnightMouse
{
    /// <summary>평가용 스테이지의 고양이·함정·보상 공통 수치를 저장하는 에셋.</summary>
    [CreateAssetMenu(menuName = "한밤의 쥐/평가용 게임 규칙")]
    public sealed class AssessmentRules : ScriptableObject
    {
        [Header("판과 성장")]
        [SerializeField, Range(30f, 600f), Tooltip("생존 목표(초). 올리면 한 판이 길어짐. 권장 300.")] private float _duration = 300f;
        [SerializeField, Range(1, 5), Tooltip("시작 하트(개). 올리면 실수 허용 증가. 권장 3.")] private int _startingHearts = 3;
        [SerializeField, Range(3, 8), Tooltip("최대 하트 상한(개). 올리면 체력 성장 여유 증가. 권장 5.")] private int _heartCap = 5;
        [SerializeField, Range(0.2f, 4f), Tooltip("피격 무적(초). 올리면 연속 피격 감소. 권장 1.5.")] private float _invulnerability = 1.5f;
        [SerializeField, Range(1, 12), Tooltip("첫 레벨 필요 경험치(점). 올리면 초기 성장 지연. 권장 4.")] private int _baseExperience = 4;
        [SerializeField, Range(0, 6), Tooltip("레벨당 필요 경험치 증가(점). 올리면 후반 성장 지연. 권장 2.")] private int _experienceStep = 2;
        [SerializeField, Range(20f, 120f), Tooltip("고양이 증원 단계 간격(초). 올리면 난도 상승 지연. 권장 60.")] private float _stageDuration = 60f;
        [SerializeField, Tooltip("단계별 고양이 수(마리). 올리면 추격 난도 증가. 권장 2, 3, 4, 6, 7.")] private int[] _catsPerStage = { 2, 3, 4, 6, 7 };
        [SerializeField, Range(0.5f, 8f), Tooltip("동일 단계의 추가 입장 간격(초). 올리면 증원이 분산됨. 권장 3.")] private float _reinforcementInterval = 3f;

        [Header("호기심냥")]
        [SerializeField, Range(1, 8), Tooltip("고양이 체력(점). 올리면 더 많은 함정 필요. 권장 3.")] private int _catHearts = 3;
        [SerializeField, Range(1f, 5f), Tooltip("순찰 속도(m/s). 올리면 순찰이 빨라짐. 권장 2.5.")] private float _patrolSpeed = 2.5f;
        [SerializeField, Range(1f, 6f), Tooltip("소음 조사 속도(m/s). 올리면 유인 반응이 빨라짐. 권장 3.5.")] private float _investigateSpeed = 3.5f;
        [SerializeField, Range(1f, 8f), Tooltip("추적 속도(m/s). 올리면 도주 난도 증가. 권장 4.5.")] private float _chaseSpeed = 4.5f;
        [SerializeField, Range(1f, 10f), Tooltip("퇴장 속도(m/s). 올리면 퇴장 연출 단축. 권장 7.")] private float _retreatSpeed = 7f;
        [SerializeField, Range(0.5f, 2f), Tooltip("소음 감지 배율(배). 올리면 멀리서도 소음을 들음. 권장 1.3.")] private float _hearingMultiplier = 1.3f;
        [SerializeField, Range(0.5f, 4f), Tooltip("근접 감지 거리(m). 올리면 가까운 쥐를 쉽게 추적. 권장 2.")] private float _detectRadius = 2f;
        [SerializeField, Range(3f, 12f), Tooltip("추적 놓침 거리(m). 올리면 추적이 오래 유지됨. 권장 6.")] private float _loseDistance = 6f;
        [SerializeField, Range(1f, 8f), Tooltip("새 소음 없는 추적 유지(초). 올리면 추적이 오래 유지됨. 권장 4.")] private float _loseTime = 4f;
        [SerializeField, Range(0.3f, 4f), Tooltip("조사 도착 후 대기(초). 올리면 함정 유인 여유 증가. 권장 2.")] private float _investigateWait = 2f;
        [SerializeField, Range(0.4f, 1.5f), Tooltip("잡기 판정 거리(m). 올리면 피격 범위 증가. 권장 0.8.")] private float _catchRadius = 0.8f;
        [SerializeField, Range(0.2f, 3f), Tooltip("잡은 후 멈춤(초). 올리면 탈출 여유 증가. 권장 1.")] private float _catchRest = 1f;
        [SerializeField, Range(3f, 40f), Tooltip("퇴장 뒤 재입장 대기(초). 올리면 보상성 휴식 증가. 권장 20.")] private float _respawnDelay = 20f;

        [Header("함정")]
        [SerializeField, Range(0.5f, 3f), Tooltip("함정 상호작용 거리(m). 올리면 멀리서 E 사용 가능. 권장 1.2.")] private float _interactionRadius = 1.2f;
        [SerializeField, Range(2f, 8f), Tooltip("함정 범위 표시 거리(m). 올리면 더 멀리서 범위를 봄. 권장 4.")] private float _trapPreviewDistance = 4f;
        [SerializeField, Range(0.1f, 2f), Tooltip("컵 낙하 지연(초). 올리면 정확한 예측 필요. 권장 0.5.")] private float _cupDelay = 0.5f;
        [SerializeField, Range(1f, 5f), Tooltip("컵 타격 반경(m). 올리면 여러 고양이를 맞힘. 권장 3.")] private float _cupRadius = 3f;
        [SerializeField, Range(1, 4), Tooltip("컵 피해(점). 올리면 쉽게 퇴장시킴. 권장 2.")] private int _cupDamage = 2;
        [SerializeField, Range(1f, 8f), Tooltip("컵 기절(초). 올리면 후속 함정 여유 증가. 권장 4.")] private float _cupStun = 4f;
        [SerializeField, Range(3f, 40f), Tooltip("컵 재사용 대기(초). 올리면 사용 빈도 감소. 권장 20.")] private float _cupCooldown = 20f;
        [SerializeField, Range(2f, 16f), Tooltip("청소기 작동(초). 올리면 긴 경로를 청소함. 권장 8.")] private float _vacuumDuration = 8f;
        [SerializeField, Range(1f, 6f), Tooltip("청소기 이동 속도(m/s). 올리면 경로 이동이 빨라짐. 권장 3.")] private float _vacuumSpeed = 3f;
        [SerializeField, Range(0.5f, 2f), Tooltip("청소기 타격 반경(m). 올리면 경로 주변도 타격. 권장 1.1.")] private float _vacuumRadius = 1.1f;
        [SerializeField, Range(1, 4), Tooltip("청소기 피해(점). 올리면 쉽게 퇴장시킴. 권장 2.")] private int _vacuumDamage = 2;
        [SerializeField, Range(0.5f, 4f), Tooltip("청소기 밀기 거리(m). 올리면 고양이가 멀리 밀림. 권장 2.")] private float _vacuumPush = 2f;
        [SerializeField, Range(0.5f, 6f), Tooltip("청소기 기절(초). 올리면 도주 여유 증가. 권장 2.")] private float _vacuumStun = 2f;
        [SerializeField, Range(10f, 45f), Tooltip("청소기 재사용 대기(초). 올리면 사용 빈도 감소. 권장 25.")] private float _vacuumCooldown = 25f;

        [Header("치즈와 미끼")]
        [SerializeField, Range(1f, 15f), Tooltip("부스러기 생성 간격(초). 올리면 경험치 공급 감소. 권장 6.")] private float _cheeseInterval = 6f;
        [SerializeField, Range(1, 20), Tooltip("동시 부스러기 제한(개). 올리면 바닥 보상 증가. 권장 10.")] private int _maxCrumbs = 10;
        [SerializeField, Range(1, 10), Tooltip("시작 부스러기(개). 올리면 초기 성장 빨라짐. 권장 6.")] private int _initialCrumbs = 6;
        [SerializeField, Range(0.5f, 4f), Tooltip("치즈 흡수 거리(m). 올리면 쉽게 수집. 권장 1.5.")] private float _pickupRadius = 1.5f;
        [SerializeField, Range(5f, 30f), Tooltip("큰 치즈 유지(초). 올리면 늦게 회수 가능. 권장 15.")] private float _bigCheeseLifetime = 15f;
        [SerializeField, Range(3f, 14f), Tooltip("미끼 최대 사거리(m). 올리면 멀리 유인 가능. 권장 8.")] private float _baitRange = 8f;
        [SerializeField, Range(1f, 10f), Tooltip("미끼 재사용 대기(초). 올리면 미끼 사용 감소. 권장 5.")] private float _baitCooldown = 5f;
        [SerializeField, Range(0.2f, 1.5f), Tooltip("미끼 비행(초). 올리면 착지 지연. 권장 0.5.")] private float _baitFlightTime = 0.5f;
        [SerializeField, Range(2f, 10f), Tooltip("미끼 소음 반경(m). 올리면 더 많은 고양이 유인. 권장 6.")] private float _baitNoiseRadius = 6f;
        [SerializeField, Range(1f, 6f), Tooltip("미끼 먹는 시간(초). 올리면 함정 사용 여유 증가. 권장 3.")] private float _baitEatTime = 3f;
        [SerializeField, Range(4f, 20f), Tooltip("남은 미끼 유지(초). 올리면 유인 기회 증가. 권장 10.")] private float _baitLifetime = 10f;

        [Header("반복 성장")]
        [SerializeField, Range(0.02f, 0.25f), Tooltip("소음 성장 1회 감소 비율(배). 올리면 소음 감소 폭 증가. 권장 0.1.")] private float _noiseUpgrade = 0.1f;
        [SerializeField, Range(0.2f, 0.8f), Tooltip("소음 최소 배율(배). 내리면 반복 성장 후 더 조용함. 권장 0.4.")] private float _minimumNoise = 0.4f;
        [SerializeField, Range(0.1f, 1f), Tooltip("대시 성장 1회 대기 감소(초). 올리면 빠른 대시 성장. 권장 0.7.")] private float _dashUpgrade = 0.7f;
        [SerializeField, Range(0.5f, 3f), Tooltip("대시 최소 대기(초). 내리면 반복 성장 후 자주 대시. 권장 1.")] private float _minimumDashCooldown = 1f;

        public float Duration => _duration;
        public int StartingHearts => _startingHearts;
        public int HeartCap => _heartCap;
        public float Invulnerability => _invulnerability;
        public int BaseExperience => _baseExperience;
        public int ExperienceStep => _experienceStep;
        public float ReinforcementInterval => _reinforcementInterval;
        public int CatHearts => _catHearts;
        public float PatrolSpeed => _patrolSpeed;
        public float InvestigateSpeed => _investigateSpeed;
        public float ChaseSpeed => _chaseSpeed;
        public float RetreatSpeed => _retreatSpeed;
        public float HearingMultiplier => _hearingMultiplier;
        public float DetectRadius => _detectRadius;
        public float LoseDistance => _loseDistance;
        public float LoseTime => _loseTime;
        public float InvestigateWait => _investigateWait;
        public float CatchRadius => _catchRadius;
        public float CatchRest => _catchRest;
        public float RespawnDelay => _respawnDelay;
        public float InteractionRadius => _interactionRadius;
        public float TrapPreviewDistance => _trapPreviewDistance;
        public float CupDelay => _cupDelay;
        public float CupRadius => _cupRadius;
        public int CupDamage => _cupDamage;
        public float CupStun => _cupStun;
        public float CupCooldown => _cupCooldown;
        public float VacuumDuration => _vacuumDuration;
        public float VacuumSpeed => _vacuumSpeed;
        public float VacuumRadius => _vacuumRadius;
        public int VacuumDamage => _vacuumDamage;
        public float VacuumPush => _vacuumPush;
        public float VacuumStun => _vacuumStun;
        public float VacuumCooldown => _vacuumCooldown;
        public float CheeseInterval => _cheeseInterval;
        public int MaxCrumbs => _maxCrumbs;
        public int InitialCrumbs => _initialCrumbs;
        public float PickupRadius => _pickupRadius;
        public float BigCheeseLifetime => _bigCheeseLifetime;
        public float BaitRange => _baitRange;
        public float BaitCooldown => _baitCooldown;
        public float BaitFlightTime => _baitFlightTime;
        public float BaitNoiseRadius => _baitNoiseRadius;
        public float BaitEatTime => _baitEatTime;
        public float BaitLifetime => _baitLifetime;
        public float NoiseUpgrade => _noiseUpgrade;
        public float MinimumNoise => _minimumNoise;
        public float DashUpgrade => _dashUpgrade;
        public float MinimumDashCooldown => _minimumDashCooldown;

        /// <summary>경과 시간에 맞는 단계별 고양이 수를 구한다.</summary>
        public int GetCatCount(float elapsed)
        {
            if (_catsPerStage == null || _catsPerStage.Length == 0) return 2;
            int stage = Mathf.Clamp(Mathf.FloorToInt(elapsed / Mathf.Max(1f, _stageDuration)), 0, _catsPerStage.Length - 1);
            return Mathf.Max(0, _catsPerStage[stage]);
        }
    }
}
