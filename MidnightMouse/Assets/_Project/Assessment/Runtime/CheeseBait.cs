using UnityEngine;

namespace MidnightMouse
{
    /// <summary>고정 풀의 미끼 오브젝트에서 비행·착지·한 마리의 먹기를 처리한다.</summary>
    public sealed class CheeseBait : MonoBehaviour
    {
        private GameController _game;
        private RadiusRing _ring;
        private Transform _visual;
        private Material _material;
        private Vector3 _start;
        private Vector3 _target;
        private float _elapsed;
        private float _remaining;
        private bool _landed;
        private bool _claimed;

        public bool IsAvailable => gameObject.activeSelf && _landed && !_claimed;

        /// <summary>시작 때 한 번 모델과 소음 표시를 만든다.</summary>
        public void Initialize(GameController game)
        {
            _game = game;
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(visual.GetComponent<Collider>());
            visual.name = "미끼 치즈";
            visual.transform.SetParent(transform, false);
            visual.transform.localScale = new Vector3(0.28f, 0.2f, 0.24f);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            _material = new Material(shader) { color = new Color(1f, 0.75f, 0.16f) };
            visual.GetComponent<Renderer>().sharedMaterial = _material;
            _visual = visual.transform;
            _ring = RadiusRing.Create(transform, "미끼 소음", new Color(1f, 0.75f, 0.16f));
            gameObject.SetActive(false);
        }

        /// <summary>풀의 미끼를 출발점에서 목표점으로 던진다.</summary>
        public void Launch(Vector3 start, Vector3 target)
        {
            _start = start + Vector3.up * 0.4f;
            _target = target + Vector3.up * 0.15f;
            _elapsed = 0f;
            _remaining = _game.Rules.BaitLifetime;
            _landed = _claimed = false;
            gameObject.SetActive(true);
            transform.position = _start;
            _ring.SetAppearance(0.2f, new Color(1f, 0.75f, 0.16f), false);
        }

        /// <summary>포물선 비행 뒤 한 번만 소음을 내고 유효 시간을 진행한다.</summary>
        public void Tick(float delta)
        {
            if (_game.Phase != GamePhase.Playing || delta <= 0f || !gameObject.activeSelf) return;
            _elapsed += delta;
            if (!_landed)
            {
                float fraction = Mathf.Clamp01(_elapsed / _game.Rules.BaitFlightTime);
                transform.position = Vector3.Lerp(_start, _target, fraction) + Vector3.up * (4f * fraction * (1f - fraction) * 1.3f);
                _visual.Rotate(0f, 200f * delta, 0f, Space.Self);
                if (fraction < 1f) return;
                _landed = true;
                _elapsed = 0f;
                _game.EmitNoise(_target, _game.Rules.BaitNoiseRadius, this);
                _game.PlaySound(GameSound.Bait);
            }
            _remaining -= delta;
            _ring.transform.position = new Vector3(_target.x, _target.y - 0.1f, _target.z);
            _ring.SetAppearance(Mathf.Lerp(0.3f, _game.Rules.BaitNoiseRadius, Mathf.Clamp01(_elapsed / 0.7f)), new Color(1f, 0.75f, 0.16f), _elapsed < 0.7f);
            if (_remaining <= 0f) gameObject.SetActive(false);
        }

        /// <summary>처음 도착한 고양이 한 마리만 미끼를 먹게 한다.</summary>
        public bool TryClaim()
        {
            if (!IsAvailable || _game.Phase != GamePhase.Playing) return false;
            _claimed = true;
            _remaining = _game.Rules.BaitEatTime;
            return true;
        }

        /// <summary>다음 판에 풀을 재사용할 수 있도록 비활성화한다.</summary>
        public void ResetRun() => gameObject.SetActive(false);

        /// <summary>풀 종료 시 생성한 시각 리소스를 해제한다.</summary>
        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
