using UnityEngine;
using UnityEngine.AI;

namespace MidnightMouse
{
    /// <summary>게임 씬의 Navigation 오브젝트에서 저장된 고양이 이동 지도를 등록합니다.</summary>
    [DefaultExecutionOrder(-500)]
    public sealed class LevelNavigation : MonoBehaviour
    {
        [SerializeField, Tooltip("이 씬의 고양이 이동 지도. 가구 배치가 바뀌면 씬 생성기로 다시 만듭니다.")]
        private NavMeshData _data;

        private NavMeshDataInstance _instance;

        /// <summary>씬을 활성화할 때 저장한 이동 지도를 한 번 등록합니다.</summary>
        private void OnEnable()
        {
            if (_data == null)
            {
                Debug.LogError("LevelNavigation: 이동 지도(_data)가 연결되지 않았습니다.", this);
                return;
            }

            if (!_instance.valid)
            {
                _instance = NavMesh.AddNavMeshData(_data, transform.position, transform.rotation);
            }
        }

        /// <summary>씬이 종료되면 등록한 이동 지도만 제거합니다.</summary>
        private void OnDisable()
        {
            if (_instance.valid)
            {
                _instance.Remove();
            }
        }
    }
}
