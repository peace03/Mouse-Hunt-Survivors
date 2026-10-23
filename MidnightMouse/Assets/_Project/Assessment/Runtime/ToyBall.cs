using UnityEngine;
using UnityEngine.AI;

namespace MidnightMouse
{
    /// <summary>게임 루트의 공 한 개에서 직선 굴림·소음과 차기 예고를 관리한다.</summary>
    public sealed class ToyBall : MonoBehaviour
    {
        private GameController _game;
        private M1Layout _layout;
        private Transform _visual;
        private Material _material;
        private RadiusRing _outline, _destination, _noise;
        private LineRenderer _line;
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private readonly Collider[] _overlaps = new Collider[32];
        private Vector3 _start, _target;
        private float _noiseTimer, _pulseAge, _pulseRadius, _pulseDuration;
        private static readonly Color Gold = new Color(1f, 210f / 255f, 63f / 255f);
        public bool IsRolling { get; private set; }
        public bool CanKick => gameObject.activeSelf && !IsRolling && _game.Phase == GamePhase.Playing;
        private AssessmentRules Rules => _game.Rules;

        /// <summary>줄무늬 구체와 표시용 리소스를 한 번만 만든다.</summary>
        public void Initialize(GameController game, M1Layout layout)
        {
            _game = game;
            _layout = layout;
            foreach (float value in new[] { Rules.BallRadius, Rules.BallSpeed, Rules.BallDistance, Rules.BallRollRadius,
                Rules.BallNoiseInterval, Rules.BallStopRadius })
                if (!float.IsFinite(value) || value <= 0f) throw new System.InvalidOperationException("장난감 공 수치는 유한한 양수여야 합니다.");
            _start = layout.BallStart;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "줄무늬 장난감 공";
            visual.GetComponent<Collider>().enabled = false;
            Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.up * Rules.BallRadius;
            visual.transform.localScale = Vector3.one * (Rules.BallRadius * 2f);
            _visual = visual.transform;
            _material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.2f, .75f, .85f) };
            visual.GetComponent<Renderer>().sharedMaterial = _material;
            var stripe = RadiusRing.Create(_visual, "회전 무늬", Gold, .08f);
            stripe.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);
            stripe.SetAppearance(.51f, Gold, true);
            _outline = RadiusRing.Create(transform, "공 상호작용", Gold, .06f);
            _destination = RadiusRing.Create(transform, "공 도착 지점", Gold, .05f);
            _noise = RadiusRing.Create(transform, "공 소음", Gold);
            _line = new GameObject("공 예상 직선").AddComponent<LineRenderer>();
            _line.transform.SetParent(transform, false);
            _line.sharedMaterial = _outline.GetComponent<LineRenderer>().sharedMaterial;
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.widthMultiplier = .04f;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            ResetRun(false);
        }

        /// <summary>새 판에서는 이동·소리를 지우고 시작 자리로 돌린다.</summary>
        public void ResetRun(bool available)
        {
            IsRolling = false;
            _noiseTimer = _pulseDuration = 0f;
            transform.position = _start;
            _visual.localRotation = Quaternion.identity;
            ShowAim(false, null);
            _noise.SetAppearance(0f, Gold, false);
            gameObject.SetActive(available);
        }

        /// <summary>목적지를 공 기준으로 제한하고 실제 가구·벽과 보행 경계 앞에서 자른다.</summary>
        public Vector3 LandingPoint(Vector3 aim)
        {
            Vector3 start = transform.position;
            if (!float.IsFinite(aim.x) || !float.IsFinite(aim.y) || !float.IsFinite(aim.z)) return start;
            Vector3 offset = Vector3.ProjectOnPlane(aim - start, Vector3.up);
            float distance = Mathf.Min(offset.magnitude, Rules.BallDistance);
            if (distance <= .001f) return start;
            Vector3 direction = offset.normalized;
            Vector3 center = start + Vector3.up * (Rules.BallRadius + .04f);
            int overlaps = Physics.OverlapSphereNonAlloc(center, Rules.BallRadius, _overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (overlaps == _overlaps.Length) return start;
            for (int i = 0; i < overlaps; i++) if (IsObstacle(_overlaps[i])) return start;
            int count = Physics.SphereCastNonAlloc(center, Rules.BallRadius, direction, _hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == _hits.Length) return start;
            for (int i = 0; i < count; i++)
                if (IsObstacle(_hits[i].collider)) distance = Mathf.Min(distance, Mathf.Max(0f, _hits[i].distance - .01f));
            Vector3 end = start + direction * distance;
            if (NavMesh.Raycast(start, end, out var boundary, NavMesh.AllAreas))
                end = start + direction * Mathf.Max(0f, Vector3.Distance(start, boundary.position) - .01f);
            return end;
        }

        /// <summary>환경 루트의 가구·벽만 막고 바닥 장식과 이동 개체는 통과시킨다.</summary>
        private bool IsObstacle(Collider item) => item.bounds.max.y > Rules.BallRadius * 2f &&
            (item.transform.root.name == "Environment_NightLivingRoom" || item.transform.IsChildOf(_layout.transform));

        /// <summary>가까이 있는 모찌만 정지한 공을 차며 쿨타임은 두지 않는다.</summary>
        public bool TryKick(Vector3 aim)
        {
            if (!CanKick || Vector3.ProjectOnPlane(_game.Player.transform.position - transform.position, Vector3.up).sqrMagnitude >
                Rules.InteractionRadius * Rules.InteractionRadius) return false;
            _target = LandingPoint(aim);
            if ((_target - transform.position).sqrMagnitude < .0001f) return false;
            IsRolling = true;
            _noiseTimer = Rules.BallNoiseInterval;
            ShowAim(false, null);
            _game.PlaySound(GameSound.BallKick);
            return true;
        }

        /// <summary>구간을 검사하며 굴리고 목적지에 도착하면 그 자리에 둔다.</summary>
        public void Tick(float delta)
        {
            if (!gameObject.activeSelf || _game.Phase != GamePhase.Playing || delta <= 0f) return;
            _pulseAge += delta;
            if (IsRolling)
            {
                // 긴 프레임에도 소음 간격과 발생 위치가 달라지지 않도록 구간별로 진행한다.
                while (delta > 0f && IsRolling)
                {
                    _target = LandingPoint(_target);
                    float travelTime = Vector3.Distance(transform.position, _target) / Rules.BallSpeed;
                    float step = Mathf.Min(delta, Mathf.Min(travelTime, _noiseTimer));
                    Vector3 before = transform.position;
                    transform.position = Vector3.MoveTowards(before, _target, Rules.BallSpeed * step);
                    Vector3 movement = transform.position - before;
                    _visual.Rotate(Vector3.Cross(Vector3.up, movement).normalized, movement.magnitude / Rules.BallRadius * Mathf.Rad2Deg, Space.World);
                    delta -= step;
                    _noiseTimer -= step;
                    if (travelTime <= step + .00001f)
                    {
                        IsRolling = false;
                        Pulse(Rules.BallStopRadius, .7f, NoiseKind.Loud, GameSound.BallStop);
                    }
                    else if (_noiseTimer <= .00001f)
                    {
                        _noiseTimer = Rules.BallNoiseInterval;
                        Pulse(Rules.BallRollRadius, Rules.BallNoiseInterval, NoiseKind.Ball, GameSound.BallRoll);
                    }
                }
            }
            _noise.SetAppearance(Mathf.Lerp(.2f, _pulseRadius, Mathf.Clamp01(_pulseAge / Mathf.Max(.01f, _pulseDuration))),
                Gold, _pulseAge < _pulseDuration);
        }

        /// <summary>공 전용 반경을 쥐의 소음 배율과 분리하고 한 번의 소리 원을 시작한다.</summary>
        private void Pulse(float radius, float duration, NoiseKind kind, GameSound sound)
        {
            _pulseAge = 0f; _pulseRadius = radius; _pulseDuration = duration;
            _noise.transform.position = transform.position + Vector3.up * .06f;
            _game.EmitNoise(transform.position, radius, kind, fromBall: true);
            _game.PlaySound(sound);
        }

        /// <summary>E 대상일 때만 실제 이동과 같은 판정으로 목적지와 직선을 표시한다.</summary>
        public void ShowAim(bool selected, Vector3? aim)
        {
            selected &= CanKick;
            _outline.transform.localPosition = Vector3.up * .06f;
            _outline.SetAppearance(Rules.BallRadius + .08f, Gold, selected);
            bool preview = selected && aim.HasValue;
            _line.enabled = preview;
            _destination.SetAppearance(.2f, Gold, preview);
            if (!preview) return;
            Vector3 end = LandingPoint(aim.Value) + Vector3.up * .06f;
            _destination.transform.position = end;
            _line.SetPosition(0, transform.position + Vector3.up * .06f);
            _line.SetPosition(1, end);
        }

        /// <summary>씬 종료 시 공의 임시 재질을 해제한다.</summary>
        private void OnDestroy() { if (_material != null) Destroy(_material); }
    }
}
