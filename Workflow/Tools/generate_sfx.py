"""아트 디렉션의 37종 효과음을 합성한다. Python + 설치된 NumPy만 사용한다.

생성: python -B Workflow/Tools/generate_sfx.py
검사: python -B Workflow/Tools/generate_sfx.py --check
기존 음원과 다른 결과는 덮어쓰지 않는다. 조정할 때는 기존 결과를 먼저 검토한다.
"""
import argparse
import hashlib
import io
import json
import uuid
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "MidnightMouse/Assets/_Project/Art/Sound"
SPEC = ROOT / "Workflow/Design/아트/아트_디렉션.md"
RATE = 48000
RNG = np.random.default_rng(20261022)


def timeline(seconds):
    return np.arange(round(seconds * RATE)) / RATE


def envelope(t, attack=0.008, release=0.045):
    return np.minimum(t / attack, 1) * np.minimum((len(t) / RATE - t) / release, 1)


def tone(seconds, start, end=None, decay=4, vibrato=0):
    t = timeline(seconds)
    frequency = np.linspace(start, end if end is not None else start, len(t))
    frequency *= 1 + vibrato * np.sin(2 * np.pi * 7 * t)
    phase = 2 * np.pi * np.cumsum(frequency) / RATE
    return (np.sin(phase) + 0.13 * np.sin(2 * phase)) * envelope(t) * np.exp(-decay * t)


def noise(seconds, low, high):
    """주파수 대역을 제한한 주기적 잡음으로 기계음의 루프에도 사용한다."""
    n = round(seconds * RATE)
    f = np.fft.rfftfreq(n, 1 / RATE)
    spectrum = np.fft.rfft(RNG.normal(size=n))
    weight = np.exp(-0.5 * ((f - (low + high) / 2) / ((high - low) / 3)) ** 2)
    weight[0] = 0
    result = np.fft.irfft(spectrum * weight, n=n)
    return result / max(np.std(result), 1e-9) * 0.22


def hiss(seconds, low, high, decay=5):
    t = timeline(seconds)
    return noise(seconds, low, high) * envelope(t) * np.exp(-decay * t)


def mix(seconds, *events):
    result = np.zeros(round(seconds * RATE))
    for start, signal, gain in events:
        offset = round(start * RATE)
        size = min(len(signal), len(result) - offset)
        assert offset >= 0 and size > 0
        result[offset:offset + size] += signal[:size] * gain
    return result


def bell(seconds, frequency, brightness=1):
    t = timeline(seconds)
    result = np.zeros(len(t))
    for ratio, gain, decay in [(1, 1, 3), (2.76, .33, 5), (5.4, .13, 8), (8.93, .04, 13)]:
        result += gain * brightness ** (ratio / 3) * np.sin(2 * np.pi * frequency * ratio * t) * np.exp(-decay * t / seconds)
    return result * envelope(t, .002, .06)


def wood(seconds, frequency):
    return tone(seconds, frequency, frequency * .72, 23) + .25 * hiss(seconds, 350, 2600, 40)


def voice(seconds, pitches, formants=(750, 1600), rasp=.08):
    """성대의 배음에 입의 공명을 더하고 음정 변화로 고양이의 감정을 구분한다."""
    t = timeline(seconds)
    f = np.interp(t / seconds, np.linspace(0, 1, len(pitches)), pitches)
    f *= 1 + .018 * np.sin(2 * np.pi * 9 * t)
    phase = np.cumsum(f) * 2 * np.pi / RATE
    result = np.zeros(len(t))
    for harmonic in range(1, 15):
        frequency = f * harmonic
        gain = .12 / harmonic
        for center in formants:
            gain += np.exp(-.5 * ((frequency - center) / 240) ** 2) / harmonic
        result += np.sin(phase * harmonic) * gain
    result += rasp * noise(seconds, 600, 3000)
    return result * np.sin(np.pi * t / seconds) ** .65 * envelope(t)


def melody(notes, spacing=.12, tail=.6):
    return mix(spacing * (len(notes) - 1) + tail,
               *((i * spacing, bell(tail, note, .65), .8) for i, note in enumerate(notes)))


def build():
    """소재마다 다른 발음, 공명, 리듬을 합성한다."""
    RNG.bit_generator.state = np.random.default_rng(20261022).bit_generator.state
    clips = []

    def add(name, sound, loop=False, peak=-6):
        clips.append((name, sound, loop, peak))

    add("발소리", wood(.12, 210), peak=-15)
    t = timeline(.3)
    add("휙", noise(.3, 500, 4200) * np.sin(np.pi * t / .3) ** 2 + .15 * tone(.3, 500, 130, 3), peak=-9)
    add("찍", voice(.27, [1400, 2100, 1650, 1200], (2300, 3600), .02))
    add("뾱", tone(.16, 420, 1050, 16), peak=-9)
    add("톡", wood(.16, 680), peak=-11)
    add("스위치 밀기", mix(.38, (0, hiss(.23, 180, 1600, 4), .65), (.19, wood(.16, 300), 1), (.24, wood(.1, 560), .4)), peak=-9)
    add("통", tone(.3, 185, 82, 14) + .2 * hiss(.3, 500, 1800, 30))
    add("귀 쫑긋", mix(.28, (0, tone(.1, 700, 1100, 10), .4), (.07, tone(.18, 1100, 1550, 12), .8)), peak=-11)
    add("야옹", voice(.8, [330, 510, 540, 400, 270], (850, 1450)))
    add("놀란 울음", voice(.66, [470, 950, 760, 530, 350], (1200, 2300), .16))
    add("당황한 울음", mix(.92, (0, voice(.32, [400, 660, 430], (950, 1700)), 1), (.25, voice(.3, [450, 710, 460]), .85), (.5, voice(.42, [490, 740, 340]), .8)))
    add("냠냠", mix(.64, *((s, voice(.15, [170, 240, 170], (450, 900), .03) + .2 * wood(.15, 350), 1) for s in [0, .2, .41])), peak=-11)
    t = timeline(2.8)
    breath = (.5 - .5 * np.cos(2 * np.pi * t / 2.8)) ** 1.8
    snore = (np.sin(2 * np.pi * 65 * t) + .32 * np.sin(2 * np.pi * 130 * t)) * (1 + .4 * np.sin(2 * np.pi * 25 * t))
    add("코골이", breath * (snore * .3 + noise(2.8, 180, 1200)), True, -12)
    add("게으름냥 기상", mix(1.45, (0, voice(.72, [95, 150, 280, 350], (500, 1100), .14), 1), (.69, wood(.25, 155), .75), (.88, voice(.5, [430, 660, 390], (1000, 1900)), .85)))
    add("덮치기 준비", mix(.68, (0, tone(.65, 100, 230, .2, .08), .5), (.15, hiss(.5, 350, 1800, .2), .4)), peak=-9)
    add("헛디딤", mix(.58, (0, hiss(.28, 650, 3200, 4), .65), (.18, wood(.2, 210), 1), (.3, tone(.25, 200, 85, 9), .6)))
    add("작은 방울", bell(.72, 1800, .75), peak=-12)
    add("달그락", mix(.55, *((s, bell(.2, f, .7), g) for s, f, g in [(0, 940, .6), (.11, 1180, .4), (.24, 1030, .8), (.37, 1250, .45)])), peak=-9)
    events = [(0, wood(.32, 150), 1), (0, hiss(.5, 1200, 8000, 12), .9)]
    for i in range(18):
        s = .015 + (i / 18) ** 1.3 * .72
        events.append((s, bell(.25 + .35 * (1 - i / 18), RNG.uniform(850, 3400), .75), .6 * (1 - i / 22)))
    add("쨍그랑", mix(1.25, *events))
    t = timeline(2)
    motor = .5 * np.sin(2 * np.pi * 120 * t) + .2 * np.sin(2 * np.pi * 240 * t) + .1 * np.sin(2 * np.pi * 720 * t)
    add("웅~", motor * (1 + .07 * np.sin(2 * np.pi * 3 * t)) + noise(2, 300, 2600) * .55, True, -12)
    add("쿵", tone(.48, 105, 43, 12) + .5 * hiss(.48, 70, 800, 18))
    add("삑", mix(.22, (0, tone(.18, 1200, decay=1), 1)), peak=-11)
    t = timeline(2)
    fan = noise(2, 130, 2100) * (.75 + .25 * np.cos(2 * np.pi * 24 * t))
    add("윙~", fan + .17 * np.sin(2 * np.pi * 180 * t) + .07 * np.sin(2 * np.pi * 360 * t), True, -13)
    t = timeline(2)
    rolling = noise(2, 70, 650) * (.65 + .25 * np.sin(2 * np.pi * 7 * t) + .1 * np.sin(2 * np.pi * 19 * t))
    add("공 굴림", rolling + .07 * np.sin(2 * np.pi * 95 * t), True, -15)
    add("방울 딸랑", mix(1.05, (0, bell(.8, 1550, .8), 1), (.13, bell(.7, 2100, .7), .65), (.32, bell(.65, 1750, .75), .4)), peak=-10)
    add("띵", bell(.85, 1046.5, .5), peak=-9)
    add("괘종", mix(3.2, (0, bell(3.2, 196, .85), 1), (.06, bell(3, 293.66, .7), .35)))
    add("경보", mix(.95, *((s, tone(.22, 740, 980, .8), 1) for s in [0, .31, .62])))
    add("+N초", melody([659.25, 987.77, 1318.51], .075, .45), peak=-9)
    add("레벨업", melody([523.25, 659.25, 783.99, 1046.5], .13, .8))
    add("카드 넘김", mix(.3, (0, hiss(.23, 1100, 6500, 6), 1), (.13, wood(.13, 750), .25)), peak=-12)
    add("확인음", melody([783.99, 1046.5], .07, .28), peak=-10)
    add("성공음", melody([659.25, 783.99, 1046.5], .18, .8))
    add("치즈 쌓임", mix(.24, (0, wood(.14, 550), .5), (.035, tone(.18, 900, 1350, 16), .7)), peak=-11)
    add("해금", mix(1.65, (0, wood(.15, 420), .4), (.12, melody([523.25, 783.99, 1046.5, 1318.51], .1, 1.1), 1)))
    events = []
    for i in range(16):
        events.append((i * .105, bell(.28, 1568 if i % 2 else 1760, .9), 1 if i % 2 else .8))
    add("알람", mix(2.05, *events))
    add("패배", mix(1.5, (0, tone(.48, 392, 349, 3), .8), (.36, tone(.5, 311, 261, 3), .85), (.75, tone(.75, 196, 130.81, 4), 1)))
    return clips


def spec_rows():
    section = SPEC.read_text(encoding="utf-8").split("### 효과음 목록", 1)[1].split("## 4.", 1)[0]
    rows = []
    for line in section.splitlines():
        if line.startswith("| ") and not line.startswith("| 묶음"):
            group, name, usage = [cell.strip() for cell in line.strip("|").split("|")]
            rows.append((group, name, usage))
    assert len(rows) == 37, f"기획 목록 수 변경: {len(rows)}"
    return rows


def wav_bytes(signal, loop, peak_db):
    assert np.isfinite(signal).all() and np.max(np.abs(signal)) > 0
    signal = signal - np.mean(signal)
    if not loop:
        signal *= envelope(np.arange(len(signal)) / RATE, .003, .015)
    signal *= 10 ** (peak_db / 20) / np.max(np.abs(signal))
    pcm = np.rint(signal * 32767).astype("<i2")
    assert np.max(np.abs(pcm.astype(np.int32))) < 32767
    if loop:
        assert abs(int(pcm[0]) - int(pcm[-1])) < 700, "루프 경계 불연속"
    else:
        pcm[0] = pcm[-1] = 0
    data = io.BytesIO()
    with wave.open(data, "wb") as stream:
        stream.setparams((1, 2, RATE, len(pcm), "NONE", "not compressed"))
        stream.writeframes(pcm.tobytes())
    return data.getvalue()


def meta(path, audio=False):
    target = Path(str(path) + ".meta")
    if target.exists():
        return
    importer = "AudioImporter" if audio else "DefaultImporter"
    body = ""
    if audio:
        body = """  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 48000
    compressionFormat: 0
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {}
  forceToMono: 0
  normalize: 0
  ambisonic: 0
  loadInBackground: 0
  preLoadAudioData: 1
  3D: 1
"""
    target.write_text(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n{importer}:\n  externalObjects: {{}}\n{body}  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="기획 대응·재현성·PCM·루프 경계만 검사")
    args = parser.parse_args()
    clips, rows = build(), spec_rows()
    assert [c[0] for c in clips] == [r[1] for r in rows], "기획 순서/이름 불일치"
    prepared, manifest = [], []
    for i, ((name, sound, loop, peak), (group, _, usage)) in enumerate(zip(clips, rows), 1):
        filename = f"{i:02d}_{group}_{name}.wav"
        path = OUTPUT / filename
        data = wav_bytes(sound, loop, peak)
        if path.exists():
            assert path.read_bytes() == data, f"기존 파일과 다름, 덮어쓰기 중단: {path}"
        elif args.check:
            raise AssertionError(f"누락: {path}")
        with wave.open(io.BytesIO(data), "rb") as stream:
            assert (stream.getnchannels(), stream.getsampwidth(), stream.getframerate()) == (1, 2, RATE)
            samples = np.frombuffer(stream.readframes(stream.getnframes()), dtype="<i2").astype(float) / 32768
        assert np.sqrt(np.mean(samples ** 2)) > .003, f"무음에 가까움: {name}"
        assert abs(np.mean(samples)) < .002, f"DC 편향: {name}"
        prepared.append((path, data))
        manifest.append(dict(file=filename, group=group, name=name, usage=usage, loop=loop,
                             seconds=round(len(samples) / RATE, 3), peak_dbfs=round(20 * np.log10(np.max(np.abs(samples))), 2),
                             rms_dbfs=round(20 * np.log10(np.sqrt(np.mean(samples ** 2))), 2),
                             sha256=hashlib.sha256(data).hexdigest()))
    assert len({m["sha256"] for m in manifest}) == 37, "중복 파형"
    if args.check:
        assert json.loads((OUTPUT / "효과음_목록.json").read_text(encoding="utf-8")) == manifest
        for path, _ in prepared:
            assert Path(str(path) + ".meta").exists(), f"meta 누락: {path}"
        print(f"PASS: 37종 기획 대응, WAV 재현성, 포맷, 무음/클리핑/DC, 4종 루프 경계, meta")
        return
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for path, data in prepared:
        if not path.exists():
            path.write_bytes(data)
        meta(path, audio=True)
    listing = OUTPUT / "효과음_목록.json"
    listing.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    meta(listing)
    readme = OUTPUT / "README.md"
    lines = ["# 코드 합성 효과음 37종", "", "`Workflow/Design/아트/아트_디렉션.md`의 효과음 목록 순서와 이름을 따른다. 외부 음원 없이 진동파·대역 제한 잡음·공명·음정 변화로 합성했다.", "", "- 형식: 48 kHz, 16-bit PCM WAV, 모노. 위치 음향과 UI 양쪽에서 사용할 수 있다.", "- 음원별 최대 음량: -6~-15 dBFS. 발소리·지속음은 작게 설정했다. 실제 게임의 믹싱은 별도 조정이 필요하다.", "- 반복 재생: 코골이, 웅~, 윙~, 공 굴림. `AudioSource.loop`를 켜고 시작·종료에 짧은 볼륨 페이드를 적용한다. WAV 자체에는 Unity의 loop 설정이 저장되지 않는다.", "- 발소리는 한 걸음이다. 이동 방식에 따라 같은 파일의 음량과 재생 간격을 바꾼다.", "- 게으름냥 기상은 여러 마리가 동시에 깨도 한 번 재생한다. 6:00 승리에는 알람만 쓴다.", "- 게임의 기존 `GameAudio` 연결은 변경하지 않았다. 이번 산출물은 교체 가능한 오디오 에셋이다.", "", "## 재생성·검사", "", "저장소 루트에서 설치된 Python과 NumPy로 실행한다. 같은 시드로 재현하며 기존 WAV가 다르면 덮어쓰지 않고 중단한다.", "", "```powershell", "python -B Workflow/Tools/generate_sfx.py", "python -B Workflow/Tools/generate_sfx.py --check", "```", "", "`효과음_목록.json`에는 쓰는 때, 반복 여부, 길이, peak/RMS와 SHA-256을 기록한다. 자동 검사는 청감·Unity 재생 검증을 대신하지 않는다.", "", "| 파일 | 길이(초) | 반복 | 쓰는 때 |", "|---|---:|---|---|"]
    lines += [f'| {m["file"]} | {m["seconds"]:.3f} | {"예" if m["loop"] else "아니요"} | {m["usage"]} |' for m in manifest]
    readme.write_text("\n".join(lines) + "\n", encoding="utf-8")
    meta(readme)
    print(f"생성: {len(manifest)}종, {sum(m['seconds'] for m in manifest):.2f}초, {sum(len(data) for _, data in prepared):,} bytes")


if __name__ == "__main__":
    main()
