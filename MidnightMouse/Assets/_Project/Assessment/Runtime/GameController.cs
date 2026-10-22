using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MidnightMouse
{
    /// <summary>게임 루트에서 세션과 명시적으로 연결한 개체들의 진행 시간을 관리한다.</summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(GameAudio))]
    public sealed partial class GameController : MonoBehaviour
    {
        [SerializeField, Tooltip("이 스테이지가 공유할 생존·고양이·함정·성장 수치 에셋.")] private AssessmentRules _rules;
        [SerializeField, Tooltip("플레이어 루트의 MouseController.")] private MouseController _player;
        [SerializeField, Tooltip("단계별로 활성화하고 재사용할 고양이 풀.")] private CatController[] _cats;
        [SerializeField, Tooltip("상호작용 가능한 컵·청소기 함정.")] private TrapController[] _traps;
        [SerializeField, Tooltip("경험치 치즈를 재사용할 고정 풀.")] private CheesePickup[] _pickups;
        [SerializeField, Tooltip("NavMesh 위의 순찰·치즈 생성 지점.")] private Transform[] _patrolPoints;
        [SerializeField, Tooltip("이동 방향·미끼 조준에 사용할 게임 카메라.")] private Camera _worldCamera;
        [SerializeField, Tooltip("화면 위 방향 이동 키.")] private Key _upKey = Key.W;
        [SerializeField, Tooltip("화면 아래 방향 이동 키.")] private Key _downKey = Key.S;
        [SerializeField, Tooltip("화면 왼쪽 방향 이동 키.")] private Key _leftKey = Key.A;
        [SerializeField, Tooltip("화면 오른쪽 방향 이동 키.")] private Key _rightKey = Key.D;
        [SerializeField, Tooltip("소음을 줄이는 살금살금 키. 달리기와 동시 입력 시 우선함.")] private Key _sneakKey = Key.LeftCtrl;
        [SerializeField, Tooltip("빠른 달리기 키.")] private Key _runKey = Key.LeftShift;
        [SerializeField, Tooltip("짧은 대시 키.")] private Key _dashKey = Key.Space;
        [SerializeField, Tooltip("가까운 함정을 사용하는 키.")] private Key _interactKey = Key.E;
        [SerializeField, Tooltip("일시정지와 재개 키.")] private Key _pauseKey = Key.Escape;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField, Tooltip("다음 단계 시작으로 게임 시계를 옮기는 디버그 키.")] private Key _nextStageKey = Key.F1;
        [SerializeField, Tooltip("게임 시계를 30초 옮기는 디버그 키.")] private Key _advanceClockKey = Key.F2;
        [SerializeField, Tooltip("모든 고양이를 정상 보상과 함께 퇴장시키는 디버그 키.")] private Key _banishAllKey = Key.F3;
        [SerializeField, Tooltip("판 CSV 시나리오. loop, corner, switch 중 측정할 항목을 적음.")] private string _measurementScenario = "loop";
        private RunMeasurement _measurement;
        private string _deathCause = "";
#endif

        private float _deferredDawn = -1f;
        private GameSession _session;
        private GameAudio _audio;
        private CheeseBait[] _baits;
        private RadiusRing _aimRing;
        private GamePhase _lastPhase;
        private float _baitCooldown;
        private float _cheeseTimer;
        private float _reinforcementTimer;
        private int _enteredCats;
        private TrapController _hintTrap;
        private bool _hintReady;
        private bool _hintActive;
        private bool _initialized;
        private bool _startInputArmed;
        private int _startedFrame = -1;
        public bool StartedThisFrame => _startedFrame == Time.frameCount;

        public GamePhase Phase => _session == null ? GamePhase.Title : _session.Phase;
        public float Elapsed => _session == null ? 0f : _session.Elapsed;
        public float RealElapsed => _session == null ? 0f : _session.RealElapsed;
        public int Stage => _rules == null ? 1 : Mathf.Clamp(1 + Mathf.FloorToInt(Elapsed / StageSeconds), 1, 5);
        public float LastDawnAdvance { get; private set; }
        public float DawnNoticeUntil { get; private set; }
        public string DebugNotice { get; private set; } = "";
        public float DebugNoticeUntil { get; private set; }
        public float Duration => CurrentNight != null ? CurrentNight.Duration : _rules == null ? 300f : _rules.Duration;
        public int Hearts => _session == null ? 0 : _session.Hearts;
        public int MaxHearts => _session == null ? 0 : _session.MaxHearts;
        public int CheeseCollected => _session == null ? 0 : _session.CheeseCollected;
        public int Level => _session == null ? 1 : _session.Level;
        public int Experience => _session == null ? 0 : _session.Experience;
        public int ExperienceToNext => _session == null ? 4 : _session.ExperienceToNext;
        public int CatsBanished => _session == null ? 0 : _session.CatsBanished;
        public MouseController Player => _player;
        public string CurrentHint { get; private set; } = "치즈를 모으며 해가 뜰 때까지 살아남으세요.";
        public float DashCooldown01 => _player == null ? 0f : _player.DashCooldown01;
        public float BaitCooldown01 => _rules == null ? 0f : Mathf.Clamp01(_baitCooldown / _rules.BaitCooldown);
        public bool IsPlayerInvulnerable => _session != null && _session.InvulnerabilityRemaining > 0f;
        public AssessmentRules Rules => _rules;
        public Camera WorldCamera => _worldCamera;
        public IReadOnlyList<CatController> Cats => _cats;
        public IReadOnlyList<TrapController> Traps => _traps;
        public Key PauseKey => _pauseKey;

        /// <summary>씬 연결을 검증하고 한 번만 규칙·오디오·개체 풀을 준비한다.</summary>
        private void Awake()
        {
            if (_rules == null || _player == null || _worldCamera == null || _cats == null || _traps == null ||
                _pickups == null || _patrolPoints == null || _patrolPoints.Length == 0)
            {
                Debug.LogError("GameController: 규칙, 플레이어, 카메라, 개체 풀과 순찰 지점 연결을 확인해야 합니다.", this);
                enabled = false;
                return;
            }
            InitializeCampaign();
            _session = CreateSession();
            _audio = GetComponent<GameAudio>();
            _player.Initialize(this, _worldCamera);
            for (int index = 0; index < _cats.Length; index++) _cats[index].Initialize(this, _patrolPoints, index);
            foreach (TrapController trap in _traps) trap.Initialize(this);
            foreach (CheesePickup pickup in _pickups) pickup.Initialize(this);
            _baits = new CheeseBait[_rules.BaitPoolSize];
            for (int index = 0; index < _baits.Length; index++)
            {
                var baitObject = new GameObject("미끼 치즈 풀 " + index);
                baitObject.transform.SetParent(transform, false);
                _baits[index] = baitObject.AddComponent<CheeseBait>();
                _baits[index].Initialize(this);
            }
            _aimRing = RadiusRing.Create(transform, "미끼 착지 조준", new Color(1f, 0.8f, 0.25f), 0.05f);
            _aimRing.SetAppearance(0.32f, Color.yellow, false);
            _lastPhase = GamePhase.Title;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string[] arguments = System.Environment.GetCommandLineArgs();
            int scenarioIndex = System.Array.IndexOf(arguments, "--m0-scenario");
            if (scenarioIndex >= 0 && scenarioIndex + 1 < arguments.Length)
            {
                string scenario = arguments[scenarioIndex + 1];
                if (scenario == "loop" || scenario == "corner" || scenario == "switch") _measurementScenario = scenario;
            }
#endif
            _initialized = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        /// <summary>실제 입력을 읽고 진행 상태에서만 모든 게임 시간을 같은 순서로 흐르게 한다.</summary>
        private void Update()
        {
            if (!_initialized) return;
            Keyboard keyboard = Keyboard.current;
            if (Phase == GamePhase.Ready)
            {
                if (keyboard == null || !keyboard.anyKey.isPressed) _startInputArmed = true;
                if (_startInputArmed && keyboard != null && keyboard.anyKey.wasPressedThisFrame) ConfirmStart();
                return;
            }
            if (Phase != GamePhase.Playing) { SyncPhase(); return; }
            float delta = Time.deltaTime;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (keyboard != null)
            {
                if (keyboard[_nextStageKey].wasPressedThisFrame) DebugAdvance(0);
                if (keyboard[_advanceClockKey].wasPressedThisFrame) DebugAdvance(1);
                if (keyboard[_banishAllKey].wasPressedThisFrame) DebugAdvance(2);
                if (Phase != GamePhase.Playing) { SyncPhase(); return; }
            }
            foreach (var cat in _cats)
                if (cat.IsAvailable && cat.State == CatState.Chase) { _measurement?.AddChaseTime(delta); break; }
#endif
            Vector2 movement = Vector2.zero;
            bool sneak = false, run = false, dash = false;
            if (keyboard != null)
            {
                movement.x = (keyboard[_rightKey].isPressed ? 1f : 0f) - (keyboard[_leftKey].isPressed ? 1f : 0f);
                movement.y = (keyboard[_upKey].isPressed ? 1f : 0f) - (keyboard[_downKey].isPressed ? 1f : 0f);
                sneak = keyboard[_sneakKey].isPressed;
                run = keyboard[_runKey].isPressed;
                dash = keyboard[_dashKey].wasPressedThisFrame;
            }
            _player.Tick(delta, movement, sneak, run, dash);
            _baitCooldown = Mathf.Max(0f, _baitCooldown - delta);
            if (keyboard != null && keyboard[_interactKey].wasPressedThisFrame) TryUseNearestTrap();
            bool hasAim = TryGetCursorAim(out Vector3 aim);
            _aimRing.transform.position = aim + Vector3.up * 0.075f;
            _aimRing.SetAppearance(0.32f, _baitCooldown <= 0f ? new Color(1f, 0.8f, 0.25f) : new Color(0.35f, 0.4f, 0.47f), hasAim);
            if (hasAim && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())) TryThrowBait(aim);
            foreach (CheeseBait bait in _baits) bait.Tick(delta);
            foreach (TrapController trap in _traps) trap.Tick(delta);
            foreach (CatController cat in _cats)
            {
                if (Phase != GamePhase.Playing) break;
                cat.Tick(delta);
            }
            foreach (CheesePickup pickup in _pickups)
            {
                if (Phase != GamePhase.Playing) break;
                pickup.Tick(delta);
            }
            // 같은 프레임의 마지막 피격을 먼저 반영해 패배가 생존 승리로 덮이지 않게 한다.
            float beforeClock = Elapsed;
            _session.Tick(delta);
            if (Phase == GamePhase.Playing)
            {
                AdvanceCrumbs(Elapsed - beforeClock);
                UpdatePopulation(delta);
                UpdateHint();
                UpdateFirstGuide();
            }
            SyncPhase();
        }

        /// <summary>새 판을 시작하며 플레이어·함정·치즈·미끼·고양이를 모두 초기화한다.</summary>
        public void StartRun()
        {
            if (!_initialized) return;
            FinishMeasurement("restart");
            if (_session == null || _session.Duration != Duration) _session = CreateSession();
            _session.Start(waitForInput: true);
            _startedFrame = -1;
            _startInputArmed = Keyboard.current == null || !Keyboard.current.anyKey.isPressed;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _measurement = new RunMeasurement(System.Guid.NewGuid().ToString("N"), CurrentNight != null ? CurrentNight.Id : "M0-day2-approx", Application.isBatchMode ? "automated" : _measurementScenario);
            _deathCause = "";
#endif
            LastDawnAdvance = DawnNoticeUntil = DebugNoticeUntil = 0f;
            DebugNotice = "";
            _player.ResetRun();
            _baitCooldown = _reinforcementTimer = 0f;
            _cheeseTimer = _rules.CheeseInterval;
            foreach (CheesePickup pickup in _pickups) pickup.ResetRun();
            foreach (CheeseBait bait in _baits) bait.ResetRun();
            foreach (TrapController trap in _traps) trap.ResetRun();
            ResetNightActors();
            for (int index = 0; index < _rules.InitialCrumbs; index++) SpawnCrumb();
            CurrentHint = "달리면 빨라지지만 고양이가 소음을 듣습니다. 함정의 노란 원에서 E.";
            _hintTrap = null;
            _hintReady = _hintActive = false;
            SyncPhase();
        }

        /// <summary>첫 키 입력을 소비하고 시간·이동·안내를 함께 시작한다.</summary>
        public void ConfirmStart()
        {
            if (Phase != GamePhase.Ready) return;
            _session.BeginPlay();
            _startedFrame = Time.frameCount;
            ShowGuide(1, "WASD 이동 · Ctrl 살금살금 · 발밑 원이 소리 범위");
            SyncPhase();
        }

        /// <summary>결과 화면에서 동일한 초기 조건의 새 판을 연다.</summary>
        public void RestartRun() => StartRun();

        /// <summary>진행과 일시정지를 전환하고 NavMesh 이동도 즉시 정지시킨다.</summary>
        public void TogglePause()
        {
            if (!_initialized) return;
            _session.TogglePause();
            SyncPhase();
        }

        /// <summary>현재 판을 멈추고 시작 화면으로 돌아간다.</summary>
        public void ReturnToTitle()
        {
            if (!_initialized) return;
            FinishMeasurement("title");
            _session.ReturnToTitle();
            SyncPhase();
        }

        /// <summary>성장 선택 상태에서만 유효한 선택 하나를 적용한다.</summary>
        public void SelectUpgrade(int index)
        {
            if (Phase != GamePhase.Upgrade || index < 0 || index > 2) return;
            if (index == 0) _player.UpgradeNoise();
            else if (index == 1) _player.UpgradeDash();
            else _session.IncreaseMaxHearts();
            _session.CompleteUpgrade();
            PlaySound(GameSound.Upgrade);
            SyncPhase();
        }

        /// <summary>성장 카드의 고정 제목을 제공한다.</summary>
        public string GetUpgradeTitle(int index)
        {
            switch (index)
            {
                case 0: return "솜털 발바닥";
                case 1: return "날쌘 몸";
                case 2: return "든든한 배";
                default: return "";
            }
        }

        /// <summary>현재 적용할 성장 효과와 반복 상한을 설명한다.</summary>
        public string GetUpgradeDescription(int index)
        {
            switch (index)
            {
                case 0: return $"발소리 배율 −{_rules.NoiseUpgrade:P0}\n최소 {_rules.MinimumNoise:P0} · 잔상에는 영향 없음";
                case 1: return "대시 대기 −0.7초\n최소 1초까지 감소";
                case 2: return "최대 하트 +1, 하트 1 회복\n최대 5하트 · 상한에서도 회복";
                default: return "";
            }
        }

        /// <summary>바닥 NavMesh로 보정한 사거리 안 지점에 사용 가능한 미끼를 던진다.</summary>
        public bool TryThrowBait(Vector3 groundPoint)
        {
            if (Phase != GamePhase.Playing || _baitCooldown > 0f || !TryClampAim(groundPoint, out Vector3 target)) return false;
            foreach (CheeseBait bait in _baits)
            {
                if (bait.gameObject.activeSelf) continue;
                bait.Launch(_player.transform.position, target);
                _baitCooldown = _rules.BaitCooldown;
                PlaySound(GameSound.Dash);
                return true;
            }
            return false;
        }

        /// <summary>상호작용 범위 안의 가장 가까운 함정만 작동시킨다.</summary>
        public bool TryUseNearestTrap()
        {
            if (Phase != GamePhase.Playing) return false;
            TrapController nearest = FindNearestTrap(_rules.InteractionRadius);
            return nearest != null && nearest.TryActivate();
        }

        /// <summary>진행 중 유효한 피격만 체력·반동·효과음으로 반영한다.</summary>
        public bool TryDamagePlayer(Vector3 source, CatState sourceState = CatState.Chase)
        {
            if (!_initialized || !_session.TryDamage()) return false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Phase == GamePhase.Lost) _deathCause = "Curious:" + sourceState;
#endif
            RecordMeasurement("player_hit", "Curious:" + sourceState);
            _player.KnockBack(source);
            PlaySound(GameSound.Hit);
            SyncPhase();
            return true;
        }

        /// <summary>획득한 치즈를 세션에 전달하고 성장에 따른 정지를 즉시 반영한다.</summary>
        public void CollectCheese(int experience)
        {
            if (Phase != GamePhase.Playing || experience <= 0) return;
            _session.CollectCheese(experience);
            PlaySound(Phase == GamePhase.Upgrade ? GameSound.Upgrade : GameSound.Pickup);
            SyncPhase();
        }

        /// <summary>실제 소음 이벤트 때만 고양이 풀에 감지 여부를 전달한다.</summary>
        public void EmitNoise(Vector3 point, float radius, NoiseKind kind = NoiseKind.Normal, bool fromPlayer = false, CheeseBait bait = null)
        {
            if (Phase != GamePhase.Playing || radius <= 0f) return;
            foreach (CatController cat in _cats) cat.ReceiveNoise(point, radius, kind, fromPlayer, bait);
        }

        /// <summary>고양이 퇴장 위치에 큰 치즈를 떨어뜨리고 퇴장 수를 기록한다.</summary>
        public void CatBanished(Vector3 position, int experience)
        {
            if (Phase != GamePhase.Playing) return;
            _session.RegisterBanished();
            SpawnPickup(position, experience, _rules.BigCheeseLifetime);
            LastDawnAdvance = Mathf.Min(Duration - Elapsed, experience * _rules.DawnPerHealth);
            DawnNoticeUntil = Time.unscaledTime + 1.6f;
            if (_deferredDawn >= 0f) { _deferredDawn += experience * _rules.DawnPerHealth; return; }
            AdvanceGameClock(experience * _rules.DawnPerHealth);
            PlaySound(GameSound.Trap);
        }

        /// <summary>동시에 맞는 대상의 부탁·퇴장을 모두 기록한 뒤 아침을 정산한다.</summary>
        public void BeginTrapHits() => _deferredDawn = 0f;

        /// <summary>한 타격 묶음의 시간 보상을 한 번 적용한다.</summary>
        public void EndTrapHits()
        {
            float seconds = _deferredDawn;
            _deferredDawn = -1f;
            if (seconds <= 0f) return;
            LastDawnAdvance = Mathf.Min(Duration - Elapsed, seconds);
            AdvanceGameClock(seconds);
            PlaySound(GameSound.Trap);
        }

        /// <summary>미리 생성한 효과음을 재생한다.</summary>
        public void PlaySound(GameSound sound)
        {
            if (_audio != null) _audio.Play(sound);
        }

        /// <summary>단계별 증원과 부스러기 생성 주기를 진행한다.</summary>
        private void AdvanceCrumbs(float seconds)
        {
            _cheeseTimer -= seconds;
            while (_cheeseTimer <= 0f)
            {
                _cheeseTimer += _rules.CheeseInterval;
                SpawnCrumb();
            }
        }

        /// <summary>게임 시계로 증원 목표를 고르고 실제 시간으로 입장을 분산한다.</summary>
        private void UpdatePopulation(float delta)
        {
            foreach (var cat in _cats) cat.ApplyStageRules();
            _reinforcementTimer = Mathf.Max(0f, _reinforcementTimer - delta);
            int desired = Mathf.Min(_rules.GetCatCount(Elapsed), _cats.Length);
            if (CurrentNight != null)
            {
                if (_reinforcementTimer > 0f) return;
                CatDefinition missing = MissingKind();
                if (missing == null) return;
                EnterCat(missing);
                _reinforcementTimer = _rules.ReinforcementInterval;
                PlaySound(GameSound.Alert);
                return;
            }
            if (_enteredCats < desired && _reinforcementTimer <= 0f)
            {
                EnterCat(null);
                _reinforcementTimer = _rules.ReinforcementInterval;
                PlaySound(GameSound.Alert);
            }
        }

        /// <summary>같은 위치가 겹치지 않는 순찰 지점에 부스러기를 풀에서 꺼낸다.</summary>
        private void SpawnCrumb()
        {
            int crumbs = 0;
            foreach (CheesePickup pickup in _pickups) if (pickup.IsActive && pickup.IsCrumb) crumbs++;
            if (crumbs >= _rules.MaxCrumbs) return;
            int start = Random.Range(0, _patrolPoints.Length);
            for (int index = 0; index < _patrolPoints.Length; index++)
            {
                Vector3 point = _patrolPoints[(start + index) % _patrolPoints.Length].position;
                bool occupied = false;
                foreach (CheesePickup pickup in _pickups)
                {
                    if (pickup.IsActive && (pickup.transform.position - point).sqrMagnitude < 1f) { occupied = true; break; }
                }
                if (occupied) continue;
                SpawnPickup(point, 1, 0f);
                return;
            }
        }

        /// <summary>지정 위치가 NavMesh 위일 때만 비어 있는 풀 항목을 활성화한다.</summary>
        private void SpawnPickup(Vector3 point, int experience, float lifetime)
        {
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, 1f, NavMesh.AllAreas)) return;
            foreach (CheesePickup pickup in _pickups)
            {
                if (pickup.IsActive) continue;
                pickup.Spawn(hit.position, experience, lifetime);
                return;
            }
        }

        /// <summary>커서 광선을 바닥 평면에 투영해 적법한 미끼 위치로 보정한다.</summary>
        private bool TryGetCursorAim(out Vector3 target)
        {
            target = _player.transform.position;
            if (Mouse.current == null) return false;
            Ray ray = _worldCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            var floor = new Plane(Vector3.up, Vector3.zero);
            if (!floor.Raycast(ray, out float distance)) return false;
            return TryClampAim(ray.GetPoint(distance), out target);
        }

        /// <summary>잘못된 좌표와 바닥 밖 지점을 거부하고 최대 사거리를 지킨다.</summary>
        private bool TryClampAim(Vector3 point, out Vector3 target)
        {
            target = _player.transform.position;
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z)) return false;
            Vector3 difference = point - _player.transform.position;
            difference.y = 0f;
            point = _player.transform.position + Vector3.ClampMagnitude(difference, _rules.BaitRange);
            point.y = 0f;
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, 0.6f, NavMesh.AllAreas)) return false;
            Vector3 toHit = hit.position - _player.transform.position;
            toHit.y = 0f;
            if (toHit.sqrMagnitude > _rules.BaitRange * _rules.BaitRange + 0.01f) return false;
            target = hit.position;
            return true;
        }

        /// <summary>지정 거리 안의 가장 가까운 함정을 찾는다.</summary>
        private TrapController FindNearestTrap(float maximumDistance)
        {
            TrapController nearest = null;
            float best = maximumDistance * maximumDistance;
            foreach (TrapController trap in _traps)
            {
                Vector3 difference = trap.transform.position - _player.transform.position;
                difference.y = 0f;
                if (difference.sqrMagnitude > best) continue;
                best = difference.sqrMagnitude;
                nearest = trap;
            }
            return nearest;
        }

        /// <summary>근처 함정이나 진행 안내가 달라질 때만 HUD 문구를 바꾼다.</summary>
        private void UpdateHint()
        {
            TrapController trap = FindNearestTrap(_rules.InteractionRadius);
            bool ready = trap != null && trap.IsReady;
            bool active = trap != null && trap.IsActive;
            if (_hintTrap == trap && _hintReady == ready && _hintActive == active) return;
            _hintTrap = trap;
            _hintReady = ready;
            _hintActive = active;
            if (trap == null) CurrentHint = "소음으로 고양이를 유인하세요. 노란 원 근처에서 E로 함정을 사용합니다.";
            else if (!trap.IsNightEnabled) CurrentHint = "청소기 충전 중 · 2일째부터 사용할 수 있습니다.";
            else if (trap.IsPressing) CurrentHint = "스위치 누르는 중 · 움직이면 취소됩니다.";
            else if (trap.IsReady) CurrentHint = trap.Kind == TrapKind.Cup ? "E  컵 떨어뜨리기 · 큰 원 안으로 고양이를 유인하세요" : "E  청소기 작동 · 표시된 경로 위로 고양이를 유인하세요";
            else if (trap.IsActive) CurrentHint = "함정이 작동 중입니다. 다음 유인 위치로 이동하세요.";
            else CurrentHint = "함정 충전 중 · 원이 노란색이 되면 다시 사용할 수 있습니다.";
        }

        /// <summary>세션 상태 전이를 실제 AI 정지·조준 표시·효과음에 반영한다.</summary>
        private void SyncPhase()
        {
            if (_lastPhase == Phase) return;
            _lastPhase = Phase;
            bool running = Phase == GamePhase.Playing;
            foreach (CatController cat in _cats) cat.SetSimulation(running);
            _player.SetSimulation(running);
            if (!running) _aimRing.SetAppearance(0.32f, Color.yellow, false);
            if (Phase == GamePhase.Won) PlaySound(GameSound.Win);
            else if (Phase == GamePhase.Lost) PlaySound(GameSound.Lose);
            if (Phase == GamePhase.Won || Phase == GamePhase.Lost)
            {
                FinishCampaign();
                FinishMeasurement(Phase == GamePhase.Won ? "won" : "lost");
            }
        }

        /// <summary>게임 시계 변화량만 부스러기와 단계에 전달한다.</summary>
        private void AdvanceGameClock(float seconds)
        {
            float before = Elapsed;
            _session.AdvanceClock(seconds);
            AdvanceCrumbs(Elapsed - before);
            if (Phase == GamePhase.Playing) UpdatePopulation(0f);
        }

        /// <summary>출시 빌드에서는 호출과 사건 문자열 생성까지 제외한다.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public void RecordMeasurement(string kind, string detail)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _measurement?.Record(RealElapsed, kind, detail);
#endif
        }

        /// <summary>판 종료 시 기록을 로컬 CSV에 남기고 쓰기 실패를 명시한다.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void FinishMeasurement(string outcome)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_measurement == null || _session == null) return;
            foreach (var trap in _traps) if (trap != null) trap.RecordPendingActivation();
            foreach (var cat in _cats) if (cat != null) cat.RecordPendingChase();
            string row = _measurement.Finish(_session, outcome, _deathCause);
            if (row == null) return;
            string path = System.IO.Path.Combine(Application.persistentDataPath, "Measurements", "m0-runs-v1.csv");
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                bool header = !System.IO.File.Exists(path) || new System.IO.FileInfo(path).Length == 0;
                System.IO.File.AppendAllText(path, (header ? RunMeasurement.Header + "\n" : "") + row + "\n", new System.Text.UTF8Encoding(false));
            }
            catch (System.IO.IOException exception) { Debug.LogError("판 CSV 저장 실패: " + path + " / " + exception.Message); }
            catch (System.UnauthorizedAccessException exception) { Debug.LogError("판 CSV 저장 권한 없음: " + path + " / " + exception.Message); }
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>예약된 디버그 입력도 정상 시간·퇴장·보상 경로로 실행한다.</summary>
        public void DebugAdvance(int action)
        {
            if (Phase != GamePhase.Playing || action < 0 || action > 2) return;
            RecordMeasurement("debug", "F" + (action + 1));
            if (action == 0) AdvanceGameClock(Stage * StageSeconds - Elapsed);
            else if (action == 1) AdvanceGameClock(30f);
            else
            {
                // 마지막 고양이까지 정상 보상을 준 뒤 아침 도달을 판정한다.
                BeginTrapHits();
                foreach (var cat in _cats) if (cat.CanReceiveHit) cat.ApplyTrapHit(cat.Health, 0f, Vector3.zero);
                EndTrapHits();
            }
            DebugNotice = action == 0 ? "디버그: 단계 " + Stage : action == 1 ? "디버그: 게임 시계 +30초" : "디버그: 모든 고양이 퇴장";
            DebugNoticeUntil = Time.unscaledTime + 2f;
            SyncPhase();
        }
#endif

        /// <summary>앱 종료로 중단된 판도 한 번만 기록한다.</summary>
        private void OnApplicationQuit() => FinishMeasurement("quit");

        /// <summary>씬 교체 중 진행하던 판을 기록한다.</summary>
        private void OnDestroy() => FinishMeasurement("scene_unload");
    }
}
