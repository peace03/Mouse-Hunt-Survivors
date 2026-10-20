using UnityEngine;

namespace MidnightMouse
{
    /// <summary>플레이어 루트의 CharacterController로 이동·소음·피격 반동을 처리한다.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class MouseController : MonoBehaviour
    {
        [SerializeField, Tooltip("사용자 쥐 모델 자식. 교체해도 이동과 충돌은 루트에 유지됨.")] private Transform _visual;
        [SerializeField, Range(0.5f, 4f), Tooltip("살금살금 속도(m/s). 올리면 조용히 빠르게 이동. 권장 2.")] private float _sneakSpeed = 2f;
        [SerializeField, Range(1f, 6f), Tooltip("걷기 속도(m/s). 올리면 기본 이동이 빨라짐. 권장 4.")] private float _walkSpeed = 4f;
        [SerializeField, Range(3f, 9f), Tooltip("달리기 속도(m/s). 올리면 도주 쉬워짐. 권장 6.5.")] private float _runSpeed = 6.5f;
        [SerializeField, Range(0.5f, 4f), Tooltip("살금살금 소음 반경(m). 올리면 쉽게 들킴. 권장 2.")] private float _sneakNoise = 2f;
        [SerializeField, Range(2f, 8f), Tooltip("걷기 소음 반경(m). 올리면 멀리서 고양이를 유인. 권장 5.")] private float _walkNoise = 5f;
        [SerializeField, Range(4f, 12f), Tooltip("달리기·대시 소음 반경(m). 올리면 많은 고양이 유인. 권장 9.")] private float _runNoise = 9f;
        [SerializeField, Range(1f, 5f), Tooltip("대시 거리(m). 올리면 멀리 피함. 권장 3.")] private float _dashDistance = 3f;
        [SerializeField, Range(0.08f, 0.4f), Tooltip("대시 이동 시간(초). 올리면 대시가 느려짐. 권장 0.15.")] private float _dashDuration = 0.15f;
        [SerializeField, Range(1f, 8f), Tooltip("대시 대기(초). 올리면 사용 빈도 감소. 권장 4.")] private float _dashCooldown = 4f;
        [SerializeField, Range(0.1f, 0.8f), Tooltip("이동 소음 전달 간격(초). 올리면 감지 갱신 감소. 권장 0.25.")] private float _noiseInterval = 0.25f;
        [SerializeField, Range(1f, 5f), Tooltip("피격 반동 거리(m). 올리면 고양이로부터 멀리 벗어남. 권장 3.")] private float _knockbackDistance = 3f;
        [SerializeField, Range(0.1f, 0.6f), Tooltip("피격 조작 불가(초). 올리면 반동이 오래 지속. 권장 0.3.")] private float _knockbackDuration = 0.3f;
        [SerializeField, Range(5f, 35f), Tooltip("중력 가속도(m/s²). 올리면 바닥에 빨리 붙음. 권장 20.")] private float _gravity = 20f;

        private CharacterController _controller;
        private GameController _game;
        private RadiusRing _noiseRing;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Vector3 _visualLocalPosition;
        private Quaternion _visualLocalRotation;
        private Vector3 _cameraForward;
        private Vector3 _cameraRight;
        private Vector3 _dashDirection;
        private Vector3 _knockbackDirection;
        private float _dashRemaining;
        private float _dashCooldownRemaining;
        private float _knockbackRemaining;
        private float _noiseTimer;
        private float _verticalVelocity;
        private float _noiseMultiplier = 1f;
        private float _currentDashCooldown;
        private float _motionTime;
        private float _lastMoveSpeed;
        private bool _initialized;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _hasDemoInput;
        private Vector2 _demoMovement;
        private bool _demoSneak;
        private bool _demoRun;
        private bool _demoDash;
#endif

        public float NoiseRadius { get; private set; }
        public float DashCooldown01 => _currentDashCooldown > 0f ? Mathf.Clamp01(_dashCooldownRemaining / _currentDashCooldown) : 0f;
        public bool IsDashing => _dashRemaining > 0f;
        public float NoiseMultiplier => _noiseMultiplier;

        /// <summary>씬의 명시적 연결을 받고 초기 위치와 카메라 기준 방향을 보존한다.</summary>
        public void Initialize(GameController game, Camera worldCamera)
        {
            if (_initialized) return;
            _initialized = true;
            _game = game;
            _controller = GetComponent<CharacterController>();
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            if (_visual == null) Debug.LogError("MouseController: _visual에 쥐 모델 자식을 연결해야 합니다.", this);
            else
            {
                _visualLocalPosition = _visual.localPosition;
                _visualLocalRotation = _visual.localRotation;
            }
            _cameraForward = Vector3.ProjectOnPlane(worldCamera.transform.forward, Vector3.up).normalized;
            _cameraRight = Vector3.ProjectOnPlane(worldCamera.transform.right, Vector3.up).normalized;
            if (_cameraForward.sqrMagnitude < 0.01f) _cameraForward = Vector3.forward;
            _noiseRing = RadiusRing.Create(transform, "소음 범위", new Color(0.3f, 0.75f, 0.85f));
            _noiseRing.transform.localPosition = Vector3.up * 0.045f;
            ResetRun();
        }

        /// <summary>새 판의 이동·성장·반동을 지우고 시작 위치로 돌아간다.</summary>
        public void ResetRun()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ClearDemoInput();
#endif
            _controller.enabled = false;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _controller.enabled = true;
            _dashRemaining = _dashCooldownRemaining = _knockbackRemaining = _noiseTimer = _motionTime = 0f;
            _verticalVelocity = 0f;
            _noiseMultiplier = 1f;
            _currentDashCooldown = _dashCooldown;
            NoiseRadius = 0f;
            if (_visual != null)
            {
                _visual.gameObject.SetActive(true);
                _visual.SetLocalPositionAndRotation(_visualLocalPosition, _visualLocalRotation);
            }
            _noiseRing.SetAppearance(0.35f, new Color(0.3f, 0.75f, 0.85f), true);
        }

        /// <summary>한 프레임의 정규화된 입력으로 이동하고 실제 움직임이 있을 때 소음을 낸다.</summary>
        public void Tick(float delta, Vector2 movement, bool sneak, bool run, bool dash)
        {
            if (_game.Phase != GamePhase.Playing || delta <= 0f) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_hasDemoInput)
            {
                movement = _demoMovement;
                sneak = _demoSneak;
                run = _demoRun;
                dash = _demoDash;
                _demoDash = false;
            }
#endif
            _motionTime += delta;
            _dashCooldownRemaining = Mathf.Max(0f, _dashCooldownRemaining - delta);
            _noiseTimer = Mathf.Max(0f, _noiseTimer - delta);
            Vector2 input = Vector2.ClampMagnitude(movement, 1f);
            Vector3 direction = _cameraForward * input.y + _cameraRight * input.x;
            float speed = sneak ? _sneakSpeed : run ? _runSpeed : _walkSpeed;
            Vector3 displacement;
            bool knockingBack = _knockbackRemaining > 0f;
            if (knockingBack)
            {
                float step = Mathf.Min(delta, _knockbackRemaining);
                displacement = _knockbackDirection * (_knockbackDistance / _knockbackDuration) * step;
                _knockbackRemaining -= step;
            }
            else
            {
                if (dash && _dashCooldownRemaining <= 0f && _dashRemaining <= 0f)
                {
                    _dashDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
                    _dashRemaining = _dashDuration;
                    _dashCooldownRemaining = _currentDashCooldown;
                    _game.EmitNoise(transform.position, _runNoise * _noiseMultiplier);
                    _game.PlaySound(GameSound.Dash);
                }
                if (_dashRemaining > 0f)
                {
                    float step = Mathf.Min(delta, _dashRemaining);
                    displacement = _dashDirection * (_dashDistance / _dashDuration) * step;
                    _dashRemaining -= step;
                    direction = _dashDirection;
                    speed = _dashDistance / _dashDuration;
                }
                else displacement = direction * speed * delta;
            }
            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity -= _gravity * delta;
            Vector3 before = transform.position;
            _controller.Move(displacement + Vector3.up * (_verticalVelocity * delta));
            float moved = Vector3.ProjectOnPlane(transform.position - before, Vector3.up).magnitude;
            _lastMoveSpeed = delta > 0f ? moved / delta : 0f;
            if (!knockingBack && direction.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), delta * 18f);
            NoiseRadius = moved > 0.001f && !knockingBack ? (IsDashing ? _runNoise : sneak ? _sneakNoise : run ? _runNoise : _walkNoise) * _noiseMultiplier : 0f;
            if (NoiseRadius > 0f && _noiseTimer <= 0f)
            {
                // 소음 이벤트 때만 고양이를 검사해 프레임별 전수 감지를 피한다.
                _game.EmitNoise(transform.position, NoiseRadius);
                _noiseTimer = _noiseInterval;
                if (!sneak) _game.PlaySound(GameSound.Footstep);
            }
            UpdateVisual(sneak);
        }

        /// <summary>피격 지점 반대 방향으로 밀려나는 동안 대시와 조작을 중단한다.</summary>
        public void KnockBack(Vector3 source)
        {
            _knockbackDirection = Vector3.ProjectOnPlane(transform.position - source, Vector3.up).normalized;
            if (_knockbackDirection.sqrMagnitude < 0.01f) _knockbackDirection = -transform.forward;
            _knockbackRemaining = _knockbackDuration;
            _dashRemaining = 0f;
        }

        /// <summary>성장으로 이동 소음을 최소 배율까지 낮춘다.</summary>
        public void UpgradeNoise() => _noiseMultiplier = Mathf.Max(_game.Rules.MinimumNoise, _noiseMultiplier - _game.Rules.NoiseUpgrade);

        /// <summary>성장으로 대시 대기를 줄이며 진행 중 대기도 새 상한에 맞춘다.</summary>
        public void UpgradeDash()
        {
            _currentDashCooldown = Mathf.Max(_game.Rules.MinimumDashCooldown, _currentDashCooldown - _game.Rules.DashUpgrade);
            _dashCooldownRemaining = Mathf.Min(_dashCooldownRemaining, _currentDashCooldown);
        }

        /// <summary>정지 상태에서는 깜빡임을 해제해 결과 화면의 쥐를 보이게 한다.</summary>
        public void SetSimulation(bool running)
        {
            if (!running && _visual != null) _visual.gameObject.SetActive(true);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>개발 빌드의 영상 시연이 실제 이동 경로에 입력을 공급한다.</summary>
        public void SetDemoInput(Vector2 movement, bool sneak, bool run, bool dash)
        {
            _hasDemoInput = true;
            _demoMovement = movement;
            _demoSneak = sneak;
            _demoRun = run;
            _demoDash |= dash;
        }

        /// <summary>자동 시연 입력을 해제하고 키보드 입력으로 돌아간다.</summary>
        public void ClearDemoInput()
        {
            _hasDemoInput = false;
            _demoDash = false;
        }
#endif

        /// <summary>사용자 모델에 작은 이동 리듬과 무적 점멸을 더한다.</summary>
        private void UpdateVisual(bool sneak)
        {
            Color color = sneak ? new Color(0.28f, 0.65f, 0.7f) : NoiseRadius > _walkNoise ? new Color(1f, 0.45f, 0.23f) : new Color(0.4f, 0.72f, 0.85f);
            _noiseRing.SetAppearance(Mathf.Max(0.35f, NoiseRadius), color, true);
            if (_visual == null) return;
            float bob = _lastMoveSpeed > 0.1f ? Mathf.Abs(Mathf.Sin(_motionTime * (_lastMoveSpeed + 5f))) * 0.045f : 0f;
            _visual.localPosition = _visualLocalPosition + Vector3.up * (bob - (sneak ? 0.045f : 0f));
            _visual.localRotation = _visualLocalRotation * Quaternion.Euler(0f, 0f, Mathf.Sin(_motionTime * 12f) * Mathf.Min(_lastMoveSpeed, 7f));
            _visual.gameObject.SetActive(!_game.IsPlayerInvulnerable || Mathf.FloorToInt(_motionTime * 12f) % 2 == 0);
        }
    }
}
