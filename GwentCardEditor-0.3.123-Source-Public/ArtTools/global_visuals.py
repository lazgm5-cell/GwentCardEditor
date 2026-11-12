"""Build reversible global card-overlay packages using local client resources."""
import argparse
import copy
import json
import os
from pathlib import Path
import re
import tempfile
import zlib
import UnityPy
from PIL import Image
from art_swap import streaming_assets, digest, write_json, texture, object_hashes, bundle_body, verify

GLOBAL = "bundledassets/dependencies/shared/global"
BORDERS = {"2083": "Border", "2086": "Elite_CardBorder", "2088": "Gorspeth_CardBorder", "2087": "Fiery Fiend"}
FRAME_ASSETS = Path(__file__).resolve().parent.parent / "Assets/RogueFrames"


def rogue_frame_image(raw, key):
    env = UnityPy.load(raw)
    if key == "2087":
        left, right = (texture(env, name)[1].image.convert("RGBA") for name in ("left", "right"))
        if left.size != (1024, 2048) or right.size != left.size:
            raise ValueError("ArtTextureLayout")
        # Both border planes use the same mesh/UVs; right is rendered after left.
        return Image.alpha_composite(left, right)
    _, donor = texture(env, "Standard_CardBorder" if key == "bronze" else BORDERS[key])
    image = donor.image.convert("RGBA")
    if image.size != (1024, 2048):
        raise ValueError("ArtTextureLayout")
    return image


def bundled_frames(border, directory=None, keys=None):
    directory = Path(directory) if directory is not None else FRAME_ASSETS
    try:
        manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8-sig"))
        if manifest["Version"] != 1 or set(manifest["Frames"]) != {"bronze", *BORDERS} or str(border) not in BORDERS:
            raise ValueError("Invalid frame manifest")
        images, hashes = {}, {}
        selected = ("bronze", str(border)) if keys is None else tuple(keys)
        if not set(selected).issubset({"bronze", *BORDERS}):
            raise ValueError("Invalid frame key")
        for key in selected:
            data = (directory / (key + ".png")).read_bytes()
            if len(data) > 10 * 1024 * 1024 or digest(data) != manifest["Frames"][key]["Sha256"]:
                raise ValueError("Frame hash mismatch")
            import io
            with Image.open(io.BytesIO(data)) as image:
                if image.size != (1024, 2048) or image.mode != "RGBA":
                    raise ValueError("Frame texture layout")
                images[key] = image.copy()
            hashes["embedded/" + key + ".png"] = digest(data)
        return images, hashes
    except (OSError, ValueError, KeyError, TypeError) as error:
        raise ValueError("VisualFrameAssets") from error


def clear_regions(image, regions):
    result = image.convert("RGBA").copy()
    for box in regions:
        result.paste((0, 0, 0, 0), box)
    return result


def overlay_regions(hide_rarity=False, hide_provision=False, hide_bread=False):
    regions = {}
    if hide_rarity:
        regions["CardElement_Atlas"] = [(512, 512, 768, 768)]
    provision = []
    if hide_provision:
        provision.extend([(0, 0, 656, 264), (0, 264, 900, 560)])
    if hide_bread:
        provision.append((656, 0, 900, 264))
    if provision:
        regions["CardProvision_Atlas"] = provision
    return regions


def build(target, output, frames="keep", rogue=None, border="2083", hide_banners=False,
          hide_rarity=False, hide_provision=False, hide_bread=False, bronze_frames=None, gold_frames=None):
    if frames not in ("keep", "none", "rogue") or str(border) not in BORDERS:
        raise ValueError("Unknown frame option")
    modes = {"CardFrame_Bronze": bronze_frames if bronze_frames is not None else frames,
             "CardFrame_Gold": gold_frames if gold_frames is not None else frames}
    if any(mode not in ("keep", "none", "rogue") for mode in modes.values()):
        raise ValueError("Unknown frame option")
    target, output = streaming_assets(target), Path(output).resolve()
    source = streaming_assets(rogue) if "rogue" in modes.values() and rogue else None
    if output.exists() or output.is_relative_to(target) or source and output.is_relative_to(source):
        raise ValueError("Output must be a new directory outside StreamingAssets")
    if all(mode == "keep" for mode in modes.values()) and not any((hide_banners, hide_rarity, hide_provision, hide_bread)):
        raise ValueError("No visual changes selected")
    original = (target / GLOBAL).read_bytes()
    catalog_bytes = (target / "bundledassets.json").read_bytes()
    catalog = json.loads(catalog_bytes.decode("utf-8-sig"))
    entries = [e for e in catalog["Infos"] if e["Path"] == GLOBAL]
    if len(entries) != 1:
        raise ValueError("Missing or duplicate global resource")
    entry = entries[0]
    if (entry["Size"], entry["FileCrc"], entry["CRC"]) != (
            len(original), zlib.crc32(original) & 0xffffffff,
            zlib.crc32(bundle_body(original)) & 0xffffffff):
        raise ValueError("Target catalog does not match global resource")
    env = UnityPy.load(original)
    if env.file.version_engine != "2022.3.62f2":
        raise ValueError("Unverified target Unity version: " + env.file.version_engine)
    before = object_hashes(env)
    replacements, source_hashes = {}, {}
    embedded = None
    if "rogue" in modes.values() and source is None:
        keys = ["bronze" if name.endswith("Bronze") else str(border) for name, mode in modes.items() if mode == "rogue"]
        embedded, source_hashes = bundled_frames(border, keys=keys)
    for name, mode in modes.items():
        if mode == "keep":
            continue
        _, value = texture(env, name)
        if (value.m_Width, value.m_Height) != (1024, 2048):
            raise ValueError("ArtTextureLayout")
        if mode == "none":
            replacements[name] = Image.new("RGBA", (1024, 2048))
        else:
            if embedded is not None:
                replacements[name] = embedded["bronze" if name.endswith("Bronze") else str(border)]
                continue
            key = "bronze" if name.endswith("Bronze") else str(border)
            path = source / ("bundledassets/vanityassets/cardborders/" + ("2082" if key == "bronze" else key) + "0480")
            data = path.read_bytes()
            source_hashes[str(path)] = digest(data)
            replacements[name] = rogue_frame_image(data, key)
    if hide_banners:
        _, value = texture(env, "CardBanners_Atlas")
        if (value.m_Width, value.m_Height) != (1024, 1024):
            raise ValueError("ArtTextureLayout")
        replacements["CardBanners_Atlas"] = Image.new("RGBA", (1024, 1024))
    for name, regions in overlay_regions(hide_rarity, hide_provision, hide_bread).items():
        _, value = texture(env, name)
        if (value.m_Width, value.m_Height) != (1024, 1024):
            raise ValueError("ArtTextureLayout")
        replacements[name] = clear_regions(value.image, regions)
    output.parent.mkdir(parents=True, exist_ok=True)
    protected = digest((target / "data_definitions").read_bytes())
    with tempfile.TemporaryDirectory(prefix="global-visuals-", dir=output.parent) as temp:
        work = Path(temp)
        (work / "previews").mkdir()
        changed, checks = set(), []
        for name, image in replacements.items():
            obj, value = texture(env, name)
            value.image.save(work / "previews" / (name + "-before.png"))
            value.set_image(image, target_format=4)
            value.save()
            image.save(work / "previews" / (name + "-after.png"))
            changed.add((obj.assets_file.name, obj.path_id))
        result = env.file.save(packer="lz4")
        reopened = UnityPy.load(result)
        after = object_hashes(reopened)
        if before.keys() != after.keys() or any(before[k] != after[k] for k in before.keys() - changed):
            raise ValueError("An unrelated Unity object changed")
        for name, image in replacements.items():
            _, value = texture(reopened, name)
            if value.image.convert("RGBA").tobytes() != image.tobytes():
                raise ValueError("Pixel round-trip failed: " + name)
            checks.append(dict(Texture=name, Pixels=digest(image.tobytes())))
        original_catalog = copy.deepcopy(catalog)
        entry.update(CRC=zlib.crc32(bundle_body(result)) & 0xffffffff,
                     FileCrc=zlib.crc32(result) & 0xffffffff, Size=len(result))
        expected = copy.deepcopy(original_catalog)
        next(e for e in expected["Infos"] if e["Path"] == GLOBAL).update(
            {k: entry[k] for k in ("CRC", "FileCrc", "Size")})
        if expected != catalog:
            raise ValueError("Unrelated catalog entries changed")
        catalog_result = (json.dumps(catalog, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        records = []
        for relative, old, new in ((GLOBAL, original, result), ("bundledassets.json", catalog_bytes, catalog_result)):
            for part, data in (("backup", old), ("payload", new)):
                dest = work / part / relative
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(data)
            records.append(dict(Path=relative, Before=digest(old), After=digest(new)))
        if digest((target / "data_definitions").read_bytes()) != protected or (target / GLOBAL).read_bytes() != original or (target / "bundledassets.json").read_bytes() != catalog_bytes:
            raise ValueError("Client changed during build")
        manifest = dict(Version=1, Scope="global-card-visuals", TargetClient=str(target),
                        ProtectedDataHash=protected, SourceHashes=source_hashes, Files=records, Checks=checks,
                        PreservedObjects=len(before) - len(changed),
                        Settings=dict(Frames=frames, Border=str(border), BronzeFrames=modes["CardFrame_Bronze"], GoldFrames=modes["CardFrame_Gold"], HideBanners=hide_banners,
                                      HideRarity=hide_rarity, HideProvision=hide_provision, HideBread=hide_bread))
        write_json(work / "manifest.json", manifest)
        verify(work)
        os.replace(work, output)
    return manifest


def audit(client, output):
    sa = streaming_assets(client)
    path = sa / "bundledassets/dependencies/shared/global"
    env = UnityPy.load(str(path))
    objects = []
    for obj in env.objects:
        if obj.type.name not in ("Texture2D", "Material", "GameObject", "Mesh", "Sprite"):
            continue
        value = obj.read()
        name = getattr(value, "m_Name", "")
        if re.search(r"card|border|overlay|bronze|gold|provision|mana|starter", name, re.I):
            record = dict(Id=obj.path_id, Type=obj.type.name, Name=name)
            if obj.type.name == "Texture2D":
                record.update(Width=value.m_Width, Height=value.m_Height, Format=value.m_TextureFormat)
            objects.append(record)
    write_json(Path(output), dict(Path=str(path), Sha256=digest(path.read_bytes()),
                                 Unity=env.file.version_engine, Objects=objects))
    print(json.dumps(dict(Objects=len(objects), Output=str(output))))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    audit(args.client, args.output)
