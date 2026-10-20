using UnityEngine;
using UnityEngine.AI;

namespace MidnightMouse
{
    /// <summary>고양이 루트의 NavMeshAgent를 소음·근접 감지 상태에 따라 움직인다.</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class CatController : MonoBehaviour
    {
        [SerializeField, Tooltip("고양이 모델 자식. 루트 이동과 분리해 기절·걷기 동작을 표시함.")] private Transform _visual;
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
        private bool _available;
        private bool _initialized;
        private int _patrolIndex;

        public CatState State { get; private set; }
        public int Health { get; private set; }
        public bool IsAvailable => _available;
        public bool CanReceiveHit => _available && State != CatState.Retreat && State != CatState.Respawning;

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
            _agent.acceleration = 18f;
            _agent.angularSpeed = 480f;
            _agent.stoppingDistance = 0.12f;
            if (_visual == null) Debug.LogError("CatController: _visual에 고양이 모델 자식을 연결해야 합니다.", this);
            else
            {
                _visualPosition = _visual.localPosition;
                _visualRotation = _visual.localRotation;
            }
            _stateRing = RadiusRing.Create(transform, "고양이 상태", new Color(0.4f, 0.65f, 0.7f), 0.045f);
            _stateRing.transform.localPosition = Vector3.up * 0.055f;
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

        /// <summary>풀의 고양이를 초기 위치와 체력으로 활성화하거나 쉬게 한다.</summary>
        public void SetAvailable(bool available)
        {
            _available = available;
            gameObject.SetActive(available);
            if (!available) return;
            Health = _rules.CatHearts;
            _stateTimer = _lostTimer = _captureRest = _destinationTimer = 0f;
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
            SetState(CatState.Patrol);
            ChoosePatrolPoint();
            SetSimulation(_game.Phase == GamePhase.Playing);
        }

        /// <summary>상태별 타이머와 감지를 진행하고 목적지를 갱신한다.</summary>
        public void Tick(float delta)
        {
            if (_game.Phase != GamePhase.Playing || delta <= 0f || !_available || !_agent.isOnNavMesh) return;
            _motionTime += delta;
            _destinationTimer -= delta;
            _captureRest = Mathf.Max(0f, _captureRest - delta);
            if (State == CatState.Respawning)
            {
                _stateTimer -= delta;
                if (_stateTimer <= 0f) SetAvailable(true);
                return;
            }
            UpdateVisual();
            if (State == CatState.Retreat)
            {
                _stateTimer -= delta;
                MoveTo(_spawnPosition, _rules.RetreatSpeed);
                if (HasArrived() || _stateTimer <= 0f)
                {
                    SetState(CatState.Respawning);
                    _stateTimer = _rules.RespawnDelay;
                    if (_visual != null) _visual.gameObject.SetActive(false);
                }
                return;
            }
            if (State == CatState.Stunned || State == CatState.Eating)
            {
                _stateTimer -= delta;
                _agent.isStopped = true;
                if (_stateTimer <= 0f)
                {
                    SetState(CatState.Patrol);
                    ChoosePatrolPoint();
                }
                return;
            }
            if (_captureRest > 0f)
            {
                _agent.isStopped = true;
                return;
            }
            Vector3 playerPosition = _game.Player.transform.position;
            float playerDistance = HorizontalDistanceSquared(playerPosition, transform.position);
            if (_bait != null && !_bait.IsAvailable) _bait = null;
            if (_bait != null)
            {
                _target = _bait.transform.position;
                if (HorizontalDistanceSquared(_target, transform.position) <= 0.6f * 0.6f && _bait.TryClaim())
                {
                    SetState(CatState.Eating);
                    _stateTimer = _rules.BaitEatTime;
                    _bait = null;
                    return;
                }
            }
            else if (playerDistance <= _rules.DetectRadius * _rules.DetectRadius)
            {
                if (State != CatState.Chase)
                {
                    _game.PlaySound(GameSound.Alert);
                    SetState(CatState.Chase);
                }
                _lostTimer = 0f;
            }
            if (_bait == null && playerDistance <= _rules.CatchRadius * _rules.CatchRadius && _game.TryDamagePlayer(transform.position))
            {
                _captureRest = _rules.CatchRest;
                _agent.isStopped = true;
                return;
            }
            switch (State)
            {
                case CatState.Patrol:
                    if (HasArrived()) ChoosePatrolPoint();
                    MoveTo(_target, _rules.PatrolSpeed);
                    break;
                case CatState.Investigate:
                    if (_bait == null && HasArrived())
                    {
                        _agent.isStopped = true;
                        if (!_arrived) { _stateTimer = _rules.InvestigateWait; _arrived = true; }
                        _stateTimer -= delta;
                        if (_stateTimer <= 0f) { SetState(CatState.Patrol); ChoosePatrolPoint(); }
                    }
                    else MoveTo(_target, _rules.InvestigateSpeed);
                    break;
                case CatState.Chase:
                    if (playerDistance > _rules.LoseDistance * _rules.LoseDistance) _lostTimer += delta;
                    else _lostTimer = 0f;
                    if (_lostTimer >= _rules.LoseTime) SetState(CatState.Investigate);
                    else _target = playerPosition;
                    MoveTo(_target, _rules.ChaseSpeed);
                    break;
            }
        }

        /// <summary>감지 범위 안의 소음에만 반응하며 미끼를 우선 조사한다.</summary>
        public void ReceiveNoise(Vector3 point, float radius, CheeseBait bait = null)
        {
            if (_game.Phase != GamePhase.Playing || !CanReceiveHit || radius <= 0f || State == CatState.Stunned || State == CatState.Eating) return;
            float hearing = radius * _rules.HearingMultiplier;
            if (HorizontalDistanceSquared(point, transform.position) > hearing * hearing) return;
            if (bait == null && _bait != null && _bait.IsAvailable) return;
            _lostTimer = 0f;
            if (State == CatState.Chase && bait == null) return;
            _bait = bait;
            _target = point;
            SetState(CatState.Investigate);
            _arrived = false;
            _destinationTimer = 0f;
            MoveTo(_target, _rules.InvestigateSpeed);
        }

        /// <summary>게임 상태와 일치하도록 경로 이동을 멈추거나 재개한다.</summary>
        public void SetSimulation(bool running)
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
                _agent.isStopped = !running || State == CatState.Stunned || State == CatState.Eating || State == CatState.Respawning;
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
        private bool HasArrived() => !_agent.pathPending && (!_agent.hasPath || _agent.remainingDistance <= 0.3f);

        /// <summary>준비된 순찰 지점 중 다음 지점을 선택한다.</summary>
        private void ChoosePatrolPoint()
        {
            if (_patrolPoints == null || _patrolPoints.Length == 0) { _target = _spawnPosition; return; }
            _patrolIndex = (_patrolIndex + Random.Range(1, _patrolPoints.Length)) % _patrolPoints.Length;
            _target = _patrolPoints[_patrolIndex].position;
            _destinationTimer = 0f;
            MoveTo(_target, _rules.PatrolSpeed);
        }

        /// <summary>상태가 바뀔 때만 기호와 범위 색을 갱신한다.</summary>
        private void SetState(CatState state)
        {
            State = state;
            _arrived = false;
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
            }
            _symbol.text = symbol;
            _symbol.color = color;
            _stateRing.SetAppearance(0.55f, color, state != CatState.Respawning);
            SetSimulation(_game.Phase == GamePhase.Playing);
        }

        /// <summary>모델의 작은 걷기 리듬과 기절 기울기를 표시한다.</summary>
        private void UpdateVisual()
        {
            _symbol.transform.rotation = _game.WorldCamera.transform.rotation;
            if (_visual == null) return;
            float speed = _agent.velocity.magnitude;
            float bob = Mathf.Abs(Mathf.Sin(_motionTime * 11f)) * Mathf.Min(speed, 5f) * 0.012f;
            _visual.localPosition = _visualPosition + Vector3.up * bob;
            _visual.localRotation = _visualRotation * Quaternion.Euler(0f, 0f, State == CatState.Stunned ? 16f * Mathf.Sin(_motionTime * 9f) : Mathf.Sin(_motionTime * 11f) * speed);
        }

        /// <summary>높이 차이를 제외하고 바닥에서의 거리를 구한다.</summary>
        private static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
        {
            Vector3 difference = a - b;
            difference.y = 0f;
            return difference.sqrMagnitude;
        }
    }
}
