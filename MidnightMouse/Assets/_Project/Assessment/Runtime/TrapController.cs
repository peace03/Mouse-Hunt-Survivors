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
        [SerializeField, Tooltip("청소기의 열린 바닥 순환 경로. 순서대로 방문하며 작동 중 반복함.")] private Transform[] _route;
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
        private bool _returning;
        private bool[] _hitCats;
        private int _routeIndex;
        private bool _initialized;

        public TrapKind Kind => _kind;
        public float CooldownRemaining => _cooldown;
        public bool IsActive => _activeRemaining > 0f;
        public bool IsReady => _cooldown <= 0f && !IsActive && !_returning;
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
            if (_kind == TrapKind.Vacuum && (_route == null || _route.Length == 0))
                Debug.LogError("TrapController: 청소기 _route에 이동 경로가 없습니다.", this);
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
            if (_kind == TrapKind.Vacuum) CreateRouteLine();
            ResetRun();
        }

        /// <summary>새 판에 모델과 대기를 원래 상태로 되돌린다.</summary>
        public void ResetRun()
        {
            _cooldown = _activeRemaining = 0f;
            _returning = false;
            _routeIndex = 0;
            if (_visual == null) return;
            _visual.SetPositionAndRotation(_visualStartPosition, _visualStartRotation);
            if (_hitCats != null) Array.Clear(_hitCats, 0, _hitCats.Length);
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
            _cooldown = _kind == TrapKind.Cup ? _rules.CupCooldown : _rules.VacuumCooldown;
            _activeRemaining = _kind == TrapKind.Cup ? _rules.CupDelay : _rules.VacuumDuration;
            _routeIndex = 0;
            Array.Clear(_hitCats, 0, _hitCats.Length);
            _game.PlaySound(GameSound.Trap);
            return true;
        }

        /// <summary>중앙 게임 시간으로만 쿨타임·낙하·청소 이동을 갱신한다.</summary>
        public void Tick(float delta)
        {
            if (_game.Phase != GamePhase.Playing || delta <= 0f || !enabled || _visual == null) return;
            float previousCooldown = _cooldown;
            _cooldown = Mathf.Max(0f, _cooldown - delta);
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
                    if (_activeRemaining <= 0f) HitCats(_effectCenter.position, _rules.CupRadius, _rules.CupDamage, _rules.CupStun, Vector3.zero, 0f);
                }
                else
                {
                    Vector3 before = _visual.position;
                    MoveVacuum(delta);
                    Vector3 direction = _visual.position - before;
                    HitCats(_visual.position, _rules.VacuumRadius, _rules.VacuumDamage, _rules.VacuumStun, Vector3.Cross(direction, Vector3.up), _rules.VacuumPush);
                    if (_activeRemaining <= 0f) _returning = true;
                }
            }
            else if (_kind == TrapKind.Vacuum && _returning)
            {
                // 돌아가는 동안은 타격하지 않으며 장애물 없는 기존 경로를 역순으로 따른다.
                ReturnVacuum(delta);
            }
            if (_kind == TrapKind.Cup && previousCooldown > 0f && _cooldown <= 0f)
                _visual.SetPositionAndRotation(_visualStartPosition, _visualStartRotation);
            UpdateIndicators();
        }

        /// <summary>고양이별 기록을 통해 한 작동에서 중복 타격을 막는다.</summary>
        private void HitCats(Vector3 center, float radius, int damage, float stun, Vector3 push, float pushDistance)
        {
            for (int index = 0; index < _game.Cats.Count; index++)
            {
                CatController cat = _game.Cats[index];
                if (_hitCats[index] || cat == null || !cat.CanReceiveHit) continue;
                Vector3 difference = cat.transform.position - center;
                difference.y = 0f;
                if (difference.sqrMagnitude > radius * radius) continue;
                if (cat.ApplyTrapHit(damage, stun, push.sqrMagnitude > 0.001f ? push : difference, pushDistance))
                    _hitCats[index] = true;
            }
        }

        /// <summary>청소기를 지정된 순환 경로 위에서 이동시킨다.</summary>
        private void MoveVacuum(float delta)
        {
            Vector3 goal = _route[_routeIndex].position;
            goal.y = _visualStartPosition.y;
            MoveVisual(goal, delta);
            if ((_visual.position - goal).sqrMagnitude < 0.01f) _routeIndex = (_routeIndex + 1) % _route.Length;
        }

        /// <summary>청소기가 지나온 경로점을 되짚은 뒤 충전독으로 복귀한다.</summary>
        private void ReturnVacuum(float delta)
        {
            if (_routeIndex >= 0)
            {
                int previous = (_routeIndex - 1 + _route.Length) % _route.Length;
                Vector3 goal = _route[previous].position;
                goal.y = _visualStartPosition.y;
                MoveVisual(goal, delta);
                if ((_visual.position - goal).sqrMagnitude < 0.01f) _routeIndex = previous == 0 ? -1 : previous;
                return;
            }
            MoveVisual(_visualStartPosition, delta);
            if ((_visual.position - _visualStartPosition).sqrMagnitude < 0.01f)
            {
                _returning = false;
                _visual.rotation = _visualStartRotation;
            }
        }

        /// <summary>청소기 모델을 바닥 경로로 움직이고 진행 방향을 보게 한다.</summary>
        private void MoveVisual(Vector3 target, float delta)
        {
            Vector3 direction = target - _visual.position;
            _visual.position = Vector3.MoveTowards(_visual.position, target, _rules.VacuumSpeed * delta);
            if (direction.sqrMagnitude > 0.01f)
                _visual.rotation = Quaternion.Slerp(_visual.rotation, Quaternion.LookRotation(direction) * _visualStartRotation, delta * 10f);
        }

        /// <summary>함정 근처에서는 실제 타격 범위와 청소 경로를 표시한다.</summary>
        private void UpdateIndicators()
        {
            if (_interactionRing == null) return;
            Color color = IsReady ? new Color(1f, 0.8f, 0.25f) : IsActive ? new Color(1f, 0.35f, 0.15f) : new Color(0.35f, 0.4f, 0.47f);
            _interactionRing.SetAppearance(_rules.InteractionRadius, color, true);
            Vector3 difference = _game.Player.transform.position - transform.position;
            difference.y = 0f;
            bool show = difference.sqrMagnitude <= _rules.TrapPreviewDistance * _rules.TrapPreviewDistance || IsActive;
            Vector3 center = EffectPosition;
            _effectRing.transform.position = new Vector3(center.x, 0.065f, center.z);
            _effectRing.SetAppearance(_kind == TrapKind.Cup ? _rules.CupRadius : _rules.VacuumRadius, color, show);
            if (_routeLine != null) _routeLine.enabled = show;
        }

        /// <summary>경로 선은 시작 때 한 번만 생성해 청소기 유인 판단을 돕는다.</summary>
        private void CreateRouteLine()
        {
            if (_route == null || _route.Length == 0) return;
            var lineObject = new GameObject("청소기 경로");
            lineObject.transform.SetParent(transform, false);
            _routeLine = lineObject.AddComponent<LineRenderer>();
            _routeLine.useWorldSpace = true;
            _routeLine.positionCount = _route.Length + 1;
            _routeLine.widthMultiplier = 0.025f;
            _routeLine.loop = true;
            _routeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _routeMaterial = new Material(Resources.Load<Material>("AssessmentFeedbackLine")) { color = new Color(0.35f, 0.68f, 0.65f) };
            _routeLine.sharedMaterial = _routeMaterial;
            _routeLine.SetPosition(0, transform.position + Vector3.up * 0.07f);
            for (int index = 0; index < _route.Length; index++)
            {
                Vector3 point = _route[index].position;
                point.y = 0.07f;
                _routeLine.SetPosition(index + 1, point);
            }
        }

        /// <summary>씬에서 만든 경로 머티리얼을 해제한다.</summary>
        private void OnDestroy()
        {
            if (_routeMaterial != null) Destroy(_routeMaterial);
        }
    }
}
