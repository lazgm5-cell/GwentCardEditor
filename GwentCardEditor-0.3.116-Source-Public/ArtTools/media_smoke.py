"""Read-only source-client check; all installations run in an owned temporary clone."""
import json
from pathlib import Path
import shutil
import tempfile
import traceback
from unittest.mock import patch
import wave
from PIL import Image
import art_swap
import audio_swap
import custom_audio
import global_visuals
import fiery_frames


def snapshot(root):
    return {str(p.relative_to(root)): art_swap.digest(p.read_bytes()) for p in root.rglob("*") if p.is_file()}


def run(client, runtime, report):
    original_root, source = custom_audio.client_root(client)
    report = Path(report).resolve()
    if report.exists() or report.is_relative_to(original_root):
        raise ValueError("MediaTestReport")
    if Path(tempfile.gettempdir()).resolve().is_relative_to(original_root):
        raise ValueError("MediaTestReport")
    result = dict(Version=1, Scope="media-compatibility", Passed=False, Checks=[], InstalledIntoGame=False,
                  SourceClient=str(original_root), Report=str(report), SourceUnchanged=None)
    baseline = {}
    stage = "source"

    def check(condition, name):
        if not condition:
            raise ValueError(name)
        result["Checks"].append(name)

    def blocked(action, root, name):
        before = snapshot(root)
        try:
            action()
        except ValueError:
            check(snapshot(root) == before, name)
        else:
            raise ValueError(name + ": conflict was not blocked")

    try:
        if shutil.disk_usage(tempfile.gettempdir()).free < 512 * 1024 * 1024:
            raise ValueError("MediaTestSpace")
        _, art_name = art_swap.card(art_swap.templates(source), 202103)
        relatives = ["data_definitions", "bundledassets.json", global_visuals.GLOBAL,
                     "audio.json", "Audio/event_inclusion.json", "Audio/soundbank_inclusion.json",
                     *art_swap.resource_paths(art_name)]
        with tempfile.TemporaryDirectory(prefix="GwentMediaCheck-") as temporary:
            work = Path(temporary); root = work / "client"; assets = root / "Gwent_Data/StreamingAssets"
            assets.mkdir(parents=True)
            for relative in relatives:
                raw = art_swap.safe_path(source, relative).read_bytes()
                baseline[relative] = art_swap.digest(raw)
                path = art_swap.safe_path(assets, relative); path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(raw)
            check(all(art_swap.digest(art_swap.safe_path(source, p).read_bytes()) == h for p, h in baseline.items()), "source_snapshot_stable")
            for relative in ("Gwent.exe", "BepInEx/core/BepInEx.Unity.IL2CPP.dll", "BepInEx/interop/Assembly-CSharp.dll"):
                path = root / relative; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b"temporary test marker; never executed")
            before = snapshot(root)
            image = work / "test.png"; Image.new("RGBA", (128, 178), (40, 180, 100, 255)).save(image)
            sound = work / "test.wav"
            with wave.open(str(sound), "wb") as writer:
                writer.setparams((2, 2, 48000, 0, "NONE", "not compressed")); writer.writeframes(b"\0" * 38400)
            # The live client is read-only; process guards are bypassed only for this private clone.
            with patch("art_swap.check_game_closed"), patch("custom_audio.check_game_closed"):
                stage = "build_art"
                art = work / "art"
                built = art_swap.build(None, None, root, 202103, art, image)
                check(len(built["Checks"]) == 4, "art_four_qualities_verified")
                stale_frames = work / "stale-frames"
                global_visuals.build(root, stale_frames, bronze_frames="keep", gold_frames="rogue")
                art_swap.transfer(art, root)
                check(snapshot(root) != before, "art_installed_in_clone")
                blocked(lambda: art_swap.transfer(stale_frames, root), root, "stale_frame_package_blocked_without_writes")
                stage = "frames"
                frames = work / "frames"
                built = global_visuals.build(root, frames, bronze_frames="keep", gold_frames="rogue", border="2086")
                check([c["Texture"] for c in built["Checks"]] == ["CardFrame_Gold"], "independent_frame_export_verified")
                art_swap.transfer(frames, root)
                check(all(art_swap.digest((assets / r["Path"]).read_bytes()) == r["After"] for r in built["Files"]), "frames_installed_over_art")
                blocked(lambda: art_swap.transfer(art, root, True), root, "out_of_order_art_restore_blocked_without_writes")
                stage = "native_audio"
                native = work / "native-audio"
                built = audio_swap.build(root, 202103, 112103, native)
                art_swap.transfer(native, root)
                check(audio_swap.describe(root, 202103)["AudioId"] == built["AfterAudio"]["AudioId"], "native_audio_installed_after_visuals")
                blocked(lambda: art_swap.transfer(frames, root, True), root, "changed_card_archive_blocks_visual_restore_without_writes")
                stage = "custom_audio"
                custom = work / "custom-audio"
                custom_audio.build(root, 202103, sound, custom, runtime)
                unchanged = (assets / "data_definitions").read_bytes()
                custom_audio.transfer(custom, root, runtime)
                check((assets / "data_definitions").read_bytes() == unchanged, "custom_audio_preserves_card_archive")
                clip = root / custom_audio.PREFIX / "clips/202103.wav"
                expected_clip = clip.read_bytes(); clip.write_bytes(expected_clip + b"foreign edit")
                blocked(lambda: custom_audio.transfer(custom, root, runtime, True), root, "foreign_audio_edit_blocks_restore_without_writes")
                clip.write_bytes(expected_clip)
                stage = "animated_frames"
                before_animation = snapshot(root)
                animated = work / "animated-frames"
                built = fiery_frames.build(root, animated)
                check(len(built["Files"]) == 15, "animated_frame_package_verified")
                fiery_frames.transfer(animated, root)
                check(json.loads((root / fiery_frames.PREFIX / "config.json").read_text())["AllGold"], "animated_gold_scope_installed_in_clone")
                removed = work / "removed-animation"
                fiery_frames.build(root, removed, remove=True); fiery_frames.transfer(removed, root)
                fiery_frames.transfer(animated, root, restore=True)
                check(snapshot(root) == before_animation, "animated_frame_restore_after_removal_byte_identical")
                stage = "restore"
                custom_audio.transfer(custom, root, runtime, True)
                check(not clip.exists(), "custom_audio_restore_removes_owned_files")
                art_swap.transfer(native, root, True)
                art_swap.transfer(frames, root, True)
                art_swap.transfer(art, root, True)
                check(snapshot(root) == before, "complete_reverse_restore_byte_identical")
        result["TemporaryFilesRemoved"] = not Path(temporary).exists()
        check(result["TemporaryFilesRemoved"], "temporary_clone_removed")
        check(all(art_swap.digest(art_swap.safe_path(source, p).read_bytes()) == h for p, h in baseline.items()), "source_resources_unchanged")
        result["SourceUnchanged"] = True
        result["Passed"] = True
    except Exception:
        result["FailedStage"] = stage
        result["Error"] = traceback.format_exc()
        try:
            result["SourceUnchanged"] = all(art_swap.digest(art_swap.safe_path(source, p).read_bytes()) == h for p, h in baseline.items()) if baseline else None
        except OSError:
            result["SourceUnchanged"] = False
    report.parent.mkdir(parents=True, exist_ok=True)
    with report.open("x", encoding="utf-8") as file:
        json.dump(result, file, ensure_ascii=False, indent=2)
    return result
