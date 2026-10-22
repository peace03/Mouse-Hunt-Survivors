using System;
using UnityEngine;

namespace MidnightMouse
{
    /// <summary>상호작용 지점 루트에서 컵 낙하 또는 청소기 경로 작동을 관리한다.</summary>
    public sealed class TrapController : MonoBehaviour
    {
        [SerializeField, Tooltip("함정 종류. 컵은 지연 타격, 청소기는 경로를 순환하며 타격함.")] private TrapKind _kind;
        [SerializeField, Tooltip("움직일 컵·청소기 모델 자식. 게임 루트와 별도로 이동함.")] private Transform _visual;
        [SerializeField, Tooltip("컵 착지 또는 청소기 범위 중심. 바닥 높이에 놓음.")] private Transform _effectCenter;
        [SerializeField, Tooltip("첫 점은 충전독. 순서대로 한 바퀴 방문하고 첫 점에서 멈추는 경로.")] private Transform[] _route;
        private GameController _game;
        private AssessmentRules _rules;
        private RadiusRing _interactionRing;
        private RadiusRing _effectRing;
        private LineRenderer _routeLine;
        private Material _routeMaterial;
        private Vector3 _visualStartPosition;
        private Quaternion _visualStartRotation;
        private Vector3 _cupLandingPosition;
        private float _cooldown;
        private float _activeRemaining;
        private bool _vacuumRunning;
        private float _pressRemaining;
        private float _pressNoiseTimer;
        private Vector3 _pressStart;
        private RadiusRing _pressRing;
        private int _activationId;
        private int _activationHits;
        private bool _activationOpen;
        private bool[] _hitCats;
        private int _routeIndex;
        private bool _initialized;
        private bool _nightEnabled = true;
        [SerializeField, Min(1), Tooltip("미해금 스위치의 안내 거리(m). 가까이 오면 회색 범위를 표시. 권장 4.")] private float _lockedHintRadius = 4f;

        public TrapKind Kind => _kind;
        public float CooldownRemaining => _cooldown;
        public bool IsActive => _activeRemaining > 0f || _vacuumRunning;
        public bool IsPressing => _pressRemaining > 0f;
        public float PressProgress => IsPressing ? 1f - _pressRemaining / _rules.SwitchPressDuration : 0f;
        public bool IsNightEnabled => _nightEnabled;
        public bool IsReady => _nightEnabled && _cooldown <= 0f && !IsActive && !IsPressing;
        public Vector3 EffectPosition => _kind == TrapKind.Vacuum && _visual != null ? _visual.position : _effectCenter.position;

        /// <summary>실제 모델과 경로를 검증하고 고정 타격 기록·범위 표시를 만든다.</summary>
        public void Initialize(GameController game)
        {
            if (_initialized) return;
            _initialized = true;
            _game = game;
            _rules = game.Rules;
            if (_visual == null || _effectCenter == null)
            {
                Debug.LogError("TrapController: _visual과 _effectCenter를 연결해야 합니다.", this);
                enabled = false;
                return;
            }
            if (_kind == TrapKind.Vacuum)
            {
                if (_route == null || _route.Length < 2 || Array.Exists(_route, point => point == null))
                {
                    Debug.LogError("TrapController: 청소기 _route에 두 개 이상의 유효한 경로점이 필요합니다.", this);
                    enabled = false;
                    return;
                }
                if (Vector3.ProjectOnPlane(_route[0].position - _visual.position, Vector3.up).sqrMagnitude > 0.01f)
                {
                    Debug.LogError("TrapController: 청소기 첫 경로점은 충전독이어야 합니다.", this);
                    enabled = false;
                    return;
                }
            }
            _visualStartPosition = _visual.position;
            _visualStartRotation = _visual.rotation;
            if (_kind == TrapKind.Cup)
            {
                // 사용자 모델의 피벗이 바닥에 있으므로 기울인 뒤의 실제 경계로 착지 높이를 정한다.
                _visual.SetPositionAndRotation(_effectCenter.position, _visualStartRotation * Quaternion.Euler(0f, 0f, -85f));
                float lowest = float.PositiveInfinity;
                foreach (Renderer renderer in _visual.GetComponentsInChildren<Renderer>()) lowest = Mathf.Min(lowest, renderer.bounds.min.y);
                _cupLandingPosition = _effectCenter.position + Vector3.up * (float.IsPositiveInfinity(lowest) ? 0.2f : _effectCenter.position.y + 0.03f - lowest);
                _visual.SetPositionAndRotation(_visualStartPosition, _visualStartRotation);
            }
            _hitCats = new bool[game.Cats.Count];
            _interactionRing = RadiusRing.Create(transform, "E 상호작용 범위", new Color(1f, 0.8f, 0.25f), 0.055f);
            _interactionRing.transform.localPosition = Vector3.up * 0.055f;
            _effectRing = RadiusRing.Create(transform, "함정 타격 범위", new Color(1f, 0.8f, 0.25f));
            _pressRing = RadiusRing.Create(game.transform, "스위치 누르는 소리", new Color(1f, 0.8f, 0.25f));
            CreateRouteLine();
            ResetRun();
        }

        /// <summary>새 판에 모델과 대기를 원래 상태로 되돌린다.</summary>
        public void ResetRun()
        {
            _cooldown = _activeRemaining = 0f;
            _vacuumRunning = false;
            _pressRemaining = _pressNoiseTimer = 0f;
            _activationId = _activationHits = 0;
            _activationOpen = false;
            _routeIndex = 0;
            if (_visual == null) return;
            _visual.SetPositionAndRotation(_visualStartPosition, _visualStartRotation);
            if (_hitCats != null) Array.Clear(_hitCats, 0, _hitCats.Length);
            UpdateIndicators();
        }

        /// <summary>이번 밤의 사용 여부를 적용하고 미해금 함정은 회색으로 표시한다.</summary>
        public void SetNightEnabled(bool value)
        {
            _nightEnabled = value;
            UpdateIndicators();
        }

        /// <summary>진행 상태·거리·쿨타임이 모두 유효할 때만 함정을 작동시킨다.</summary>
        public bool TryActivate()
        {
            if (!_initialized || !enabled || _game.Phase != GamePhase.Playing || !IsReady) return false;
            Vector3 difference = _game.Player.transform.position - transform.position;
            difference.y = 0f;
            if (difference.sqrMagnitude > _rules.InteractionRadius * _rules.InteractionRadius) return false;
            if (_kind == TrapKind.Vacuum && (_route == null || _route.Length == 0)) return false;
            _pressStart = _game.Player.transform.position;
            _pressRemaining = _rules.SwitchPressDuration;
            _pressNoiseTimer = 0f;
            UpdateIndicators();
            return true;
        }

        /// <summary>중앙 게임 시간으로만 쿨타임·낙하·청소 이동을 갱신한다.</summary>
        public void Tick(float delta)
        {
            if (_game.Phase != GamePhase.Playing || delta <= 0f || !float.IsFinite(delta) || !enabled || _visual == null) return;
            if (!_nightEnabled) { UpdateIndicators(); return; }
            float previousCooldown = _cooldown;
            _cooldown = Mathf.Max(0f, _cooldown - delta);
            if (IsPressing)
            {
                if (Vector3.ProjectOnPlane(_game.Player.transform.position - _pressStart, Vector3.up).sqrMagnitude > 0.0001f)
                {
                    _pressRemaining = 0f;
                    _game.RecordMeasurement("switch_cancel", name);
                }
                else
                {
                    _pressNoiseTimer -= delta;
                    if (_pressNoiseTimer <= 0f)
                    {
                        _game.EmitNoise(_game.Player.transform.position, _rules.SwitchNoiseRadius, NoiseKind.Normal, true);
                        _pressNoiseTimer = _rules.SwitchNoiseInterval;
                    }
                    _pressRemaining = Mathf.Max(0f, _pressRemaining - delta);
                    if (!IsPressing) BeginActivation();
                }
                UpdateIndicators();
                return;
            }
            if (IsActive)
            {
                _activeRemaining = Mathf.Max(0f, _activeRemaining - delta);
                if (_kind == TrapKind.Cup)
                {
                    float fraction = 1f - _activeRemaining / _rules.CupDelay;
                    // 식탁을 통과하지 않도록 가장자리 밖으로 밀린 뒤 바닥으로 떨어진다.
                    Vector3 edge = new Vector3(_cupLandingPosition.x, _visualStartPosition.y, _cupLandingPosition.z);
                    float fall = Mathf.Clamp01((fraction - 0.3f) / 0.7f);
                    _visual.position = fraction < 0.3f
                        ? Vector3.Lerp(_visualStartPosition, edge, fraction / 0.3f)
                        : Vector3.Lerp(edge, _cupLandingPosition, fall * fall);
                    _visual.rotation = _visualStartRotation * Quaternion.Euler(0f, 0f, -85f * fall);
                    if (_activeRemaining <= 0f)
                    {
                        HitCats(_effectCenter.position, _rules.CupRadius, _rules.CupDamage, _rules.CupStun, Vector3.zero, 0f);
                        RecordPendingActivation();
                    }
                }
                else
                {
                    MoveVacuum(delta);
                }
            }
            if (_kind == TrapKind.Cup && previousCooldown > 0f && _cooldown <= 0f)
                _visual.SetPositionAndRotation(_visualStartPosition, _visualStartRotation);
            UpdateIndicators();
        }

        /// <summary>고양이별 기록을 통해 한 작동에서 중복 타격을 막는다.</summary>
        private void HitCats(Vector3 center, float radius, int damage, float stun, Vector3 push, float pushDistance, Vector3? segmentEnd = null)
        {
            _game.BeginTrapHits();
            for (int index = 0; index < _game.Cats.Count; index++)
            {
                CatController cat = _game.Cats[index];
                if (_hitCats[index] || cat == null || !cat.CanReceiveHit) continue;
                Vector3 nearest = center;
                if (segmentEnd.HasValue)
                {
                    Vector3 segment = Vector3.ProjectOnPlane(segmentEnd.Value - center, Vector3.up);
                    float fraction = segment.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(cat.transform.position - center, segment) / segment.sqrMagnitude) : 0f;
                    nearest += segment * fraction;
                }
                Vector3 difference = cat.transform.position - nearest;
                difference.y = 0f;
                if (difference.sqrMagnitude > radius * radius) continue;
                CatState state = cat.State;
                if (cat.ApplyTrapHit(damage, stun, push.sqrMagnitude > 0.001f ? push : difference, pushDistance, transform.position))
                {
                    _hitCats[index] = true;
                    _activationHits++;
                    _game.RecordTrapHit(_kind, _activationHits);
                    _game.RecordMeasurement("trap_hit", name + "|" + _activationId + "|" + index + "|" + state + "|wind=0");
                }
            }
            _game.EndTrapHits();
        }

        /// <summary>청소기를 지정된 순환 경로 위에서 이동시킨다.</summary>
        private void MoveVacuum(float delta)
        {
            float remaining = _rules.VacuumSpeed * delta;
            while (_vacuumRunning && remaining > 0f)
            {
                Vector3 goal = _route[_routeIndex % _route.Length].position;
                goal.y = _visualStartPosition.y;
                Vector3 before = _visual.position;
                float distance = Vector3.Distance(before, goal);
                float step = Mathf.Min(remaining, distance);
                MoveVisual(goal, step / _rules.VacuumSpeed);
                HitVacuumSegment(before, _visual.position);
                remaining -= step;
                if (step < distance) break;
                _routeIndex++;
                if (_routeIndex > _route.Length)
                {
                    _vacuumRunning = false;
                    _visual.SetPositionAndRotation(_visualStartPosition, _visualStartRotation);
                    RecordPendingActivation();
                }
            }
        }

        /// <summary>충전독 원 안을 제외한 이동 선분만 검사해 저프레임에서도 타격을 놓치지 않는다.</summary>
        private void HitVacuumSegment(Vector3 from, Vector3 to)
        {
            Vector3 direction = Vector3.ProjectOnPlane(to - from, Vector3.up);
            Vector3 offset = Vector3.ProjectOnPlane(from - _visualStartPosition, Vector3.up);
            float lengthSquared = direction.sqrMagnitude;
            float radiusSquared = _rules.VacuumDockSafety * _rules.VacuumDockSafety;
            if (lengthSquared < 0.000001f) return;
            float projection = Vector3.Dot(offset, direction);
            float discriminant = projection * projection - lengthSquared * (offset.sqrMagnitude - radiusSquared);
            Vector3 push = Vector3.Cross(direction, Vector3.up);
            if (discriminant <= 0f)
            {
                HitCats(from, _rules.VacuumRadius, _rules.VacuumDamage, _rules.VacuumStun, push, _rules.VacuumPush, to);
                return;
            }
            float root = Mathf.Sqrt(discriminant);
            float enter = Mathf.Clamp01((-projection - root) / lengthSquared);
            float leave = Mathf.Clamp01((-projection + root) / lengthSquared);
            if (enter > 0f) HitCats(from, _rules.VacuumRadius, _rules.VacuumDamage, _rules.VacuumStun, push, _rules.VacuumPush, from + direction * enter);
            if (leave < 1f) HitCats(from + direction * leave, _rules.VacuumRadius, _rules.VacuumDamage, _rules.VacuumStun, push, _rules.VacuumPush, to);
        }

        /// <summary>누르기를 마친 순간부터 쿨타임과 작동별 타격 기록을 시작한다.</summary>
        private void BeginActivation()
        {
            _cooldown = _kind == TrapKind.Cup ? _rules.CupCooldown : _rules.VacuumCooldown;
            _activeRemaining = _kind == TrapKind.Cup ? _rules.CupDelay : 0f;
            _vacuumRunning = _kind == TrapKind.Vacuum;
            _routeIndex = 1;
            Array.Clear(_hitCats, 0, _hitCats.Length);
            _activationId++;
            _activationHits = 0;
            _activationOpen = true;
            _game.RecordMeasurement("trap_start", name + "|" + _activationId);
            _game.PlaySound(GameSound.Trap);
        }

        /// <summary>완료되거나 판 종료로 중단된 작동의 타격 수를 한 번만 기록한다.</summary>
        public void RecordPendingActivation()
        {
            if (!_activationOpen) return;
            _game.RecordMeasurement("trap_end", name + "|" + _activationId + "|hits=" + _activationHits);
            _activationOpen = false;
        }

        /// <summary>청소기 모델을 바닥 경로로 움직이고 진행 방향을 보게 한다.</summary>
        private void MoveVisual(Vector3 target, float delta)
        {
            Vector3 direction = target - _visual.position;
            _visual.position = Vector3.MoveTowards(_visual.position, target, _rules.VacuumSpeed * delta);
            if (direction.sqrMagnitude > 0.01f)
                _visual.rotation = Quaternion.Slerp(_visual.rotation, Quaternion.LookRotation(direction) * _visualStartRotation, delta * 10f);
        }

        /// <summary>실제 타격 범위와 청소 경로를 항상 표시한다.</summary>
        private void UpdateIndicators()
        {
            if (_interactionRing == null) return;
            bool visible = _nightEnabled || Vector3.ProjectOnPlane(_game.Player.transform.position - transform.position, Vector3.up).sqrMagnitude <= _lockedHintRadius * _lockedHintRadius;
            Color color = IsReady ? new Color(1f, 0.8f, 0.25f) : IsActive ? new Color(1f, 0.35f, 0.15f) : new Color(0.35f, 0.4f, 0.47f);
            _interactionRing.SetAppearance(_rules.InteractionRadius, color, visible);
            Vector3 center = EffectPosition;
            _effectRing.transform.position = new Vector3(center.x, 0.065f, center.z);
            _effectRing.SetAppearance(_kind == TrapKind.Cup ? _rules.CupRadius : _rules.VacuumRadius, color, visible);
            _pressRing.transform.position = _game.Player.transform.position + Vector3.up * 0.07f;
            _pressRing.SetAppearance(_rules.SwitchNoiseRadius, new Color(1f, 0.8f, 0.25f), IsPressing);
            if (_routeMaterial != null) _routeMaterial.color = color;
            if (_routeLine != null) _routeLine.enabled = visible;
        }

        /// <summary>경로 선은 시작 때 한 번만 생성해 청소기 유인 판단을 돕는다.</summary>
        private void CreateRouteLine()
        {
            var lineObject = new GameObject(_kind == TrapKind.Cup ? "스위치 연결선" : "청소기 경로");
            lineObject.transform.SetParent(transform, false);
            _routeLine = lineObject.AddComponent<LineRenderer>();
            _routeLine.useWorldSpace = true;
            _routeLine.positionCount = _kind == TrapKind.Cup ? 2 : _route.Length + 1;
            _routeLine.widthMultiplier = 0.025f;
            _routeLine.loop = false;
            _routeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _routeMaterial = new Material(Resources.Load<Material>("AssessmentFeedbackLine")) { color = new Color(0.35f, 0.68f, 0.65f) };
            _routeLine.sharedMaterial = _routeMaterial;
            _routeLine.SetPosition(0, transform.position + Vector3.up * 0.07f);
            if (_kind == TrapKind.Cup)
            {
                _routeLine.SetPosition(1, new Vector3(_effectCenter.position.x, 0.07f, _effectCenter.position.z));
                return;
            }
            float totalLength = 0f;
            for (int index = 0; index < _route.Length; index++)
            {
                Vector3 point = _route[(index + 1) % _route.Length].position;
                point.y = 0.07f;
                _routeLine.SetPosition(index + 1, point);
                totalLength += Vector3.Distance(_routeLine.GetPosition(index), point);
            }
            float safeFraction = Mathf.Min(0.49f, _rules.VacuumDockSafety / Mathf.Max(0.01f, totalLength));
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.18f, 0f), new GradientAlphaKey(0.18f, safeFraction), new GradientAlphaKey(1f, safeFraction + 0.001f),
                    new GradientAlphaKey(1f, 1f - safeFraction - 0.001f), new GradientAlphaKey(0.18f, 1f - safeFraction), new GradientAlphaKey(0.18f, 1f) });
            _routeLine.colorGradient = gradient;
        }

        /// <summary>씬에서 만든 경로 머티리얼을 해제한다.</summary>
        private void OnDestroy()
        {
            if (_routeMaterial != null) Destroy(_routeMaterial);
        }
    }
}
