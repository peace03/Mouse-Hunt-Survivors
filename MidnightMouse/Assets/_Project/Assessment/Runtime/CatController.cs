using UnityEngine;
using UnityEngine.AI;

namespace MidnightMouse
{
    /// <summary>고양이 루트의 NavMeshAgent를 소음·근접 감지 상태에 따라 움직인다.</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed partial class CatController : MonoBehaviour
    {
        [SerializeField, Tooltip("고양이 모델 자식. 루트 이동과 분리해 기절·걷기 동작을 표시함.")] private Transform _visual;
        private static readonly int IdleAnimation = Animator.StringToHash("Base Layer.A_CartoonAnimal-Cat_Idle");
        private static readonly int RunAnimation = Animator.StringToHash("Base Layer.A_CartoonAnimal-Cat__Run");
        private Animator _animator;
        private bool _isMoving;
        private CatDefinition _definition;
        private GameController _game;
        private AssessmentRules _rules;
        private NavMeshAgent _agent;
        private Transform[] _patrolPoints;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Vector3 _visualPosition;
        private Quaternion _visualRotation;
        private Vector3 _target;
        private CheeseBait _bait;
        private RadiusRing _stateRing;
        private TextMesh _symbol;
        private float _stateTimer;
        private float _lostTimer;
        private float _motionTime;
        private float _destinationTimer;
        private float _captureRest;
        private bool _arrived;
        private bool _investigatingBait;
        private Vector3 _investigateProgressPosition;
        private float _investigateBlockedTime;
        private bool _available;
        private bool _initialized;
        private int _patrolIndex;
        private int _slotIndex;
        private CatTarget _targetKind;
        private int _baitGeneration;
        private float _baitImmunityRemaining;
        private float _chaseStarted = -1f;

        public CatDefinition Definition => _definition;
        private CatKind Kind => _definition != null ? _definition.Kind : CatKind.Curious;
        private int MaxHealth => _definition != null ? _definition.Health : _rules.CatHearts;
        private float PatrolSpeed => _definition != null ? _definition.Patrol : _rules.PatrolSpeed;
        private float InvestigateSpeed => _definition != null ? _definition.Investigate : _rules.InvestigateSpeed;
        private float RetreatSpeed => _definition != null ? _definition.Retreat : _rules.RetreatSpeed;

        public CatState State { get; private set; }
        public int Health { get; private set; }
        public float BaitImmunityRemaining => _baitImmunityRemaining;
        public bool IsAvailable => _available;
        public bool CanReceiveHit => _available && CatReactionRules.EvaluateTrapHit(Kind, State) != CatReaction.Ignore;

        /// <summary>씬의 순찰 지점과 공통 규칙을 연결하고 상태 표시를 만든다.</summary>
        public void Initialize(GameController game, Transform[] patrolPoints, int index)
        {
            if (_initialized) return;
            _initialized = true;
            _game = game;
            _rules = game.Rules;
            _patrolPoints = patrolPoints;
            _agent = GetComponent<NavMeshAgent>();
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _patrolIndex = index * 3;
            _slotIndex = index;
            _agent.acceleration = _rules.CatAcceleration;
            _agent.angularSpeed = 480f;
            _agent.stoppingDistance = 0.12f;
            if (_visual == null) Debug.LogError("CatController: _visual에 고양이 모델 자식을 연결해야 합니다.", this);
            else
            {
                _visualPosition = _visual.localPosition;
                _visualRotation = _visual.localRotation;
                _animator = _visual.GetComponentInChildren<Animator>(true);
            }
            _stateRing = RadiusRing.Create(transform, "고양이 상태", new Color(0.4f, 0.65f, 0.7f), 0.045f);
            _stateRing.transform.localPosition = Vector3.up * 0.055f;
            _sleepGauge = RadiusRing.Create(transform, "수면 게이지", new Color(.65f, .7f, .9f), .07f);
            _sleepGauge.transform.localPosition = Vector3.up * .065f;
            _sleepGauge.SetAppearance(.75f, Color.white, false);
            var label = new GameObject("고양이 상태 기호");
            label.transform.SetParent(transform, false);
            label.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            _symbol = label.AddComponent<TextMesh>();
            _symbol.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _symbol.fontSize = 64;
            _symbol.characterSize = 0.1f;
            _symbol.anchor = TextAnchor.MiddleCenter;
            _symbol.alignment = TextAlignment.Center;
            label.GetComponent<MeshRenderer>().sharedMaterial = _symbol.font.material;
            SetAvailable(false);
        }

        /// <summary>비활성 슬롯에 종류와 이번 판의 입장 지점을 배정한다.</summary>
        public void Configure(CatDefinition definition, Vector3 spawn, Vector3? sleepPoint = null, bool startAsleep = false)
        {
            _definition = definition;
            _spawnPosition = spawn;
            _sleepPosition = sleepPoint ?? spawn;
            _startAsleep = startAsleep;
        }

        /// <summary>풀의 고양이를 초기 위치와 체력으로 활성화하거나 쉬게 한다.</summary>
        public void SetAvailable(bool available)
        {
            _available = available;
            gameObject.SetActive(available);
            if (!available) return;
            Health = MaxHealth;
            _ballWakeAt = -1f;
            _chaseStarted = -1f;
            _stateTimer = _lostTimer = _captureRest = _destinationTimer = _baitImmunityRemaining = 0f;
            _targetKind = CatTarget.None;
            _bait = null;
            _agent.enabled = true;
            if (NavMesh.SamplePosition(_spawnPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
            transform.rotation = _spawnRotation;
            if (!_agent.isOnNavMesh)
            {
                Debug.LogError("CatController: 시작 위치에 NavMesh가 없습니다.", this);
                _available = false;
                return;
            }
            if (_visual != null) _visual.gameObject.SetActive(true);
            _isMoving = false;
            if (_animator != null) _animator.Play(IdleAnimation, 0, 0f);
            StartAvailableState();
            SetSimulation(_game.Phase == GamePhase.Playing);
        }

        /// <summary>상태별 타이머와 감지를 진행하고 목적지를 갱신한다.</summary>
        public void Tick(float delta)
        {
            if (_game.Phase != GamePhase.Playing || delta <= 0f || !_available || !_agent.isOnNavMesh) return;
            ApplyStageRules();
            _motionTime += delta;
            _destinationTimer -= delta;
            _captureRest = Mathf.Max(0f, _captureRest - delta);
            _baitImmunityRemaining = Mathf.Max(0f, _baitImmunityRemaining - delta);
            if (State == CatState.Respawning)
            {
                _stateTimer -= delta;
                if (_stateTimer <= 0f) SetAvailable(true);
                return;
            }
            UpdateVisual();
            if (State == CatState.Sleeping) { TickSleeping(delta); return; }
            if (State == CatState.Retreat)
            {
                _stateTimer -= delta;
                MoveTo(_spawnPosition, RetreatSpeed);
                if (HasArrived() || _stateTimer <= 0f)
                {
                    SetState(CatState.Respawning);
                    _stateTimer = _rules.RespawnDelay;
                    if (_visual != null) _visual.gameObject.SetActive(false);
                }
                return;
            }
            if (State == CatState.Stunned)
            {
                _stateTimer -= delta;
                _agent.isStopped = true;
                if (_stateTimer <= 0f)
                {
                    FinishTarget(CatTargetEnd.StunFinished);
                    _destinationTimer = 0f;
                    MoveTo(_target, InvestigateSpeed);
                }
                return;
            }
            if (State == CatState.Eating)
            {
                TryCatchPlayer();
                _stateTimer -= delta;
                _agent.isStopped = true;
                if (_stateTimer <= 0f) FinishTarget(CatTargetEnd.MealFinished);
                return;
            }
            if (_captureRest > 0f)
            {
                _agent.isStopped = true;
                return;
            }
            Vector3 playerPosition = _game.Player.transform.position;
            float playerDistance = HorizontalDistanceSquared(playerPosition, transform.position);
            if (TryCatchPlayer()) return;
            if (_bait != null && (!_bait.IsAvailable || _bait.Generation != _baitGeneration))
            {
                _bait = null;
                FinishTarget(CatTargetEnd.BaitGone);
                _investigatingBait = true;
                _destinationTimer = 0f;
            }
            if (playerDistance <= _rules.DetectRadius * _rules.DetectRadius && CatReactionRules.ShouldChase(Kind, State, _targetKind))
            {
                if (State != CatState.Chase)
                {
                    _bait = null;
                    _game.PlaySound(GameSound.Alert);
                    SetState(CatState.Chase);
                }
                _lostTimer = 0f;
            }
            if (_bait != null)
            {
                _target = _bait.transform.position;
                if (HorizontalDistanceSquared(_target, transform.position) <= _rules.BaitEatRadius * _rules.BaitEatRadius && _bait.TryClaim())
                {
                    FinishTarget(CatTargetEnd.BaitClaimed);
                    _stateTimer = _rules.BaitEatTime;
                    _baitImmunityRemaining = _game.BaitImmunity;
                    _game.RecordMeasurement("bait_start", _slotIndex + "|" + _baitGeneration);
                    _bait = null;
                    return;
                }
            }
            switch (State)
            {
                case CatState.Patrol:
                    if (HasArrived()) ChoosePatrolPoint();
                    MoveTo(_target, PatrolSpeed);
                    break;
                case CatState.Investigate:
                    if (_bait == null && ShouldObserveHere(delta))
                    {
                        _agent.isStopped = true;
                        if (!_arrived) { _stateTimer = _rules.InvestigateWait; _arrived = true; }
                        _stateTimer -= delta;
                        if (_stateTimer <= 0f) FinishTarget(CatTargetEnd.Observed);
                    }
                    else MoveTo(_target, InvestigateSpeed);
                    break;
                case CatState.Chase:
                    if (playerDistance > _rules.LoseDistance * _rules.LoseDistance) _lostTimer += delta;
                    else _lostTimer = 0f;
                    if (_lostTimer >= _rules.LoseTime) FinishTarget(CatTargetEnd.LostPlayer, "distance");
                    else _target = playerPosition;
                    MoveTo(_target, State == CatState.Chase ? _game.ChaseSpeed(_definition) : InvestigateSpeed);
                    break;
                case CatState.WakeRush:
                    _stateTimer -= delta;
                    if (HasArrived() || _stateTimer <= 0f) FinishTarget(CatTargetEnd.WakeFinished);
                    else MoveTo(_target, _definition.WakeSpeed);
                    break;
                case CatState.Returning:
                    if (HasArrived()) FinishTarget(CatTargetEnd.HomeReached);
                    else MoveTo(_sleepPosition, PatrolSpeed);
                    break;
            }
        }

        /// <summary>감지 범위 안의 소음에만 반응하며 미끼를 우선 조사한다.</summary>
        public void ReceiveNoise(Vector3 point, float radius, NoiseKind kind, bool fromPlayer, CheeseBait bait = null, bool fromBall = false)
        {
            if (_game.Phase != GamePhase.Playing || !CanReceiveHit || radius <= 0f || State == CatState.Stunned || State == CatState.Eating) return;
            float hearing = IsLazy && State == CatState.Sleeping && fromPlayer ? radius
                : radius * (_definition != null ? _definition.Hearing : _rules.HearingMultiplier);
            if (HorizontalDistanceSquared(point, transform.position) > hearing * hearing) return;
            CatReaction reaction = CatReactionRules.EvaluateNoise(Kind, State, _targetKind, kind,
                fromPlayer: fromPlayer, fromBall: fromBall, baitAvailable: bait != null && bait.IsAvailable, baitImmune: _baitImmunityRemaining > 0f, stage: _game.Stage);
            if (reaction == CatReaction.Ignore) return;
            if (reaction == CatReaction.RefreshChase)
            {
                _lostTimer = 0f;
                return;
            }
            if (reaction == CatReaction.WakeRush)
            {
                bool ballWake = IsLazy && State == CatState.Sleeping && fromBall && kind == NoiseKind.Loud;
                BeginWakeRush(point, ballWake ? "ball" : "sound");
                if (ballWake) _ballWakeAt = _game.RealElapsed;
                return;
            }
            if (reaction == CatReaction.WakeChase) { WakeAndChase("dash"); return; }
            // 공을 따라가는 실제 이동은 사냥꾼 단계에서 연결한다.
            if (reaction != CatReaction.Investigate && reaction != CatReaction.FollowBait) return;
            _bait = kind == NoiseKind.Bait ? bait : null;
            if (_bait != null) _baitGeneration = _bait.Generation;
            _target = point;
            SetState(CatState.Investigate, kind == NoiseKind.Bait ? "bait" : kind == NoiseKind.Afterimage ? "afterimage" : "sound");
            if (fromPlayer && (kind == NoiseKind.Normal || kind == NoiseKind.Loud))
                _game.ShowGuide(2, "클릭: 치즈를 던져 고양이를 딴 데로");
            _targetKind = CatReactionRules.NoiseTarget(Kind, kind, fromBall);
            _arrived = false;
            _destinationTimer = 0f;
            MoveTo(_target, InvestigateSpeed);
        }

        /// <summary>유효한 함정 피해를 적용하고 기절 또는 치즈를 남기는 퇴장으로 전환한다.</summary>
        public bool ApplyTrapHit(int damage, float stun, Vector3 pushDirection, float pushDistance = 0f, Vector3? switchPosition = null)
        {
            if (_game.Phase != GamePhase.Playing || !CanReceiveHit || damage <= 0) return false;
            if (_ballWakeAt >= 0f && switchPosition.HasValue)
            {
                if (_game.CurrentNight != null && _game.RealElapsed - _ballWakeAt <= _game.CurrentNight.BallWakeWindow)
                    _game.RecordBallWakeHit();
                _ballWakeAt = -1f;
            }
            bool sleepingHit = IsLazy && State == CatState.Sleeping;
            if (sleepingHit)
            {
                damage = (int)System.Math.Min(int.MaxValue, (long)damage * _definition.SleepDamageMultiplier);
                if (_sleptFromBait && switchPosition.HasValue) _game.RecordBaitSleepHit();
            }
            Health = Mathf.Max(0, Health - damage);
            _bait = null;
            if (Health == 0)
            {
                SetState(CatState.Retreat, "banished");
                _game.CatBanished(transform.position, MaxHealth);
                _stateTimer = _rules.RetreatTimeout;
                _destinationTimer = 0f;
                MoveTo(_spawnPosition, RetreatSpeed);
            }
            else if (sleepingHit) BeginWakeRush(switchPosition ?? transform.position, "trap");
            else
            {
                SetState(CatState.Stunned, "trap");
                _target = switchPosition ?? transform.position;
                _stateTimer = Mathf.Max(0.1f, stun);
                if (pushDistance > 0f)
                {
                    Vector3 goal = transform.position + Vector3.ProjectOnPlane(pushDirection, Vector3.up).normalized * pushDistance;
                    if (_agent.Raycast(goal, out NavMeshHit obstacle)) goal = obstacle.position;
                    if (NavMesh.SamplePosition(goal, out NavMeshHit hit, 0.5f, NavMesh.AllAreas)) _agent.Warp(hit.position);
                }
            }
            return true;
        }

        /// <summary>미끼 목표와 먹기 상태에서도 실제 잡기 거리와 피격 무적을 적용한다.</summary>
        private bool TryCatchPlayer()
        {
            if (_captureRest > 0f || HorizontalDistanceSquared(_game.Player.transform.position, transform.position) > _rules.CatchRadius * _rules.CatchRadius) return false;
            if (!_game.TryDamagePlayer(transform.position, State, Kind, _targetKind)) return false;
            _captureRest = _rules.CatchRest;
            _agent.isStopped = true;
            return true;
        }

        /// <summary>게임 상태와 일치하도록 경로 이동을 멈추거나 재개한다.</summary>
        public void SetSimulation(bool running)
        {
            if (_animator != null) _animator.speed = running ? 1f : 0f;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = !running || State == CatState.Stunned || State == CatState.Eating || State == CatState.Respawning || State == CatState.Sleeping;
                if (_agent.isStopped) _agent.velocity = Vector3.zero;
            }
        }

        /// <summary>도달 가능한 목적지를 일정 주기로만 갱신해 경로 계산을 제한한다.</summary>
        private void MoveTo(Vector3 destination, float speed)
        {
            _agent.isStopped = false;
            _agent.speed = speed;
            if (_destinationTimer > 0f) return;
            _destinationTimer = 0.15f;
            _agent.SetDestination(destination);
        }

        /// <summary>경로 계산이 완료된 뒤 실제 도착했는지 확인한다.</summary>
        private bool HasArrived() => !_agent.pathPending && (!_agent.hasPath ||
            // 미끼를 먹을 수 있는 거리에서도 조사를 끝내야 서로 피하는 고양이들이 중심에 갇히지 않는다.
            _agent.remainingDistance <= (_investigatingBait ? _rules.BaitEatRadius : 0.3f));

        /// <summary>도착했거나 몸 반경만큼도 이동하지 못한 조사는 현재 자리에서 두리번거린다.</summary>
        private bool ShouldObserveHere(float delta)
        {
            if (_arrived || HasArrived()) return true;
            // 군집 회피의 작은 흔들림을 이동으로 세지 않고, 정상 이동은 계속 목표를 따라가게 한다.
            if (HorizontalDistanceSquared(transform.position, _investigateProgressPosition) >= _agent.radius * _agent.radius)
            {
                _investigateProgressPosition = transform.position;
                _investigateBlockedTime = 0f;
            }
            else if (!_agent.pathPending) _investigateBlockedTime += delta;
            return _investigateBlockedTime >= _rules.InvestigateWait;
        }

        /// <summary>준비된 순찰 지점 중 다음 지점을 선택한다.</summary>
        private void ChoosePatrolPoint()
        {
            if (_patrolPoints == null || _patrolPoints.Length == 0) { _target = _spawnPosition; return; }
            _patrolIndex = (_patrolIndex + Random.Range(1, _patrolPoints.Length)) % _patrolPoints.Length;
            _target = _patrolPoints[_patrolIndex].position;
            _destinationTimer = 0f;
            MoveTo(_target, PatrolSpeed);
        }

        /// <summary>실제로 끝난 목표의 다음 상태를 공통 표로 결정한다.</summary>
        private void FinishTarget(CatTargetEnd ending, string reason = "state")
        {
            CatState next = CatReactionRules.EndTarget(Kind, State, _targetKind, ending, _game.Stage);
            if (next == CatState.Sleeping) { SleepNow(ending == CatTargetEnd.MealFinished ? "bait" : "home"); return; }
            SetState(next, reason);
            if (next == CatState.Patrol) ChoosePatrolPoint();
            else if (next == CatState.Returning) MoveHome();
        }

        /// <summary>상태가 바뀔 때만 기호와 범위 색을 갱신한다.</summary>
        private void SetState(CatState state, string chaseEndReason = "state")
        {
            if (State == CatState.Chase && state != CatState.Chase) EndChase(chaseEndReason);
            if (State != CatState.Chase && state == CatState.Chase) _chaseStarted = _game.RealElapsed;
            State = state;
            _sleptFromBait = false;
            _wakeProgress = 0f;
            if (state == CatState.Chase) _game.ShowGuide(2, "클릭: 치즈를 던져 고양이를 딴 데로");
            _targetKind = state == CatState.Chase ? CatTarget.Player : state == CatState.WakeRush ? CatTarget.WakePoint : state == CatState.Investigate ? CatTarget.Normal : CatTarget.None;
            _arrived = false;
            _investigatingBait = false;
            _investigateProgressPosition = transform.position;
            _investigateBlockedTime = 0f;
            Color color = new Color(0.4f, 0.65f, 0.7f);
            string symbol = "·";
            switch (state)
            {
                case CatState.Investigate: color = new Color(1f, 0.83f, 0.3f); symbol = "?"; break;
                case CatState.Chase: color = new Color(1f, 0.3f, 0.2f); symbol = "!"; break;
                case CatState.Eating: color = new Color(1f, 0.83f, 0.3f); symbol = "♥"; break;
                case CatState.Stunned: color = new Color(0.8f, 0.6f, 1f); symbol = "*"; break;
                case CatState.Retreat: color = new Color(0.55f, 0.9f, 0.75f); symbol = "!!"; break;
                case CatState.Respawning: symbol = ""; break;
                case CatState.Sleeping: color = new Color(0.65f, 0.7f, 0.9f); symbol = "Zzz"; break;
                case CatState.WakeRush: color = new Color(1f, 0.4f, 0.2f); symbol = "!!"; break;
                case CatState.Returning: color = new Color(0.65f, 0.7f, 0.9f); symbol = "z"; break;
            }
            _symbol.text = symbol;
            _symbol.color = color;
            _stateRing.SetAppearance(0.55f, color, state != CatState.Respawning);
            UpdateSleepGauge();
            SetSimulation(_game.Phase == GamePhase.Playing);
        }

        /// <summary>실제 이동 속도로 기본 동작을 전환하고 기절 기울기를 표시한다.</summary>
        private void UpdateVisual()
        {
            if (State == CatState.Patrol || State == CatState.Investigate || State == CatState.Chase)
                _symbol.text = _baitImmunityRemaining > 0f ? "×" : State == CatState.Chase ? "!" : State == CatState.Investigate ? "?" : "·";
            _symbol.transform.rotation = _game.WorldCamera.transform.rotation;
            if (_visual == null) return;
            float speed = _agent.velocity.magnitude;
            if (_animator != null)
            {
                bool moving = speed > .1f && !_agent.isStopped && State != CatState.Stunned && State != CatState.Eating;
                if (moving != _isMoving)
                {
                    _isMoving = moving;
                    _animator.CrossFadeInFixedTime(moving ? RunAnimation : IdleAnimation, .12f);
                }
                // shortcut: 전용 걷기 클립을 리타기팅하기 전까지 달리기를 속도에 맞춰 느리게 재생한다.
                _animator.speed = moving ? Mathf.Clamp(speed / _rules.ChaseSpeed, .2f, 1.5f) : 1f;
            }
            float bob = _animator == null ? Mathf.Abs(Mathf.Sin(_motionTime * 11f)) * Mathf.Min(speed, 5f) * 0.012f : 0f;
            _visual.localPosition = _visualPosition + Vector3.up * bob;
            _visual.localRotation = _visualRotation * Quaternion.Euler(0f, 0f, State == CatState.Stunned ? 16f * Mathf.Sin(_motionTime * 9f) : _animator == null ? Mathf.Sin(_motionTime * 11f) * speed : 0f);
        }

        /// <summary>높이 차이를 제외하고 바닥에서의 거리를 구한다.</summary>
        private static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
        {
            Vector3 difference = a - b;
            difference.y = 0f;
            return difference.sqrMagnitude;
        }

        /// <summary>추적을 끝낸 원인과 실제 지속 시간을 사건 기록에 남긴다.</summary>
        private void EndChase(string reason)
        {
            if (_chaseStarted < 0f) return;
            _game.RecordMeasurement("chase_end", _slotIndex + "|" + (_game.RealElapsed - _chaseStarted).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "|" + reason);
            _chaseStarted = -1f;
        }

        /// <summary>판이 끝날 때 아직 진행 중인 추적을 종료 원인과 구분해 기록한다.</summary>
        public void RecordPendingChase() => EndChase("run_end");
    }
}
