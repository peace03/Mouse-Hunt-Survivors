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
    public sealed class GameController : MonoBehaviour
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

        public GamePhase Phase => _session == null ? GamePhase.Title : _session.Phase;
        public float Elapsed => _session == null ? 0f : _session.Elapsed;
        public float Duration => _rules == null ? 300f : _rules.Duration;
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
            _session = new GameSession(_rules.Duration, _rules.StartingHearts, _rules.Invulnerability,
                _rules.BaseExperience, _rules.ExperienceStep, _rules.HeartCap);
            _audio = GetComponent<GameAudio>();
            _player.Initialize(this, _worldCamera);
            for (int index = 0; index < _cats.Length; index++) _cats[index].Initialize(this, _patrolPoints, index);
            foreach (TrapController trap in _traps) trap.Initialize(this);
            foreach (CheesePickup pickup in _pickups) pickup.Initialize(this);
            _baits = new CheeseBait[3];
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
            _initialized = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        /// <summary>실제 입력을 읽고 진행 상태에서만 모든 게임 시간을 같은 순서로 흐르게 한다.</summary>
        private void Update()
        {
            if (!_initialized) return;
            Keyboard keyboard = Keyboard.current;
            if (Phase != GamePhase.Playing) { SyncPhase(); return; }
            float delta = Time.deltaTime;
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
            _session.Tick(delta);
            if (Phase == GamePhase.Playing)
            {
                UpdatePopulation(delta);
                UpdateHint();
            }
            SyncPhase();
        }

        /// <summary>새 판을 시작하며 플레이어·함정·치즈·미끼·고양이를 모두 초기화한다.</summary>
        public void StartRun()
        {
            if (!_initialized) return;
            _session.Start();
            _player.ResetRun();
            _baitCooldown = _reinforcementTimer = 0f;
            _cheeseTimer = _rules.CheeseInterval;
            _enteredCats = Mathf.Min(_rules.GetCatCount(0f), _cats.Length);
            foreach (CheesePickup pickup in _pickups) pickup.ResetRun();
            foreach (CheeseBait bait in _baits) bait.ResetRun();
            foreach (TrapController trap in _traps) trap.ResetRun();
            for (int index = 0; index < _cats.Length; index++) _cats[index].SetAvailable(index < _enteredCats);
            for (int index = 0; index < _rules.InitialCrumbs; index++) SpawnCrumb();
            CurrentHint = "달리면 빨라지지만 고양이가 소음을 듣습니다. 함정의 노란 원에서 E.";
            _hintTrap = null;
            _hintReady = _hintActive = false;
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
                case 0: return "이동 소음 반경 −10%\n최소 40%까지 감소";
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
        public bool TryDamagePlayer(Vector3 source)
        {
            if (!_initialized || !_session.TryDamage()) return false;
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
        public void EmitNoise(Vector3 point, float radius, CheeseBait bait = null)
        {
            if (Phase != GamePhase.Playing || radius <= 0f) return;
            foreach (CatController cat in _cats) cat.ReceiveNoise(point, radius, bait);
        }

        /// <summary>고양이 퇴장 위치에 큰 치즈를 떨어뜨리고 퇴장 수를 기록한다.</summary>
        public void CatBanished(Vector3 position, int experience)
        {
            if (Phase != GamePhase.Playing) return;
            _session.RegisterBanished();
            SpawnPickup(position, experience, _rules.BigCheeseLifetime);
            PlaySound(GameSound.Trap);
        }

        /// <summary>미리 생성한 효과음을 재생한다.</summary>
        public void PlaySound(GameSound sound)
        {
            if (_audio != null) _audio.Play(sound);
        }

        /// <summary>단계별 증원과 부스러기 생성 주기를 진행한다.</summary>
        private void UpdatePopulation(float delta)
        {
            _cheeseTimer -= delta;
            if (_cheeseTimer <= 0f)
            {
                _cheeseTimer = _rules.CheeseInterval;
                SpawnCrumb();
            }
            _reinforcementTimer = Mathf.Max(0f, _reinforcementTimer - delta);
            int desired = Mathf.Min(_rules.GetCatCount(Elapsed), _cats.Length);
            if (_enteredCats < desired && _reinforcementTimer <= 0f)
            {
                _cats[_enteredCats++].SetAvailable(true);
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
        }
    }
}
