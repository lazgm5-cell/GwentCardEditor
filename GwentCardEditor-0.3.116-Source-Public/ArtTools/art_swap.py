"""Experimental standard-art replacement. Card data, premium and audio stay intact."""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import uuid
import xml.etree.ElementTree as ET
import zipfile
import zlib


def digest(data):
    return hashlib.sha256(data).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def streaming_assets(root):
    root = Path(root).resolve()
    matches = [p for p in (root, root / "Gwent_Data/StreamingAssets", root / "RogueMage_Data/StreamingAssets")
               if (p / "data_definitions").is_file()]
    if len(matches) != 1:
        raise ValueError("Expected one GWENT or Rogue Mage StreamingAssets directory")
    return matches[0]


def templates(sa):
    with zipfile.ZipFile(sa / "data_definitions") as archive:
        if archive.namelist().count("Templates.xml") != 1:
            raise ValueError("Missing or duplicate Templates.xml")
        return ET.fromstring(archive.read("Templates.xml"))


def card(root, card_id):
    matches = [c for c in root if c.get("Type") == "CardTemplate" and c.get("Id") == str(card_id)]
    if len(matches) != 1:
        raise ValueError(f"Missing or duplicate card {card_id}")
    value = matches[0]
    art_id = int(value.get("ArtId", "0"))
    if art_id <= 0:
        raise ValueError("Card has no supported art")
    return value, str(art_id * 10000)


def resource_paths(texture_name):
    base = "bundledassets/cardassets/textures/standard/"
    return [base + q + "/" + texture_name for q in ("high", "medium", "uber")] + [base + "low"]


def safe_path(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError("Path escapes package or client")
    return path


def bundle_body(data):
    import lz4.block
    import lzma
    def decompress(blob, mode, size):
        if mode == 0:
            result = blob
        elif mode == 1:
            result = lzma.decompress(blob)
        elif mode in (2, 3):
            result = lz4.block.decompress(blob, uncompressed_size=size)
        else:
            raise ValueError("Unsupported bundle compression")
        if len(result) != size:
            raise ValueError("Truncated bundle block")
        return result
    if not data.startswith(b"UnityFS\0"):
        raise ValueError("Expected UnityFS")
    pos = 8
    version = struct.unpack_from(">I", data, pos)[0]
    pos += 4
    for _ in range(2):
        pos = data.index(b"\0", pos) + 1
    size, compressed, uncompressed, flags = struct.unpack_from(">QIII", data, pos)
    if size != len(data):
        raise ValueError("Bundle size mismatch")
    pos += 20
    if version >= 7:
        pos = (pos + 15) // 16 * 16
    info_pos = len(data) - compressed if flags & 128 else pos
    info = decompress(data[info_pos:info_pos + compressed], flags & 63, uncompressed)
    if not flags & 128:
        pos += compressed
    if flags & 512:
        pos = (pos + 15) // 16 * 16
    count = struct.unpack_from(">I", info, 16)[0]
    cursor, blocks = 20, []
    for _ in range(count):
        raw_size, packed_size, mode = struct.unpack_from(">IIH", info, cursor)
        cursor += 10
        blocks.append(decompress(data[pos:pos + packed_size], mode & 63, raw_size))
        pos += packed_size
    return b"".join(blocks)


def texture(env, name):
    matches = texture_matches(env, name)
    if len(matches) != 1:
        raise ValueError(f"Expected one texture named {name}")
    return matches[0]


def texture_matches(env, name):
    matches = [(obj, obj.read()) for obj in env.objects if obj.type.name == "Texture2D"]
    return [(obj, value) for obj, value in matches if value.m_Name == name]


def object_hashes(env):
    return {(obj.assets_file.name, obj.path_id): digest(obj.get_raw_data()) for obj in env.objects}


def load_image(path):
    from PIL import Image, ImageOps
    import warnings
    path = Path(path)
    if path.stat().st_size > 50 * 1024 * 1024:
        raise ValueError("ArtImageTooLarge")
    with warnings.catch_warnings():
        warnings.simplefilter("error", Image.DecompressionBombWarning)
        with Image.open(path) as image:
            if image.format not in ("PNG", "JPEG", "WEBP") or getattr(image, "n_frames", 1) != 1:
                raise ValueError("ArtImageUnsupported")
            if image.width * image.height > 25_000_000 or min(image.size) < 16:
                raise ValueError("ArtImageTooLarge")
            pixels = ImageOps.exif_transpose(image).convert("RGBA")
            return Image.frombytes("RGBA", pixels.size, pixels.tobytes())


def normalize_image(source, output):
    image = load_image(source)
    output = Path(output)
    if output.exists() or output.resolve() == Path(source).resolve():
        raise ValueError("OutputExists")
    image.save(output, format="PNG")
    return dict(Width=image.width, Height=image.height)


def build(source, source_id, target, target_id, output, image_file=None):
    import UnityPy
    from PIL import Image, ImageOps
    source = streaming_assets(source) if image_file is None else None
    target, output = streaming_assets(target), Path(output).resolve()
    if output.exists() or source is not None and output.is_relative_to(source) or output.is_relative_to(target):
        raise ValueError("Output must be a new directory outside StreamingAssets")
    source_card, source_name = card(templates(source), source_id) if source is not None else (None, None)
    custom_image = load_image(image_file) if image_file is not None else None
    custom_hash = digest(Path(image_file).read_bytes()) if image_file is not None else None
    if custom_image is not None and abs(custom_image.width / custom_image.height - 128 / 178) > 0.01:
        raise ValueError("ArtCropRatio")
    target_templates = templates(target)
    target_card, target_name = card(target_templates, target_id)
    shared = [c.get("Id") for c in target_templates if c.get("ArtId") == target_card.get("ArtId")]
    if shared != [str(target_id)]:
        raise ValueError("Target art is shared; isolated ArtId remapping is required: " + ",".join(shared))
    catalog_path = target / "bundledassets.json"
    catalog = json.loads(catalog_path.read_text(encoding="utf-8-sig"))
    original_catalog = copy.deepcopy(catalog)
    paths, entries = resource_paths(target_name), {}
    for path in paths:
        matches = [entry for entry in catalog["Infos"] if entry["Path"] == path]
        if len(matches) != 1 or matches[0].get("AllDependencies"):
            raise ValueError("Missing, duplicate or dependent target resource: " + path)
        entries[path] = matches[0]
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="art-swap-build-", dir=output.parent) as temp:
        work = Path(temp)
        (work / "previews").mkdir()
        records, checks, source_hashes = [], [], {}
        protected = digest((target / "data_definitions").read_bytes())
        for path in paths:
            quality = "low" if path.endswith("/low") else path.split("/")[-2]
            if source is not None:
                source_path = source / ("bundledassets/cardassets/textures/standard/" + quality
                                        + ("/" + source_name if quality != "low" else ""))
                source_data = source_path.read_bytes()
                source_hashes[str(source_path)] = digest(source_data)
                _, source_texture = texture(UnityPy.load(source_data), source_name)
            original = (target / path).read_bytes()
            entry = entries[path]
            if entry["Size"] != len(original) or entry["FileCrc"] != zlib.crc32(original) & 0xffffffff:
                raise ValueError("Target catalog does not match resource: " + path)
            if entry["CRC"] != zlib.crc32(bundle_body(original)) & 0xffffffff:
                raise ValueError("Target decompressed CRC mismatch: " + path)
            env = UnityPy.load(original)
            if env.file.version_engine != "2022.3.62f2":
                raise ValueError("Unverified target Unity version: " + env.file.version_engine)
            target_matches = texture_matches(env, target_name)
            if not target_matches and quality == "low":
                print("Skipped low: no dedicated target texture", target_name, flush=True, file=sys.stderr)
                continue
            if len(target_matches) != 1:
                raise ValueError(f"Expected one texture named {target_name}")
            obj, value = target_matches[0]
            before_objects = object_hashes(env)
            size = (value.m_Width, value.m_Height)
            if custom_image is not None:
                if size[1] != size[0] * 2 or size[0] not in (128, 256, 512, 1024):
                    raise ValueError("ArtTextureLayout")
                image = Image.new("RGBA", size, (0, 0, 0, 255))
                visible = ImageOps.fit(custom_image, (size[0], size[0] * 178 // 128),
                                       method=Image.Resampling.LANCZOS, centering=(0.5, 0.5))
                image.alpha_composite(visible, (0, 0))
            else:
                image = source_texture.image.convert("RGBA")
                if abs(image.width / image.height - size[0] / size[1]) > 0.01:
                    raise ValueError("Aspect ratio differs; explicit crop approval required")
            if quality == "uber":
                image.save(work / "previews/source.png")
                value.image.save(work / "previews/original.png")
            if image.size != size:
                image = image.resize(size, Image.Resampling.LANCZOS)
            value.set_image(image, target_format=4)
            value.save()
            changed_key = (obj.assets_file.name, obj.path_id)
            result = env.file.save(packer="lz4")
            reopened = UnityPy.load(result)
            _, changed_texture = texture(reopened, target_name)
            if changed_texture.image.convert("RGBA").tobytes() != image.tobytes():
                raise ValueError("Pixel round-trip failed: " + quality)
            after_objects = object_hashes(reopened)
            if set(before_objects) != set(after_objects) or any(
                    before_objects[key] != after_objects[key] for key in before_objects if key != changed_key):
                raise ValueError("An unrelated Unity object changed: " + quality)
            if quality == "uber":
                changed_texture.image.save(work / "previews/replacement.png")
            entry.update(CRC=zlib.crc32(bundle_body(result)) & 0xffffffff,
                         FileCrc=zlib.crc32(result) & 0xffffffff, Size=len(result))
            for part, data in (("payload", result), ("backup", original)):
                dest = work / part / path
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(data)
            records.append(dict(Path=path, Before=digest(original), After=digest(result)))
            checks.append(dict(Quality=quality, Width=size[0], Height=size[1],
                               Pixels=digest(image.tobytes()), PreservedObjects=len(before_objects) - 1))
            print("Verified", quality, size, "preserved objects", len(before_objects) - 1, flush=True, file=sys.stderr)
        expected = copy.deepcopy(original_catalog)
        for entry in expected["Infos"]:
            if entry["Path"] in entries:
                entry.update({key: entries[entry["Path"]][key] for key in ("CRC", "FileCrc", "Size")})
        if expected != catalog:
            raise ValueError("Unrelated catalog entries changed")
        before_catalog = catalog_path.read_bytes()
        result_catalog = (json.dumps(catalog, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        (work / "payload/bundledassets.json").write_bytes(result_catalog)
        (work / "backup/bundledassets.json").write_bytes(before_catalog)
        records.append(dict(Path="bundledassets.json", Before=digest(before_catalog), After=digest(result_catalog)))
        if digest((target / "data_definitions").read_bytes()) != protected:
            raise ValueError("Target data changed during build")
        manifest = dict(Version=1, Scope="standard-art-only", SourceCard=source_id, SourceArtId=source_card.get("ArtId") if source_card is not None else None,
                        TargetCard=target_id, TargetArtId=target_card.get("ArtId"), TargetClient=str(target),
                        ProtectedDataHash=protected, SourceHashes=source_hashes, Files=records, Checks=checks,
                        PremiumUnchanged=True, AudioUnchanged=True, SourceImageHash=custom_hash,
                        ContentRatio="128:178" if custom_image is not None else None)
        write_json(work / "manifest.json", manifest)
        verify(work)
        os.replace(work, output)
    return manifest


def preview_image(target, target_id, output):
    import UnityPy
    target = streaming_assets(target)
    _, texture_name = card(templates(target), target_id)
    failures = []
    for relative in resource_paths(texture_name):
        path = target / relative
        if not path.is_file():
            continue
        try:
            _, value = texture(UnityPy.load(path.read_bytes()), texture_name)
            image = value.image
            if image is None:
                raise ValueError("Texture has no decoded image")
            output = Path(output)
            output.parent.mkdir(parents=True, exist_ok=True)
            image.convert("RGBA").save(output, format="PNG")
            return dict(Output=str(output), Quality=relative.split("/")[-2], Width=image.width, Height=image.height)
        except Exception as exc:
            failures.append(f"{relative}: {exc}")
    raise ValueError("CardArtUnavailable: " + " | ".join(failures[-3:]))


def verify(package):
    package = Path(package).resolve()
    manifest = json.loads((package / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("Scope") in ("global-card-visuals-animated", "animated-frame-removal"):
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import fiery_frames
        return fiery_frames.verify(package)
    if manifest.get("Version") != 1:
        raise ValueError("Unknown package format")
    if manifest.get("Scope") == "standard-art-only":
        name = str(int(manifest["TargetArtId"]) * 10000)
        qualities = [check.get("Quality") for check in manifest.get("Checks", [])]
        if (len(qualities) not in (3, 4) or qualities[:3] != ["high", "medium", "uber"]
                or qualities[3:] not in ([], ["low"])):
            raise ValueError("Unexpected standard-art quality set")
        expected = [resource_paths(name)[("high", "medium", "uber", "low").index(q)]
                    for q in qualities] + ["bundledassets.json"]
    elif manifest.get("Scope") == "static-cardback-image":
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import cardback_swap
        expected = cardback_swap.paths(manifest["TargetCardback"]) + ["bundledassets.json"]
        if manifest.get("GeometryHash") != cardback_swap.MESH_HASH or manifest.get("ContentRatio") != "2:3":
            raise ValueError("CardbackGeometry")
    elif manifest.get("Scope") == "global-card-visuals":
        expected = ["bundledassets/dependencies/shared/global", "bundledassets.json"]
    elif manifest.get("Scope") == "native-card-audio-reference":
        expected = ["data_definitions"]
        if set(manifest.get("ProtectedAudioMetadata", {})) != {"audio.json", "Audio/event_inclusion.json", "Audio/soundbank_inclusion.json"}:
            raise ValueError("Unexpected protected audio paths")
    else:
        raise ValueError("Unknown package format")
    if sorted(record["Path"] for record in manifest["Files"]) != sorted(expected):
        raise ValueError("Unexpected package file paths")
    for record in manifest["Files"]:
        for part, key in (("payload", "After"), ("backup", "Before")):
            if digest(safe_path(package / part, record["Path"]).read_bytes()) != record[key]:
                raise ValueError("Package checksum mismatch: " + record["Path"])
    return manifest


def check_game_closed():
    # tasklist uses the Windows console code page, not Python's forced UTF-8 mode.
    running = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Gwent.exe", "/FO", "CSV"], capture_output=True, check=True)
    if not isinstance(running.stdout, bytes) or not running.stdout:
        raise ValueError("ArtProcessCheckFailed")
    if b'"gwent.exe"' in running.stdout.lower():
        raise ValueError("Close Gwent.exe before changing art files")


def transfer(package, client, restore=False):
    package, target = Path(package).resolve(), streaming_assets(client)
    manifest = verify(package)
    if manifest["Scope"] in ("global-card-visuals-animated", "animated-frame-removal"):
        import fiery_frames
        return fiery_frames.transfer(package, client, restore)
    check_game_closed()
    if manifest["Scope"] == "static-cardback-image" and not restore:
        import cardback_swap
        cardback_swap.geometry(target)
    if restore and all(digest(safe_path(target, record["Path"]).read_bytes()) == record["Before"] for record in manifest["Files"]):
        return dict(Completed=True, Restored=True, AlreadyRestored=True, Files=0, Target=str(target))
    if manifest["Scope"] == "native-card-audio-reference":
        for path, expected in manifest["ProtectedAudioMetadata"].items():
            if digest(safe_path(target, path).read_bytes()) != expected:
                raise ValueError("Client changed; audio metadata differs")
    elif digest((target / "data_definitions").read_bytes()) != manifest["ProtectedDataHash"]:
        raise ValueError("Client card data differs from package source")
    current_key, desired_key = ("After", "Before") if restore else ("Before", "After")
    source_part = "backup" if restore else "payload"
    paths = [(safe_path(target, record["Path"]), record) for record in manifest["Files"]]
    for path, record in paths:
        if digest(path.read_bytes()) != record[current_key]:
            raise ValueError("Client changed; refusing overwrite: " + record["Path"])
    staged, committed = [], []
    try:
        for path, record in paths:
            temporary = path.with_name(path.name + ".art-swap-" + uuid.uuid4().hex)
            staged.append((temporary, path, record))
            shutil.copyfile(safe_path(package / source_part, record["Path"]), temporary)
        for temporary, path, record in staged:
            os.replace(temporary, path)
            committed.append((path, record))
            if digest(path.read_bytes()) != record[desired_key]:
                raise ValueError("Installed checksum mismatch")
    except Exception:
        undo_part = "payload" if restore else "backup"
        for path, record in reversed(committed):
            shutil.copyfile(safe_path(package / undo_part, record["Path"]), path)
        raise
    finally:
        for temporary, _, _ in staged:
            temporary.unlink(missing_ok=True)
    return dict(Completed=True, Restored=restore, Files=len(paths), Target=str(target))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dependencies", type=Path)
    sub = parser.add_subparsers(dest="command", required=True)
    builder = sub.add_parser("build")
    for name in ("source", "target", "output"):
        builder.add_argument("--" + name, required=True, type=Path)
    for name in ("source-card", "target-card"):
        builder.add_argument("--" + name, required=True, type=int)
    image_builder = sub.add_parser("image-build")
    for name in ("image", "target", "output"):
        image_builder.add_argument("--" + name, required=True, type=Path)
    image_builder.add_argument("--target-card", required=True, type=int)
    image_preview = sub.add_parser("image-preview")
    image_preview.add_argument("--target", required=True, type=Path)
    image_preview.add_argument("--target-card", required=True, type=int)
    image_preview.add_argument("--output", required=True, type=Path)
    back_catalog = sub.add_parser("cardback-catalog")
    back_catalog.add_argument("--target", required=True, type=Path)
    back_builder = sub.add_parser("cardback-image-build")
    for name in ("image", "target", "output"):
        back_builder.add_argument("--" + name, required=True, type=Path)
    back_builder.add_argument("--target-cardback", required=True, type=int)
    normalizer = sub.add_parser("normalize")
    normalizer.add_argument("--image", required=True, type=Path)
    normalizer.add_argument("--output", required=True, type=Path)
    visuals = sub.add_parser("global-build")
    fiery = sub.add_parser("fiery-build")
    for name in ("client", "output"):
        fiery.add_argument("--" + name, required=True, type=Path)
    fiery.add_argument("--bronze-frames", choices=("keep", "none", "rogue"), default="keep")
    for name in ("banners", "rarity", "provision", "bread"):
        fiery.add_argument("--hide-" + name, action="store_true")
    fiery_remove = sub.add_parser("fiery-remove")
    for name in ("client", "output"):
        fiery_remove.add_argument("--" + name, required=True, type=Path)
    media_test = sub.add_parser("media-check")
    for name in ("client", "runtime", "report"):
        media_test.add_argument("--" + name, required=True, type=Path)
    for name in ("target", "output"):
        visuals.add_argument("--" + name, required=True, type=Path)
    visuals.add_argument("--frames", choices=("keep", "none", "rogue"), default="keep")
    for name in ("bronze-frames", "gold-frames"):
        visuals.add_argument("--" + name, choices=("keep", "none", "rogue"))
    visuals.add_argument("--rogue", type=Path)
    visuals.add_argument("--border", choices=("2083", "2086", "2088", "2087"), default="2083")
    for name in ("banners", "rarity", "provision", "bread"):
        visuals.add_argument("--hide-" + name, action="store_true")
    audio = sub.add_parser("audio-build")
    for name in ("target", "output"):
        audio.add_argument("--" + name, required=True, type=Path)
    for name in ("target-card", "donor-card"):
        audio.add_argument("--" + name, required=True, type=int)
    audio_info = sub.add_parser("audio-info")
    audio_info.add_argument("--target", required=True, type=Path)
    audio_info.add_argument("--target-card", required=True, type=int)
    audio_catalog = sub.add_parser("audio-catalog")
    audio_catalog.add_argument("--target", required=True, type=Path)
    wav_normalize = sub.add_parser("wav-normalize")
    wav_normalize.add_argument("--source", required=True, type=Path)
    wav_normalize.add_argument("--output", required=True, type=Path)
    wav_probe = sub.add_parser("wav-probe")
    wav_probe.add_argument("--source", required=True, type=Path)
    wav_extract = sub.add_parser("wav-extract")
    for name in ("source", "output"):
        wav_extract.add_argument("--" + name, required=True, type=Path)
    for name in ("start", "end"):
        wav_extract.add_argument("--" + name, required=True, type=float)
    wav_edit = sub.add_parser("wav-edit")
    for name in ("source", "output"):
        wav_edit.add_argument("--" + name, required=True, type=Path)
    for name in ("start", "end"):
        wav_edit.add_argument("--" + name, required=True, type=float)
    for name in ("fade-in", "fade-out"):
        wav_edit.add_argument("--" + name, default=0, type=float)
    wav_status = sub.add_parser("wav-status")
    wav_status.add_argument("--client", required=True, type=Path)
    wav_builder = sub.add_parser("wav-build")
    for name in ("client", "source", "output", "runtime"):
        wav_builder.add_argument("--" + name, required=True, type=Path)
    wav_builder.add_argument("--target-card", required=True, type=int)
    wav_builder.add_argument("--gain", type=float, default=1)
    wav_builder.add_argument("--loop", action="store_true")
    for command in ("wav-verify", "wav-install", "wav-restore"):
        action = sub.add_parser(command)
        for name in ("package", "runtime"):
            action.add_argument("--" + name, required=True, type=Path)
        if command != "wav-verify": action.add_argument("--client", required=True, type=Path)
    for command in ("verify", "install", "restore"):
        action = sub.add_parser(command)
        action.add_argument("--package", required=True, type=Path)
        if command != "verify":
            action.add_argument("--client", required=True, type=Path)
    args = parser.parse_args()
    if args.dependencies:
        sys.path.insert(0, str(args.dependencies.resolve()))
    if args.command.startswith("wav-"):
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import custom_audio
        if args.command == "wav-normalize": result = custom_audio.normalize(args.source, args.output)
        elif args.command == "wav-probe": result = custom_audio.probe(args.source)
        elif args.command == "wav-extract": result = custom_audio.extract(args.source, args.output, args.start, args.end)
        elif args.command == "wav-edit": result = custom_audio.edit(args.source, args.output, args.start, args.end, args.fade_in, args.fade_out)
        elif args.command == "wav-status": result = dict(Loader=custom_audio.supported(args.client))
        elif args.command == "wav-build": result = custom_audio.build(args.client, args.target_card, args.source, args.output, args.runtime, args.gain, args.loop)
        elif args.command == "wav-verify": result = custom_audio.verify(args.package, args.runtime)
        else: result = custom_audio.transfer(args.package, args.client, args.runtime, args.command == "wav-restore")
    elif args.command in ("audio-build", "audio-info", "audio-catalog"):
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import audio_swap
        if args.command == "audio-build":
            result = audio_swap.build(args.target, args.target_card, args.donor_card, args.output)
        elif args.command == "audio-catalog":
            result = audio_swap.catalog(args.target)
        else:
            result = audio_swap.describe(args.target, args.target_card)
    elif args.command in ("fiery-build", "fiery-remove"):
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import fiery_frames
        if args.command == "fiery-remove":
            result = fiery_frames.build(args.client, args.output, remove=True)
            if not result.get("AlreadyRemoved"):
                result = dict(fiery_frames.transfer(args.output, args.client), Package=str(args.output))
        else:
            result = fiery_frames.build(args.client, args.output, bronze=args.bronze_frames, hide_banners=args.hide_banners, hide_rarity=args.hide_rarity, hide_provision=args.hide_provision, hide_bread=args.hide_bread)
    elif args.command == "media-check":
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from media_smoke import run as run_media_check
        result = run_media_check(args.client, args.runtime, args.report)
    elif args.command == "global-build":
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from global_visuals import build as build_global
        result = build_global(args.target, args.output, args.frames, args.rogue, args.border,
                              args.hide_banners, args.hide_rarity, args.hide_provision, args.hide_bread,
                              bronze_frames=args.bronze_frames, gold_frames=args.gold_frames)
    elif args.command == "build":
        result = build(args.source, args.source_card, args.target, args.target_card, args.output)
    elif args.command in ("cardback-catalog", "cardback-image-build"):
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import cardback_swap
        result = cardback_swap.catalog(args.target) if args.command == "cardback-catalog" else cardback_swap.build(args.target, args.target_cardback, args.image, args.output)
    elif args.command == "image-build":
        result = build(None, None, args.target, args.target_card, args.output, args.image)
    elif args.command == "image-preview":
        result = preview_image(args.target, args.target_card, args.output)
    elif args.command == "normalize":
        result = normalize_image(args.image, args.output)
    elif args.command == "verify":
        result = verify(args.package)
    else:
        result = transfer(args.package, args.client, args.command == "restore")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
