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
        [SerializeField, Tooltip("이동 방향·미끼 조준에 사용할 게임 카메라.")] private Camera _worldCamera;
        [SerializeField, Tooltip("화면 위 방향 이동 키.")] private Key _upKey = Key.W;
        [SerializeField, Tooltip("화면 아래 방향 이동 키.")] private Key _downKey = Key.S;
        [SerializeField, Tooltip("화면 왼쪽 방향 이동 키.")] private Key _leftKey = Key.A;
        [SerializeField, Tooltip("화면 오른쪽 방향 이동 키.")] private Key _rightKey = Key.D;
        [SerializeField, Tooltip("소음을 줄이는 살금살금 키. 달리기와 동시 입력 시 우선함.")] private Key _sneakKey = Key.LeftCtrl;
        [SerializeField, Tooltip("빠른 달리기 키.")] private Key _runKey = Key.LeftShift;
        [SerializeField, Tooltip("짧은 대시 키.")] private Key _dashKey = Key.Space;
        [SerializeField, Tooltip("일시정지와 재개 키.")] private Key _pauseKey = Key.Escape;

        private GameSession _session;
        private GameAudio _audio;
        private GamePhase _lastPhase;
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
        public bool IsPlayerInvulnerable => _session != null && _session.InvulnerabilityRemaining > 0f;
        public AssessmentRules Rules => _rules;
        public Camera WorldCamera => _worldCamera;

        /// <summary>씬 연결을 검증하고 한 번만 규칙·오디오·개체 풀을 준비한다.</summary>
        private void Awake()
        {
            if (_rules == null || _player == null || _worldCamera == null)
            {
                Debug.LogError("GameController: 규칙, 플레이어, 카메라, 개체 풀과 순찰 지점 연결을 확인해야 합니다.", this);
                enabled = false;
                return;
            }
            _session = new GameSession(_rules.Duration, _rules.StartingHearts, _rules.Invulnerability,
                _rules.BaseExperience, _rules.ExperienceStep, _rules.HeartCap);
            _audio = GetComponent<GameAudio>();
            _player.Initialize(this, _worldCamera);
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
            if (keyboard != null && keyboard[_pauseKey].wasPressedThisFrame) TogglePause();
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
            // 같은 프레임의 마지막 피격을 먼저 반영해 패배가 생존 승리로 덮이지 않게 한다.
            _session.Tick(delta);
            SyncPhase();
        }

        /// <summary>새 판을 시작하며 플레이어·함정·치즈·미끼·고양이를 모두 초기화한다.</summary>
        public void StartRun()
        {
            if (!_initialized) return;
            _session.Start();
            _player.ResetRun();
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

        /// <summary>진행 중 유효한 피격만 체력·반동·효과음으로 반영한다.</summary>
        public bool TryDamagePlayer(Vector3 source)
        {
            if (!_initialized || !_session.TryDamage()) return false;
            _player.KnockBack(source);
            PlaySound(GameSound.Hit);
            SyncPhase();
            return true;
        }

        /// <summary>미리 생성한 효과음을 재생한다.</summary>
        public void PlaySound(GameSound sound)
        {
            if (_audio != null) _audio.Play(sound);
        }

        /// <summary>세션 상태 전이를 실제 AI 정지·조준 표시·효과음에 반영한다.</summary>
        private void SyncPhase()
        {
            if (_lastPhase == Phase) return;
            _lastPhase = Phase;
            bool running = Phase == GamePhase.Playing;
            _player.SetSimulation(running);
            if (Phase == GamePhase.Won) PlaySound(GameSound.Win);
            else if (Phase == GamePhase.Lost) PlaySound(GameSound.Lose);
        }
    }
}
