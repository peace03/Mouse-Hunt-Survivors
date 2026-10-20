using UnityEngine;

namespace MidnightMouse
{
    /// <summary>GameController 루트에서 시작 시 만든 짧은 합성 효과음을 재사용한다.</summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class GameAudio : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f), Tooltip("효과음 전체 음량(배). 올리면 소리가 커짐. 권장 0.3~0.6.")] private float _volume = 0.45f;
        private AudioSource _source;
        private AudioClip[] _clips;

        /// <summary>반복 재생 때 할당이 없도록 작은 파형을 시작 시 한 번 만든다.</summary>
        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _clips = new AudioClip[10];
            _clips[(int)GameSound.Pickup] = CreateTone("치즈", 700f, 1100f, 0.13f);
            _clips[(int)GameSound.Dash] = CreateTone("대시", 320f, 70f, 0.17f);
            _clips[(int)GameSound.Hit] = CreateTone("피격", 190f, 65f, 0.35f);
            _clips[(int)GameSound.Trap] = CreateTone("함정", 150f, 900f, 0.24f);
            _clips[(int)GameSound.Bait] = CreateTone("미끼", 480f, 220f, 0.15f);
            _clips[(int)GameSound.Upgrade] = CreateTone("성장", 520f, 1040f, 0.55f);
            _clips[(int)GameSound.Win] = CreateTone("아침", 500f, 1500f, 1.1f);
            _clips[(int)GameSound.Lose] = CreateTone("도망", 300f, 60f, 0.9f);
            _clips[(int)GameSound.Footstep] = CreateTone("발소리", 110f, 80f, 0.045f);
            _clips[(int)GameSound.Alert] = CreateTone("고양이", 340f, 520f, 0.22f);
        }

        /// <summary>재생 중인 소리를 끊지 않고 효과음을 겹쳐 재생한다.</summary>
        public void Play(GameSound sound)
        {
            if (_source == null || _clips == null) return;
            _source.PlayOneShot(_clips[(int)sound], _volume * (sound == GameSound.Footstep ? 0.12f : 1f));
        }

        /// <summary>부드러운 시작과 끝을 가진 짧은 합성음을 만든다.</summary>
        private static AudioClip CreateTone(string clipName, float startFrequency, float endFrequency, float duration)
        {
            const int sampleRate = 22050;
            int length = Mathf.CeilToInt(sampleRate * duration);
            var samples = new float[length];
            float phase = 0f;
            for (int index = 0; index < length; index++)
            {
                float fraction = (float)index / length;
                phase += Mathf.Lerp(startFrequency, endFrequency, fraction) * Mathf.PI * 2f / sampleRate;
                float envelope = Mathf.Min(1f, fraction * 30f) * Mathf.Pow(1f - fraction, 1.5f);
                samples[index] = (Mathf.Sin(phase) + Mathf.Sin(phase * 2.01f) * 0.22f) * envelope * 0.35f;
            }
            AudioClip clip = AudioClip.Create(clipName, length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>씬 종료 시 생성한 오디오 리소스를 해제한다.</summary>
        private void OnDestroy()
        {
            if (_clips == null) return;
            foreach (AudioClip clip in _clips) if (clip != null) Destroy(clip);
        }
    }
}
