using UnityEngine;

namespace MidnightMouse
{
    /// <summary>소음·함정·미끼의 바닥 범위를 보여 주는 선 오브젝트에 붙인다.</summary>
    public sealed class RadiusRing : MonoBehaviour
    {
        private LineRenderer _line;
        private Material _material;
        private float _arc = 1f;

        /// <summary>원점을 기준으로 한 번만 원형 선을 만든다.</summary>
        public static RadiusRing Create(Transform parent, string objectName, Color color, float width = 0.035f)
        {
            var ringObject = new GameObject(objectName);
            ringObject.transform.SetParent(parent, false);
            var ring = ringObject.AddComponent<RadiusRing>();
            ring._line = ringObject.AddComponent<LineRenderer>();
            // Resources의 재질 참조로 Player 빌드에서도 표시용 셰이더를 보존한다.
            ring._material = new Material(Resources.Load<Material>("AssessmentFeedbackLine"));
            ring._line.sharedMaterial = ring._material;
            ring._line.useWorldSpace = false;
            ring._line.loop = true;
            ring._line.positionCount = 64;
            ring._line.widthMultiplier = width;
            ring._line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring._line.receiveShadows = false;
            for (int index = 0; index < 64; index++)
            {
                float angle = index * Mathf.PI * 2f / 64f;
                ring._line.SetPosition(index, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
            }
            ring.SetAppearance(1f, color, true);
            return ring;
        }

        /// <summary>기존 선을 재사용해 반경과 표시 상태를 바꾼다.</summary>
        public void SetAppearance(float radius, Color color, bool visible)
        {
            transform.localScale = new Vector3(radius, 1f, radius);
            _material.color = color;
            _line.enabled = visible;
        }

        /// <summary>반경을 유지한 채 원호 길이를 줄여 남은 비율을 표시한다.</summary>
        public void SetArc(float remaining)
        {
            remaining = Mathf.Clamp01(remaining);
            if (Mathf.Approximately(_arc, remaining)) return;
            _arc = remaining;
            _line.loop = false;
            _line.positionCount = 65;
            for (int index = 0; index <= 64; index++)
            {
                float angle = Mathf.PI * .5f - index * Mathf.PI * 2f * remaining / 64f;
                _line.SetPosition(index, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
            }
        }

        /// <summary>씬에서 만든 머티리얼을 씬 종료 시 해제한다.</summary>
        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
