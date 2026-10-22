using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MidnightMouse
{
    /// <summary>M1_Layout 루트에서 출입구와 후속 기능의 예약 자리를 보관하고 실제 씬 배치를 검사한다.</summary>
    public sealed class M1Layout : MonoBehaviour
    {
        [SerializeField, Tooltip("고양이 출입구 월드 좌표(m). 거실 왼쪽 벽, 주방 위 벽 순서. 벽 안쪽 보행면에 둠.")]
        private Vector3[] _petDoors = { new Vector3(-12f, 0f, 0f), new Vector3(1.7f, 0f, 9.1f) };
        [SerializeField, Tooltip("수면 자리 월드 좌표(m). 게으름냥 순서대로 배정. 함정 경계에서 2m 이상 떨어뜨림.")]
        private Vector3[] _sleepPoints = { new Vector3(-10.8f, 0f, -8f), new Vector3(8f, 0f, -5.5f) };
        [SerializeField, Tooltip("공 시작 예약 자리(m). 스위치에서 2.4m 이상, 컵 A까지 최대 6m 직선이 가능한 자리.")]
        private Vector3 _ballStart = new Vector3(0f, 0f, -5.7f);
        [SerializeField, Tooltip("컵 B 낙하 예약 자리(m). 주방 조리대 앞 보행면. 추위 판 기능은 후속 구현.")]
        private Vector3 _cupBCenter = new Vector3(7f, 0f, 5.5f);
        [SerializeField, Tooltip("컵 B 스위치 예약 자리(m). 낙하 중심에서 6m. 추위 판 기능은 후속 구현.")]
        private Vector3 _cupBSwitch = new Vector3(1f, 0f, 5.5f);
        [SerializeField, Tooltip("청소기 순환 경로의 부모. 첫 자식은 충전독이며 마지막에서 첫 지점으로 돌아옴.")]
        private Transform _vacuumRoute;
        [SerializeField, Tooltip("순찰·부스러기 지점의 부모. 모든 자식이 쥐 시작점에서 걸어서 도달 가능해야 함.")]
        private Transform _patrolPoints;
        [SerializeField, Range(0.05f, 0.4f), Tooltip("보행면 허용 오차(m). 올리면 바닥 밖 지점을 허용할 수 있음. 권장 0.2.")]
        private float _floorTolerance = 0.2f;
        [SerializeField, Range(0.1f, 1f), Tooltip("직선 통로 검사 반경(m). 올리면 좁은 통로를 막힌 것으로 판단. 권장 0.55.")]
        private float _routeClearance = 0.55f;
        [SerializeField, Range(1f, 10f), Tooltip("공 최대 이동 거리(m). 올리면 컵에 닿는 후보가 멀어짐. 기획 초기값 6.")]
        private float _ballDistance = 6f;
        [SerializeField, Range(1f, 10f), Tooltip("컵 스위치와 낙하 중심 거리(m). 올리면 유인 동선이 길어짐. 기획값 6.")]
        private float _cupSwitchDistance = 6f;
        [SerializeField, Range(0f, 5f), Tooltip("수면 자리와 위험 경계 간 여유(m). 올리면 잠든 고양이가 안전해짐. 기획값 2.")]
        private float _sleepClearance = 2f;
        [SerializeField, Tooltip("청소기 고리 길이 최소·최대(m). 짧으면 함정 상주가 쉬워짐. 기획 범위 24~33.")]
        private Vector2 _routeLengthRange = new Vector2(24f, 33f);

        public IReadOnlyList<Vector3> PetDoors => _petDoors;
        public IReadOnlyList<Vector3> SleepPoints => _sleepPoints;
        public Transform VacuumRoute => _vacuumRoute;
        public Vector3 BallStart => _ballStart;

        /// <summary>미래 선풍기 조건을 제외한 검사 1·2·3·5·6의 실패 내용을 반환한다.</summary>
        public List<string> ValidateLayout(GameController game)
        {
            var errors = new List<string>();
            if (game == null || _vacuumRoute == null || _patrolPoints == null || _petDoors.Length != 2 || _sleepPoints.Length != 2)
            { errors.Add("M1 배치 연결 누락: 게임·경로·순찰·출입구 2개·수면 2개가 필요합니다."); return errors; }
            TrapController cup = null, vacuum = null;
            foreach (var trap in game.Traps) { if (trap.Kind == TrapKind.Cup) cup = trap; else if (trap.Kind == TrapKind.Vacuum) vacuum = trap; }
            if (cup == null || vacuum == null || _vacuumRoute.childCount < 2)
            { errors.Add("M1 컵·청소기 또는 청소기 경로 연결 누락"); return errors; }
            Physics.SyncTransforms();
            Vector3 cupCenter = cup.transform.Find("EffectCenter").position;
            if (Mathf.Abs(FlatDistance(cup.transform.position, cupCenter) - _cupSwitchDistance) > 0.2f ||
                Mathf.Abs(FlatDistance(_cupBSwitch, _cupBCenter) - _cupSwitchDistance) > 0.2f) errors.Add("검사 1: 컵 A·B 스위치 거리");
            float length = 0f;
            for (int i = 0; i < _vacuumRoute.childCount; i++)
            {
                Vector3 a = _vacuumRoute.GetChild(i).position, b = _vacuumRoute.GetChild((i + 1) % _vacuumRoute.childCount).position;
                length += FlatDistance(a, b);
                if (!IsClearSegment(a, b, _routeClearance)) errors.Add("검사 5: 청소기 직선 장애물 " + i);
                if (NavMesh.Raycast(a, b, out var routeHit, NavMesh.AllAreas)) errors.Add("검사 5: 청소기 NavMesh 직선 단절 " + i + " / " + a + " → " + b + " / 경계 " + routeHit.position + " / 거리 " + routeHit.distance);
                CheckWalkable(errors, game.Player.transform.position, a, "청소기 " + i);
                foreach (Vector3 sleep in _sleepPoints)
                    if (DistanceToSegment(sleep, a, b) < game.Rules.VacuumRadius + _sleepClearance) errors.Add("검사 2: 수면과 청소기 경로 간격");
            }
            if (length < _routeLengthRange.x || length > _routeLengthRange.y || FlatDistance(_vacuumRoute.GetChild(0).position, vacuum.transform.position) > 0.01f)
                errors.Add("검사 5: 청소기 길이 또는 첫 지점 " + length);
            foreach (Vector3 sleep in _sleepPoints)
            {
                if (FlatDistance(sleep, cupCenter) < game.Rules.CupRadius + _sleepClearance ||
                    FlatDistance(sleep, _cupBCenter) < game.Rules.CupRadius + _sleepClearance || FlatDistance(sleep, _ballStart) < _sleepClearance)
                    errors.Add("검사 2: 수면과 컵·공 간격");
                CheckWalkable(errors, game.Player.transform.position, sleep, "수면 예약 자리");
            }
            bool ballCandidate = false;
            for (int angle = 0; angle < 360; angle++)
            {
                Vector3 end = _ballStart + new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad)) * _ballDistance;
                if (FlatDistance(end, cupCenter) > game.Rules.CupRadius || FlatDistance(end, cup.transform.position) < 4f || !IsClearSegment(_ballStart, end, 0.15f)) continue;
                foreach (Vector3 sleep in _sleepPoints) if (FlatDistance(end, sleep) <= 9f) ballCandidate = true;
                if (ballCandidate) break;
            }
            var path = new NavMeshPath();
            bool ballWalk = NavMesh.CalculatePath(_ballStart, cup.transform.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            float walkLength = 0f;
            for (int i = 1; i < path.corners.Length; i++) walkLength += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            if (!ballCandidate || !ballWalk || walkLength > 10f || FlatDistance(_ballStart, cup.transform.position) < 2.4f ||
                FlatDistance(_ballStart, vacuum.transform.position) < 2.4f || FlatDistance(_ballStart, _cupBSwitch) < 2.4f) errors.Add("검사 3: 공 최대 거리 직선·스위치 보행 경로·상호작용 간격");
            foreach (Vector3 door in _petDoors) CheckWalkable(errors, game.Player.transform.position, door, "펫도어");
            foreach (Transform point in _patrolPoints) CheckWalkable(errors, game.Player.transform.position, point.position, "순찰 " + point.name);
            foreach (Vector3 point in new[] { game.Player.transform.position, cupCenter, cup.transform.position, vacuum.transform.position, _cupBCenter, _cupBSwitch, _ballStart })
                CheckWalkable(errors, game.Player.transform.position, point, "시작·낙하·스위치·공");
            return errors;
        }

        /// <summary>실제 보행면과 연결 경로를 검사하며 장식 바닥과 이동 개체는 장애물에서 제외한다.</summary>
        private void CheckWalkable(List<string> errors, Vector3 start, Vector3 target, string label)
        {
            var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(target, out var hit, _floorTolerance, NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(start, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete || !IsClearSegment(target, target, 0.1f))
                errors.Add("검사 6: 보행 불가 " + label + " " + target);
        }

        /// <summary>환경 충돌체와의 겹침·구간 충돌을 검사한다. 가구와 벽은 Environment 또는 M1_Layout 아래에 둔다.</summary>
        public bool IsClearSegment(Vector3 start, Vector3 end, float radius)
        {
            Vector3 offset = Vector3.up * 0.7f;
            foreach (Collider collider in Physics.OverlapCapsule(start + offset, end + offset, radius, ~0, QueryTriggerInteraction.Ignore))
                if (collider.transform.root.name == "Environment_NightLivingRoom" || collider.transform.IsChildOf(transform)) return false;
            return true;
        }

        /// <summary>높이 차이를 제외한 바닥 거리를 구한다.</summary>
        private static float FlatDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;

        /// <summary>수면 자리와 닫힌 청소기 고리의 각 구간 사이 최단 거리를 구한다.</summary>
        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            point.y = a.y = b.y = 0f;
            Vector3 segment = b - a;
            return Vector3.Distance(point, a + segment * (segment.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(point - a, segment) / segment.sqrMagnitude) : 0f));
        }
    }
}
