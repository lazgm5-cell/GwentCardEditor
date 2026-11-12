"""Isolated static-cardback image packages; no prefab or card-data changes."""
from __future__ import annotations

import base64
import copy
import io
import json
import os
from pathlib import Path
import tempfile
import zlib

import art_swap as api

IDS = tuple(25000400 + i * 10000 for i in range(7))
BASE = "bundledassets/vanityassets/cardbacks/textures/standard/"
MESH_HASH = "952011a94e066a4f22adf1b56503a8f2f9dca39281e9ba4b9322e454f3e7b6b4"


def paths(cardback):
    if type(cardback) is not int or cardback not in IDS:
        raise ValueError("CardbackUnsupported")
    return [BASE + q + "/" + str(cardback) for q in ("high", "medium", "uber")] + [BASE + "low"]


def geometry(target):
    import UnityPy
    env = UnityPy.load((target / "bundledassets/dependencies/shared/global").read_bytes())
    meshes = [o for o in env.objects if o.type.name == "Mesh" and o.read().m_Name == "StandardCardBack_Card_CardBackMesh"]
    if len(meshes) != 1 or api.digest(meshes[0].get_raw_data()) != MESH_HASH:
        raise ValueError("CardbackGeometry")


def checked_texture(target, catalog, path, cardback):
    import UnityPy
    entries = [e for e in catalog["Infos"] if e["Path"] == path]
    if len(entries) != 1 or entries[0].get("AllDependencies"):
        raise ValueError("CardbackUnsupported")
    data = api.safe_path(target, path).read_bytes()
    entry = entries[0]
    if entry["Size"] != len(data) or entry["FileCrc"] != zlib.crc32(data) & 0xffffffff or entry["CRC"] != zlib.crc32(api.bundle_body(data)) & 0xffffffff:
        raise ValueError("CardbackCatalogMismatch")
    env = UnityPy.load(data)
    if env.file.version_engine != "2022.3.62f2":
        raise ValueError("CardbackGeometry")
    obj, value = api.texture(env, str(cardback))
    if value.m_Width not in (128, 256, 512, 1024) or value.m_Height != value.m_Width * 2:
        raise ValueError("CardbackGeometry")
    return data, env, obj, value, entry


def catalog(client):
    target = api.streaming_assets(client)
    geometry(target)
    index = json.loads((target / "bundledassets.json").read_text(encoding="utf-8-sig"))
    rows = []
    for number in IDS:
        _, _, _, texture, _ = checked_texture(target, index, paths(number)[0], number)
        image = texture.image.convert("RGBA")
        image = image.crop((0, 0, image.width, image.width * 3 // 2))
        image.thumbnail((80, 120))
        stream = io.BytesIO()
        image.save(stream, format="PNG")
        rows.append(dict(Id=number, Preview=base64.b64encode(stream.getvalue()).decode("ascii")))
    return dict(Cardbacks=rows, Ratio="2:3", StaticOnly=True)


def build(client, number, image_file, output):
    import UnityPy
    from PIL import Image
    target, output = api.streaming_assets(client), Path(output).resolve()
    numbers = [number] if type(number) is int else list(number)
    if not numbers or any(type(value) is not int or value not in IDS for value in numbers) or len(set(numbers)) != len(numbers):
        raise ValueError("CardbackUnsupported")
    selected_paths = list(dict.fromkeys(path for value in numbers for path in paths(value)))
    if output.exists() or output.is_relative_to(target):
        raise ValueError("Output must be a new directory outside StreamingAssets")
    geometry(target)
    source = api.load_image(image_file)
    if abs(source.width / source.height - 2 / 3) > .001:
        raise ValueError("CardbackCropRatio")
    catalog_path = target / "bundledassets.json"
    original_catalog = catalog_path.read_bytes()
    index = json.loads(original_catalog.decode("utf-8-sig"))
    before_index = copy.deepcopy(index)
    protected = api.digest((target / "data_definitions").read_bytes())
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="cardback-build-", dir=output.parent) as temporary:
        work = Path(temporary)
        (work / "previews").mkdir()
        records, checks = [], []
        for path in selected_paths:
            path_numbers = [value for value in numbers if path in paths(value)]
            original, env, obj, value, entry = checked_texture(target, index, path, path_numbers[0])
            hashes = api.object_hashes(env)
            changed_keys, expected_pixels = set(), {}
            for number in path_numbers:
                obj, value = api.texture(env, str(number))
                image = value.image.convert("RGBA")
                visible = source.resize((image.width, image.width * 3 // 2), Image.Resampling.LANCZOS)
                opaque = Image.new("RGBA", visible.size, (0, 0, 0, 255))
                opaque.alpha_composite(visible)
                image.paste(opaque, (0, 0))
                if "/uber/" in path:
                    prefix = f"{number}-" if len(numbers) > 1 else ""
                    value.image.save(work / f"previews/{prefix}original.png")
                    image.save(work / f"previews/{prefix}replacement.png")
                value.set_image(image, target_format=4)
                value.save()
                expected_pixels[number] = image.tobytes()
                changed_keys.add((obj.assets_file.name, obj.path_id))
            result = env.file.save(packer="lz4")
            reopened = UnityPy.load(result)
            for number, pixels in expected_pixels.items():
                _, changed = api.texture(reopened, str(number))
                if changed.image.convert("RGBA").tobytes() != pixels:
                    raise ValueError("CardbackPixelRoundtrip")
            after = api.object_hashes(reopened)
            if hashes.keys() != after.keys() or any(hashes[key] != after[key] for key in hashes if key not in changed_keys):
                raise ValueError("Unrelated cardback object changed")
            entry.update(CRC=zlib.crc32(api.bundle_body(result)) & 0xffffffff, FileCrc=zlib.crc32(result) & 0xffffffff, Size=len(result))
            for folder, data in (("payload", result), ("backup", original)):
                dest = api.safe_path(work / folder, path)
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(data)
            records.append(dict(Path=path, Before=api.digest(original), After=api.digest(result)))
            checks.extend(dict(Path=path, TargetCardback=number, Pixels=api.digest(pixels), PreservedObjects=len(hashes)-len(changed_keys))
                          for number, pixels in expected_pixels.items())
        expected = copy.deepcopy(before_index)
        for entry in expected["Infos"]:
            if entry["Path"] in selected_paths:
                replacement = next(e for e in index["Infos"] if e["Path"] == entry["Path"])
                entry.update({k: replacement[k] for k in ("CRC", "FileCrc", "Size")})
        if expected != index or protected != api.digest((target / "data_definitions").read_bytes()):
            raise ValueError("Unrelated client data changed")
        updated = (json.dumps(index, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        for folder, data in (("payload", updated), ("backup", original_catalog)):
            (work / folder / "bundledassets.json").write_bytes(data)
        records.append(dict(Path="bundledassets.json", Before=api.digest(original_catalog), After=api.digest(updated)))
        manifest = dict(Version=1, Scope="static-cardback-image", TargetCardbacks=numbers,
                        TargetCardback=numbers[0] if len(numbers) == 1 else None, TargetClient=str(target),
                        ProtectedDataHash=protected, SourceImageHash=api.digest(Path(image_file).read_bytes()),
                        GeometryHash=MESH_HASH, ContentRatio="2:3", Files=records, Checks=checks,
                        PremiumUnchanged=True, AudioUnchanged=True)
        api.write_json(work / "manifest.json", manifest)
        api.verify(work)
        os.replace(work, output)
    return manifest
