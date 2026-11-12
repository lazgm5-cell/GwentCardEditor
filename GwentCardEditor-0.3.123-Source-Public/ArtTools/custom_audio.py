"""Reversible custom premium WAV packages; no native game resource changes."""
from array import array
import copy
import json
import math
import os
from pathlib import Path
import re
import shutil
import tempfile
import uuid
import wave
from art_swap import digest, write_json, safe_path, streaming_assets, check_game_closed
from audio_swap import describe

PREFIX = "BepInEx/plugins/GwentCardEditor.CustomAudio/"
RUNTIME = ("GwentCardEditor.CustomAudio.dll", "NAudio.Core.dll", "NAudio.WinMM.dll", "NAudio-LICENSE.txt")


def normalize(source, output):
    source, output = Path(source).resolve(), Path(output).resolve()
    if output.exists() or source == output:
        raise ValueError("OutputExists")
    if source.stat().st_size > 50 * 1024 * 1024:
        raise ValueError("CustomAudioFormat")
    try:
        with wave.open(str(source), "rb") as reader:
            channels, width, rate, count = reader.getnchannels(), reader.getsampwidth(), reader.getframerate(), reader.getnframes()
            if channels not in (1, 2) or width != 2 or rate not in (44100, 48000) or not 0 < count <= rate * 120 or reader.getcomptype() != "NONE":
                raise ValueError("CustomAudioFormat")
            frames = reader.readframes(count)
            if len(frames) != count * channels * width:
                raise ValueError("CustomAudioFormat")
    except (wave.Error, EOFError) as error:
        raise ValueError("CustomAudioFormat") from error
    if channels == 1:
        samples = array("h", frames)
        frames = array("h", (sample for sample in samples for _ in range(2))).tobytes()
    with output.open("xb") as file:
        with wave.open(file, "wb") as writer:
            writer.setparams((2, 2, rate, count, "NONE", "not compressed"))
            writer.writeframes(frames)
    return dict(SampleRate=rate, Channels=2, Seconds=count / rate, Sha256=digest(output.read_bytes()))


def probe(source):
    source = Path(source).resolve()
    if source.stat().st_size > 50 * 1024 * 1024:
        raise ValueError("CustomAudioFormat")
    try:
        with wave.open(str(source), "rb") as reader:
            channels, rate, count = reader.getnchannels(), reader.getframerate(), reader.getnframes()
            if channels not in (1, 2) or reader.getsampwidth() != 2 or rate not in (44100, 48000) or count <= 0 or reader.getcomptype() != "NONE":
                raise ValueError("CustomAudioFormat")
            reader.setpos(count - 1)
            if len(reader.readframes(1)) != channels * 2:
                raise ValueError("CustomAudioFormat")
    except (wave.Error, EOFError) as error:
        raise ValueError("CustomAudioFormat") from error
    return dict(SampleRate=rate, Channels=channels, Seconds=count / rate)


def extract(source, output, start, end):
    source, output = Path(source).resolve(), Path(output).resolve()
    if source == output or output.exists():
        raise ValueError("OutputExists")
    info = probe(source)
    if any(isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) for v in (start, end)) or not (0 <= start < end <= info["Seconds"] + 1e-9 and end - start <= 120 + 1e-9):
        raise ValueError("CustomAudioRange")
    with wave.open(str(source), "rb") as reader:
        rate, channels = reader.getframerate(), reader.getnchannels()
        first, last = round(start * rate), round(end * rate)
        count = last - first
        if count <= 0 or count > 120 * rate:
            raise ValueError("CustomAudioRange")
        reader.setpos(first)
        frames = reader.readframes(count)
        if len(frames) != count * channels * 2:
            raise ValueError("CustomAudioFormat")
    if channels == 1:
        frames = array("h", (v for sample in array("h", frames) for v in (sample, sample))).tobytes()
    created = False
    try:
        with output.open("xb") as file:
            created = True
            with wave.open(file, "wb") as writer:
                writer.setparams((2, 2, rate, count, "NONE", "not compressed"))
                writer.writeframes(frames)
    except Exception:
        if created:
            output.unlink(missing_ok=True)
        raise
    return dict(SampleRate=rate, Channels=2, Seconds=count / rate, Sha256=digest(output.read_bytes()))


def edit(source, output, start, end, fade_in=0, fade_out=0):
    """Frame-aligned PCM crop with linear, non-overlapping fades."""
    source, output = Path(source).resolve(), Path(output).resolve()
    if output.exists() or source == output:
        raise ValueError("OutputExists")
    values = (start, end, fade_in, fade_out)
    if any(isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) for v in values):
        raise ValueError("CustomAudioRange")
    with tempfile.TemporaryDirectory(prefix="GwentAudioEdit-") as temporary:
        normalized = Path(temporary) / "source.wav"
        normalize(source, normalized)
        with wave.open(str(normalized), "rb") as reader:
            rate, count = reader.getframerate(), reader.getnframes()
            if not (0 <= start < end <= count / rate + 1e-9 and fade_in >= 0 and fade_out >= 0 and fade_in + fade_out <= end - start + 1e-9):
                raise ValueError("CustomAudioRange")
            first, last = round(start * rate), round(end * rate)
            length = last - first
            if length <= 0:
                raise ValueError("CustomAudioRange")
            reader.setpos(first)
            samples = array("h", reader.readframes(length))
        incoming, outgoing = round(fade_in * rate), round(fade_out * rate)
        if incoming + outgoing > length:
            raise ValueError("CustomAudioRange")
        for frame in range(length):
            scale = 1.0
            if incoming and frame < incoming:
                scale = min(scale, frame / max(1, incoming - 1))
            if outgoing and frame >= length - outgoing:
                scale = min(scale, (length - 1 - frame) / max(1, outgoing - 1))
            if scale != 1:
                for channel in (0, 1):
                    index = frame * 2 + channel
                    samples[index] = round(samples[index] * scale)
        created = False
        try:
            with output.open("xb") as file:
                created = True
                with wave.open(file, "wb") as writer:
                    writer.setparams((2, 2, rate, length, "NONE", "not compressed"))
                    writer.writeframes(samples.tobytes())
        except Exception:
            if created:
                output.unlink(missing_ok=True)
            raise
    return dict(SampleRate=rate, Channels=2, Seconds=length / rate, Sha256=digest(output.read_bytes()))


def client_root(client):
    assets = streaming_assets(client)
    if assets.name.lower() != "streamingassets" or assets.parent.name.lower() != "gwent_data" or not (assets.parent.parent / "Gwent.exe").is_file():
        raise ValueError("CustomAudioClient")
    return assets.parent.parent, assets


def supported(client):
    root, _ = client_root(client)
    return all((root / path).is_file() for path in ("BepInEx/core/BepInEx.Unity.IL2CPP.dll", "BepInEx/interop/Assembly-CSharp.dll"))


def configuration(raw):
    try:
        value = json.loads(raw.decode("utf-8-sig")) if raw is not None else dict(Version=1, Cards={})
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("CustomAudioConflict") from error
    if not isinstance(value, dict) or set(value) != {"Version", "Cards"} or type(value["Version"]) is not int or value["Version"] != 1 or not isinstance(value["Cards"], dict) or len(value["Cards"]) > 2000:
        raise ValueError("CustomAudioConflict")
    for key, clip in value["Cards"].items():
        if not re.fullmatch(r"[1-9][0-9]*", key) or int(key) > 2147483647 or not isinstance(clip, dict) or set(clip) != {"Sha256", "Gain", "Loop"}:
            raise ValueError("CustomAudioConflict")
        gain = clip["Gain"]
        if not isinstance(clip["Sha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", clip["Sha256"]) or type(clip["Loop"]) is not bool or type(gain) not in (int, float) or not math.isfinite(gain) or not 0 <= gain <= 1:
            raise ValueError("CustomAudioConflict")
    return value


def build(client, card_id, source, output, runtime, gain=1, loop=False):
    root, assets = client_root(client)
    describe(assets, card_id)
    if type(gain) not in (float, int) or not math.isfinite(gain) or not 0 <= gain <= 1 or type(loop) is not bool:
        raise ValueError("CustomAudioFormat")
    output, runtime = Path(output).resolve(), Path(runtime).resolve()
    if output.exists() or output.is_relative_to(root):
        raise ValueError("Output must be a new directory outside client")
    dlls = {name: (runtime / name).read_bytes() for name in RUNTIME}
    config_path = safe_path(root, PREFIX + "config.json")
    original_config = config_path.read_bytes() if config_path.exists() else None
    config = configuration(original_config)
    old_clip_hash = config["Cards"].get(str(card_id), {}).get("Sha256")
    dependencies = {}
    for key, clip in config["Cards"].items():
        path = PREFIX + "clips/" + key + ".wav"
        if digest(safe_path(root, path).read_bytes()) != clip["Sha256"]:
            raise ValueError("CustomAudioConflict")
        if key != str(card_id): dependencies[path] = clip["Sha256"]
    for name, data in dlls.items():
        path = safe_path(root, PREFIX + name)
        if path.exists() and path.read_bytes() != data:
            raise ValueError("CustomAudioConflict")
    clip_path = safe_path(root, PREFIX + "clips/" + str(card_id) + ".wav")
    if clip_path.exists() and str(card_id) not in config["Cards"]:
        raise ValueError("CustomAudioConflict")
    protected = digest((assets / "data_definitions").read_bytes())
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="custom-audio-", dir=output.parent) as temporary:
        work = Path(temporary)
        normalized = work / "preview.wav"
        info = normalize(source, normalized)
        config["Cards"][str(card_id)] = dict(Sha256=info["Sha256"], Gain=gain, Loop=loop)
        configuration((json.dumps(config)).encode("utf-8"))
        payload = {PREFIX + name: data for name, data in dlls.items()}
        payload[PREFIX + "config.json"] = (json.dumps(config, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        payload[PREFIX + "clips/" + str(card_id) + ".wav"] = normalized.read_bytes()
        records = []
        for relative, data in payload.items():
            path = safe_path(root, relative)
            before = path.read_bytes() if path.exists() else None
            if relative == PREFIX + "config.json" and before != original_config:
                raise ValueError("CustomAudioConflict")
            if relative == PREFIX + "clips/" + str(card_id) + ".wav" and (digest(before) if before is not None else None) != old_clip_hash:
                raise ValueError("CustomAudioConflict")
            if relative.removeprefix(PREFIX) in RUNTIME and before is not None and before != data:
                raise ValueError("CustomAudioConflict")
            for part, content in (("payload", data), ("backup", before)):
                if content is not None:
                    destination = safe_path(work / part, relative)
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    destination.write_bytes(content)
            records.append(dict(Path=relative, Before=digest(before) if before is not None else None, After=digest(data)))
        manifest = dict(Version=1, Scope="custom-premium-wav", TargetCard=card_id, ProtectedDataHash=protected,
                        Files=records, ProtectedClips=dependencies, Audio=info, RuntimeHashes={n: digest(d) for n, d in dlls.items()})
        write_json(work / "manifest.json", manifest)
        verify(work, runtime)
        if digest((assets / "data_definitions").read_bytes()) != protected:
            raise ValueError("CustomAudioConflict")
        for record in records:
            path = safe_path(root, record["Path"])
            if (digest(path.read_bytes()) if path.exists() else None) != record["Before"]:
                raise ValueError("CustomAudioConflict")
        for path, expected in dependencies.items():
            if digest(safe_path(root, path).read_bytes()) != expected: raise ValueError("CustomAudioConflict")
        os.replace(work, output)
    return manifest


def verify(package, runtime):
    package, runtime = Path(package).resolve(), Path(runtime).resolve()
    value = json.loads((package / "manifest.json").read_text(encoding="utf-8"))
    if value.get("Version") != 1 or value.get("Scope") != "custom-premium-wav" or type(value.get("TargetCard")) is not int or value["TargetCard"] <= 0:
        raise ValueError("CustomAudioConflict")
    expected = [PREFIX + n for n in RUNTIME] + [PREFIX + "config.json", PREFIX + "clips/" + str(value["TargetCard"]) + ".wav"]
    if sorted(r["Path"] for r in value["Files"]) != sorted(expected):
        raise ValueError("CustomAudioConflict")
    if value["RuntimeHashes"] != {n: digest((runtime / n).read_bytes()) for n in RUNTIME}:
        raise ValueError("CustomAudioConflict")
    for path in value["ProtectedClips"]:
        if not re.fullmatch(re.escape(PREFIX) + r"clips/[1-9][0-9]*\.wav", path): raise ValueError("CustomAudioConflict")
    for record in value["Files"]:
        name = record["Path"].removeprefix(PREFIX)
        if name in RUNTIME and (record["After"] != value["RuntimeHashes"][name] or record["Before"] not in (None, value["RuntimeHashes"][name])):
            raise ValueError("CustomAudioConflict")
        for part, key in (("payload", "After"), ("backup", "Before")):
            if record[key] is not None and digest(safe_path(package / part, record["Path"]).read_bytes()) != record[key]:
                raise ValueError("CustomAudioConflict")
    return value


def transfer(package, client, runtime, restore=False):
    package = Path(package).resolve()
    root, assets = client_root(client)
    value = verify(package, runtime)
    check_game_closed()
    if not restore and not supported(client): raise ValueError("CustomAudioLoader")
    if not restore and digest((assets / "data_definitions").read_bytes()) != value["ProtectedDataHash"]:
        raise ValueError("CustomAudioConflict")
    for path, expected in value["ProtectedClips"].items():
        if digest(safe_path(root, path).read_bytes()) != expected: raise ValueError("CustomAudioConflict")
    current, desired = ("After", "Before") if restore else ("Before", "After")
    part, undo = ("backup", "payload") if restore else ("payload", "backup")
    paths = [(safe_path(root, r["Path"]), r) for r in value["Files"]]
    for path, record in paths:
        actual = digest(path.read_bytes()) if path.exists() else None
        if actual != record[current]: raise ValueError("CustomAudioConflict")
    staged, committed = [], []
    try:
        for path, record in paths:
            if record[desired] is None:
                staged.append((None, path, record)); continue
            path.parent.mkdir(parents=True, exist_ok=True)
            temporary = path.with_name(path.name + ".card-editor-" + uuid.uuid4().hex)
            staged.append((temporary, path, record))
            shutil.copyfile(safe_path(package / part, record["Path"]), temporary)
        for temporary, path, record in staged:
            if temporary is None: path.unlink()
            else: os.replace(temporary, path)
            committed.append((path, record))
            actual = digest(path.read_bytes()) if path.exists() else None
            if actual != record[desired]: raise ValueError("CustomAudioConflict")
    except Exception:
        for path, record in reversed(committed):
            if record[current] is None: path.unlink(missing_ok=True)
            else: shutil.copyfile(safe_path(package / undo, record["Path"]), path)
        raise
    finally:
        for temporary, _, _ in staged:
            if temporary is not None: temporary.unlink(missing_ok=True)
    return dict(Completed=True, Restored=restore, Files=len(paths))
