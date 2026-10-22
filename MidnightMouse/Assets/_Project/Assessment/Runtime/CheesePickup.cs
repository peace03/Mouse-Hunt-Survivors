using UnityEngine;

namespace MidnightMouse
{
    /// <summary>고정 풀의 치즈 오브젝트에서 수명과 플레이어 흡수를 처리한다.</summary>
    public sealed class CheesePickup : MonoBehaviour
    {
        private GameController _game;
        private Vector3 _baseScale;
        private Vector3 _restPosition;
        private float _lifetime;
        private float _motionTime;
        private int _experience;
        private bool _attracting;
        private bool _initialized;

        public bool IsActive => gameObject.activeSelf;
        public bool IsCrumb => _experience == 1;

        /// <summary>풀이 만들어질 때 실제 치즈 크기를 저장한다.</summary>
        public void Initialize(GameController game)
        {
            if (_initialized) return;
            _initialized = true;
            _game = game;
            _baseScale = transform.localScale;
            gameObject.SetActive(false);
        }

        /// <summary>풀에서 치즈를 꺼내 보상과 수명을 설정한다.</summary>
        public void Spawn(Vector3 position, int experience, float lifetime)
        {
            _experience = experience;
            _lifetime = lifetime;
            _motionTime = Random.Range(0f, 6f);
            _attracting = false;
            _restPosition = position + Vector3.up * 0.18f;
            transform.position = _restPosition;
            transform.localScale = _baseScale * (experience > 1 ? _game.Rules.BigCheeseScale : 1f);
            gameObject.SetActive(true);
        }

        /// <summary>진행 시간으로 흔들림·유효 시간·흡수를 갱신한다.</summary>
        public void Tick(float delta)
        {
            if (_game.Phase != GamePhase.Playing || delta <= 0f || !IsActive) return;
            _motionTime += delta;
            if (_lifetime > 0f)
            {
                _lifetime -= delta;
                if (_lifetime <= 0f) { ResetRun(); return; }
            }
            Vector3 target = _game.Player.transform.position + Vector3.up * 0.3f;
            Vector3 difference = target - transform.position;
            difference.y = 0f;
            if (difference.sqrMagnitude <= _game.Rules.PickupRadius * _game.Rules.PickupRadius) _attracting = true;
            if (_attracting)
            {
                transform.position = Vector3.MoveTowards(transform.position, target, _game.Rules.PickupSpeed * delta);
                if ((target - transform.position).sqrMagnitude <= 0.25f * 0.25f)
                {
                    _game.CollectCheese(_experience);
                    ResetRun();
                }
            }
            else transform.position = _restPosition + Vector3.up * (Mathf.Sin(_motionTime * 3f) * 0.06f);
            transform.Rotate(0f, 60f * delta, 0f, Space.World);
        }

        /// <summary>다음 보상에 재사용하도록 풀로 돌려보낸다.</summary>
        public void ResetRun() => gameObject.SetActive(false);
    }
}
