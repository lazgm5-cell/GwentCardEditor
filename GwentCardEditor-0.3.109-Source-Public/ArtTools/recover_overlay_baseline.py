"""Create a reversible recovery package only for recognized overlay-hiding changes."""
import argparse
import json
import os
from pathlib import Path
import tempfile
import UnityPy
from PIL import Image
import art_swap as art
import global_visuals as visuals


def build(client, baseline, output):
    target, baseline, output = art.streaming_assets(client), Path(baseline).resolve(), Path(output).resolve()
    manifest = art.verify(baseline)
    if manifest["Scope"] != "global-card-visuals" or output.exists() or output.is_relative_to(target) or output.is_relative_to(baseline):
        raise ValueError("Invalid recovery source or output")
    protected = art.digest((target / "data_definitions").read_bytes())
    if protected != manifest["ProtectedDataHash"]:
        raise ValueError("Baseline card data does not match")
    current = {p: (target / p).read_bytes() for p in (visuals.GLOBAL, "bundledassets.json")}
    original = {p: (baseline / "backup" / p).read_bytes() for p in current}
    old_catalog, new_catalog = (json.loads(d["bundledassets.json"].decode("utf-8-sig")) for d in (original, current))
    for catalog in (old_catalog, new_catalog):
        entries = [e for e in catalog["Infos"] if e["Path"] == visuals.GLOBAL]
        if len(entries) != 1:
            raise ValueError("Ambiguous global catalog entry")
        raw = original[visuals.GLOBAL] if catalog is old_catalog else current[visuals.GLOBAL]
        import zlib
        if (entries[0]["Size"], entries[0]["FileCrc"], entries[0]["CRC"]) != (len(raw), zlib.crc32(raw) & 0xffffffff, zlib.crc32(art.bundle_body(raw)) & 0xffffffff):
            raise ValueError("Catalog mismatch")
    expected = json.loads(json.dumps(old_catalog))
    old_entry = next(e for e in expected["Infos"] if e["Path"] == visuals.GLOBAL)
    new_entry = next(e for e in new_catalog["Infos"] if e["Path"] == visuals.GLOBAL)
    for key in ("Size", "FileCrc", "CRC"):
        old_entry[key] = new_entry[key]
    if expected != new_catalog:
        raise ValueError("Unrelated catalog changes; recovery refused")
    a, b = (UnityPy.load(d[visuals.GLOBAL]) for d in (original, current))
    before, after = art.object_hashes(a), art.object_hashes(b)
    if before.keys() != after.keys():
        raise ValueError("Unity object sets differ")
    changed = {k for k in before if before[k] != after[k]}
    checks = []
    for name in ("CardBanners_Atlas", "CardElement_Atlas", "CardProvision_Atlas"):
        obj, value = art.texture(b, name)
        key = (obj.assets_file.name, obj.path_id)
        if key not in changed:
            continue
        initial, actual = art.texture(a, name)[1].image.convert("RGBA"), value.image.convert("RGBA")
        if name == "CardBanners_Atlas":
            candidates = [Image.new("RGBA", initial.size)]
        elif name == "CardElement_Atlas":
            candidates = [visuals.clear_regions(initial, visuals.overlay_regions(hide_rarity=True)[name])]
        else:
            candidates = [visuals.clear_regions(initial, visuals.overlay_regions(hide_provision=p, hide_bread=q)[name]) for p, q in ((True, False), (False, True), (True, True))]
        if not any(actual.size == candidate.size and actual.tobytes() == candidate.tobytes() for candidate in candidates):
            raise ValueError("Unrecognized texture modification: " + name)
        changed.remove(key)
        checks.append(dict(Texture=name, Pixels=art.digest(initial.tobytes())))
    if changed or not checks:
        raise ValueError("Unrelated Unity changes or no overlay changes; recovery refused")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="overlay-recovery-", dir=output.parent) as temporary:
        work = Path(temporary); records = []
        for relative in current:
            for part, data in (("backup", current[relative]), ("payload", original[relative])):
                path = work / part / relative; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)
            records.append(dict(Path=relative, Before=art.digest(current[relative]), After=art.digest(original[relative])))
        result = dict(Version=1, Scope="global-card-visuals", TargetClient=str(target), ProtectedDataHash=protected,
                      SourceHashes={str(baseline / "backup" / p): art.digest(data) for p, data in original.items()},
                      Files=records, Checks=checks, PreservedObjects=len(before) - len(checks),
                      Settings=dict(Recovery="verified-overlay-baseline", BaselinePackage=str(baseline)))
        art.write_json(work / "manifest.json", result); art.verify(work)
        if any((target / p).read_bytes() != data for p, data in current.items()) or art.digest((target / "data_definitions").read_bytes()) != protected:
            raise ValueError("Client changed during recovery build")
        os.replace(work, output)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("client", "baseline", "output"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.client, args.baseline, args.output), indent=2))
