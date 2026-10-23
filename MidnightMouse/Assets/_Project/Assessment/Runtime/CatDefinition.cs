using UnityEngine;

namespace MidnightMouse
{
    /// <summary>고양이 종류별 기본 능력을 보관하는 데이터 에셋.</summary>
    [CreateAssetMenu(menuName = "한밤의 쥐/고양이 종류")]
    public sealed class CatDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("저장·사건에 쓰는 종류 ID. 출시 뒤 유지.")] private string _id = "curious";
        [SerializeField, Tooltip("소리 반응표 종류. Curious는 미끼, Lazy는 잠으로 유인. Hunter 이동은 후속 구현.")] private CatKind _kind;
        [SerializeField, Min(1), Tooltip("최대 체력. 높을수록 퇴장에 많은 타격 필요. 호기심냥 3.")] private int _health = 3;
        [SerializeField, Min(0.1f), Tooltip("순찰 속도(m/s). 높을수록 방을 빨리 돎. 초기 2.5.")] private float _patrol = 2.5f;
        [SerializeField, Min(0.1f), Tooltip("조사 속도(m/s). 높을수록 소리로 빨리 이동. 초기 3.5.")] private float _investigate = 3.5f;
        [SerializeField, Min(0.1f), Tooltip("추적 속도(m/s). 높을수록 도주 어려움. 초기 4.5.")] private float _chase = 4.5f;
        [SerializeField, Min(0.1f), Tooltip("퇴장 속도(m/s). 높을수록 빨리 물러남. 초기 7.")] private float _retreat = 7f;
        [SerializeField, Min(0.1f), Tooltip("청각 반경 배율. 높을수록 멀리 들음. 초기 1.3.")] private float _hearing = 1.3f;
        [SerializeField, Min(0.1f), Tooltip("게으름냥 기상 질주 속도(m/s). 높을수록 깨웠을 때 위험. 초기 5.5.")] private float _wakeSpeed = 5.5f;
        [SerializeField, Min(0.1f), Tooltip("기상 질주 최대 시간(초). 도착하면 먼저 종료. 초기 6.")] private float _wakeDuration = 6f;
        [SerializeField, Min(1), Tooltip("잠든 채 피격 피해 배율. 높을수록 잠 활용 보상 증가. 초기 2.")] private int _sleepDamageMultiplier = 2;
        [SerializeField, Range(.1f, 10f), Tooltip("소음 원 안에서 걷기를 계속 들으면 깨는 시간(초). 높을수록 늦게 깸. 초기 3.")] private float _walkWakeSeconds = 3f;
        [SerializeField, Range(.1f, 10f), Tooltip("소음 원 안에서 달리기를 계속 들으면 깨는 시간(초). 높을수록 늦게 깸. 초기 1.5.")] private float _runWakeSeconds = 1.5f;
        [SerializeField, Range(.1f, 30f), Tooltip("조용할 때 수면 게이지 전체 회복 시간(초). 높을수록 천천히 회복. 걷기·달리기 기상 시간보다 크게, 권장 6.")] private float _sleepRecoverySeconds = 6f;
        public string Id => _id;
        public CatKind Kind => _kind;
        public int Health => _health;
        public float Patrol => _patrol;
        public float Investigate => _investigate;
        public float Chase => _chase;
        public float Retreat => _retreat;
        public float Hearing => _hearing;
        public float WakeSpeed => _wakeSpeed;
        public float WakeDuration => _wakeDuration;
        public int SleepDamageMultiplier => _sleepDamageMultiplier;
        public float WalkWakeSeconds => _walkWakeSeconds;
        public float RunWakeSeconds => _runWakeSeconds;
        public float SleepRecoverySeconds => _sleepRecoverySeconds;

        /// <summary>종류별 기본 수치가 유한한 양수인지 확인한다.</summary>
        public bool IsValid() => !string.IsNullOrWhiteSpace(_id) && _kind >= CatKind.Curious && _kind <= CatKind.Hunter && _health > 0 &&
            float.IsFinite(_patrol) && _patrol > 0 && float.IsFinite(_investigate) && _investigate > 0 &&
            float.IsFinite(_chase) && _chase > 0 && float.IsFinite(_retreat) && _retreat > 0 &&
            float.IsFinite(_hearing) && _hearing > 0 && float.IsFinite(_wakeSpeed) && _wakeSpeed > 0 &&
            float.IsFinite(_wakeDuration) && _wakeDuration > 0 && _sleepDamageMultiplier >= 1 &&
            float.IsFinite(_walkWakeSeconds) && _walkWakeSeconds > 0 && float.IsFinite(_runWakeSeconds) && _runWakeSeconds > 0 &&
            float.IsFinite(_sleepRecoverySeconds) && _sleepRecoverySeconds > Mathf.Max(_walkWakeSeconds, _runWakeSeconds);
    }
}
