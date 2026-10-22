using UnityEngine;
using UnityEngine.AI;

namespace MidnightMouse
{
    /// <summary>고양이 루트에서 게으름냥의 수면·기상·귀가를 기존 이동과 연결한다.</summary>
    public sealed partial class CatController
    {
        private Vector3 _sleepPosition;
        private bool _startAsleep;
        private bool _sleptFromBait;
        private float _wakeProgress;
        private RadiusRing _sleepGauge;
        private bool IsLazy => Kind == CatKind.Lazy;

        /// <summary>몸이 닿으면 즉시 깨고, 소음 원 안의 실제 걷기·달리기로 수면 게이지를 줄인다.</summary>
        private void TickSleeping(float delta)
        {
            if (!IsLazy) return;
            float contactRadius = _agent.radius + _game.Player.BodyRadius;
            if (HorizontalDistanceSquared(transform.position, _game.Player.transform.position) <= contactRadius * contactRadius)
            { WakeAndChase("contact"); return; }

            NoiseKind? movement = _game.Player.SleepDisturbance;
            float radius = _game.Player.NoiseRadius;
            if (!movement.HasValue || radius <= 0f || HorizontalDistanceSquared(transform.position, _game.Player.transform.position) > radius * radius)
                _wakeProgress = 0f;
            else _wakeProgress += delta / (movement == NoiseKind.Loud ? _definition.RunWakeSeconds : _definition.WalkWakeSeconds);
            if (_wakeProgress >= 1f) WakeAndChase("footsteps");
            else UpdateSleepGauge();
        }

        /// <summary>고양이 발밑의 별도 원호로 남은 수면량을 표시하고 깨어나면 숨긴다.</summary>
        private void UpdateSleepGauge()
        {
            _sleepGauge.SetAppearance(.75f, Color.Lerp(new Color(.65f, .7f, .9f), new Color(1f, .45f, .23f), _wakeProgress),
                IsLazy && State == CatState.Sleeping && _available);
            _sleepGauge.SetArc(1f - _wakeProgress);
        }

        /// <summary>모찌가 잠을 깨우면 소리 지점 대신 현재 모찌를 추적한다.</summary>
        private void WakeAndChase(string reason)
        {
            _bait = null;
            _target = _game.Player.transform.position;
            _lostTimer = _destinationTimer = 0f;
            SetState(CatState.Chase);
            MoveTo(_target, _game.ChaseSpeed(_definition));
            _game.PlaySound(GameSound.Wake);
            _game.RecordMeasurement("cat_wake", _slotIndex + "|" + reason + "|" + _game.RealElapsed);
        }

        /// <summary>최초 배치만 수면 자리에서 시작하고 재입장은 출입구에서 걸어간다.</summary>
        private void StartAvailableState()
        {
            if (IsLazy && _game.Stage < 5)
            {
                if (_startAsleep)
                {
                    if (NavMesh.SamplePosition(_sleepPosition, out var hit, .3f, NavMesh.AllAreas)) _agent.Warp(hit.position);
                    SleepNow("start");
                }
                else { SetState(CatState.Returning); MoveHome(); }
            }
            else { SetState(CatState.Patrol); ChoosePatrolPoint(); }
            _startAsleep = false;
        }

        /// <summary>새 목표가 생기면 취소할 수 있는 순찰 속도의 귀가를 시작한다.</summary>
        private void MoveHome()
        {
            _target = _sleepPosition;
            _destinationTimer = 0f;
            MoveTo(_target, PatrolSpeed);
        }

        /// <summary>잠드는 바로 그 순간 단계와 원인을 기록한다.</summary>
        private void SleepNow(string reason)
        {
            if (_game.Stage >= 5) { SetState(CatState.Patrol); ChoosePatrolPoint(); return; }
            _bait = null;
            SetState(CatState.Sleeping);
            _agent.ResetPath();
            _sleptFromBait = reason == "bait";
            _game.RecordMeasurement("cat_sleep", _slotIndex + "|" + reason);
        }

        /// <summary>기상 지점을 고정하며 추적 배율과 분리된 질주를 시작한다.</summary>
        private void BeginWakeRush(Vector3 point, string reason)
        {
            _bait = null;
            _target = point;
            SetState(CatState.WakeRush);
            _stateTimer = _definition.WakeDuration;
            _destinationTimer = 0f;
            MoveTo(_target, _definition.WakeSpeed);
            _game.PlaySound(GameSound.Wake);
            _game.RecordMeasurement("cat_wake", _slotIndex + "|" + reason + "|" + _game.RealElapsed);
        }

        /// <summary>5단계에는 수면·귀가·미끼 이동을 끝내고 다시 잠들지 않게 한다.</summary>
        public void ApplyStageRules()
        {
            if (!_available || !IsLazy || _game.Stage < 5) return;
            if (State == CatState.Sleeping)
            {
                _game.PlaySound(GameSound.Wake);
                _game.RecordMeasurement("cat_wake", _slotIndex + "|stage|" + _game.RealElapsed);
            }
            if (State == CatState.Sleeping || State == CatState.Returning || _bait != null)
            {
                _bait = null;
                SetState(CatState.Patrol);
                ChoosePatrolPoint();
            }
        }
    }
}
