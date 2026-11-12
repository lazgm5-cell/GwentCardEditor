"""Repair external Unity PPtr indices when scene objects are merged between bundles."""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import uuid
import zlib

import UnityPy

import art_swap


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def path_key(value: str) -> str:
    return value.replace("\\", "/").casefold().rstrip("/")


def contains_external_ref(value) -> bool:
    if isinstance(value, dict):
        if "m_FileID" in value and "m_PathID" in value:
            return bool(value["m_FileID"])
        return any(contains_external_ref(item) for item in value.values())
    if isinstance(value, (list, tuple)):
        return any(contains_external_ref(item) for item in value)
    return False


def remap_tree(source, target, file_ids: dict[int, int], object_id: int, path="") -> tuple[bool, int]:
    changed, count = False, 0
    if isinstance(source, dict) and isinstance(target, dict):
        if "m_FileID" in source and "m_PathID" in source and "m_FileID" in target and "m_PathID" in target:
            old = source["m_FileID"]
            if old == 0:
                return False, 0
            if source["m_PathID"] != target["m_PathID"]:
                raise ValueError(f"PPtr path mismatch in object {object_id} at {path}")
            new = file_ids.get(old)
            if new is None:
                raise ValueError(f"Unknown source external fileID {old} in object {object_id}")
            current = target["m_FileID"]
            if current != new:
                target["m_FileID"] = new
                return True, 1
            return False, 1
        for key, value in source.items():
            if key in target:
                part_changed, part_count = remap_tree(value, target[key], file_ids, object_id, f"{path}.{key}")
                changed |= part_changed
                count += part_count
            elif contains_external_ref(value):
                raise ValueError(f"Target tree omits external references at {path}.{key} in object {object_id}")
    elif isinstance(source, (list, tuple)) and isinstance(target, (list, tuple)):
        for index, (left, right) in enumerate(zip(source, target)):
            part_changed, part_count = remap_tree(left, right, file_ids, object_id, f"{path}[{index}]")
            changed |= part_changed
            count += part_count
        if any(contains_external_ref(item) for item in source[len(target):]):
            raise ValueError(f"Target tree omits external list references at {path} in object {object_id}")
    return changed, count


def build(source_root: Path, source_bundle: str, target_root: Path, target_bundle: str, output: Path):
    source_root, target_root, output = map(lambda p: Path(p).resolve(), (source_root, target_root, output))
    if output.exists() or output.is_relative_to(target_root):
        raise ValueError("Output must be a new directory outside the target client")

    source_path, target_path = source_root / source_bundle, target_root / target_bundle
    source_env = UnityPy.load(str(source_path))
    target_before = target_path.read_bytes()
    target_env = UnityPy.load(target_before)
    source_file = source_env.objects[0].assets_file
    target_file = target_env.objects[0].assets_file

    target_ext = {path_key(value.path): index + 1 for index, value in enumerate(target_file.externals)}
    file_ids, added_external_refs = {}, []
    for source_id, external in enumerate(source_file.externals, 1):
        key = path_key(external.path)
        target_id = target_ext.get(key)
        if target_id is None:
            target_file.externals.append(copy.deepcopy(external))
            target_id = len(target_file.externals)
            target_ext[key] = target_id
            added_external_refs.append(external.path)
        file_ids[source_id] = target_id

    target_objects = {obj.path_id: obj for obj in target_env.objects}
    matched, modified, pointer_counts = 0, set(), {}
    for source_obj in source_env.objects:
        target_obj = target_objects.get(source_obj.path_id)
        if target_obj is None or source_obj.type.name != target_obj.type.name:
            continue
        source_tree, target_tree = source_obj.read_typetree(), target_obj.read_typetree()
        if getattr(source_obj.read(), "m_Name", "") != getattr(target_obj.read(), "m_Name", ""):
            continue
        matched += 1
        changed, count = remap_tree(source_tree, target_tree, file_ids, source_obj.path_id)
        if changed:
            target_obj.save_typetree(target_tree)
            modified.add(target_obj.path_id)
        if count:
            pointer_counts[source_obj.path_id] = count
    if not modified:
        raise ValueError("No merged scene references required repair")

    result_bundle = target_env.file.save(packer="lz4")
    check_env = UnityPy.load(result_bundle)
    check_file = check_env.objects[0].assets_file
    if len(check_file.externals) != len(target_file.externals):
        raise ValueError("External reference table did not survive serialization")
    for obj in check_env.objects:
        try:
            tree = obj.read_typetree()
        except Exception:
            continue
        refs = []
        def collect(value):
            if isinstance(value, dict):
                if "m_FileID" in value and "m_PathID" in value:
                    refs.append(value)
                else:
                    for child in value.values(): collect(child)
            elif isinstance(value, (list, tuple)):
                for child in value: collect(child)
        collect(tree)
        for ref in refs:
            if ref["m_FileID"] < 0 or ref["m_FileID"] > len(check_file.externals):
                raise ValueError(f"Out-of-range external reference in object {obj.path_id}")

    catalog_path = target_root / "bundledassets.json"
    catalog_before = catalog_path.read_bytes()
    catalog = json.loads(catalog_before.decode("utf-8-sig"))
    entries = [entry for entry in catalog["Infos"] if entry["Path"] == target_bundle]
    if len(entries) != 1:
        raise ValueError("Missing or duplicate target bundle catalog entry")
    entry = entries[0]
    entry.update(CRC=zlib.crc32(art_swap.bundle_body(result_bundle)) & 0xffffffff,
                 FileCrc=zlib.crc32(result_bundle) & 0xffffffff, Size=len(result_bundle))
    catalog_after = (json.dumps(catalog, ensure_ascii=False, indent=2) + "\n").encode("utf-8")

    output.mkdir(parents=True)
    files = {
        target_bundle: (target_before, result_bundle),
        "bundledassets.json": (catalog_before, catalog_after),
    }
    records = []
    for part, side in (("backup", 0), ("payload", 1)):
        for relative, pair in files.items():
            destination = output / part / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(pair[side])
    for relative, (before, after) in files.items():
        records.append({"Path": relative, "Before": sha(before), "After": sha(after)})
    manifest = {
        "Version": 1,
        "Scope": "witcher-scene-external-remap",
        "TargetClient": str(target_root),
        "SourceBundle": str(source_path),
        "TargetBundle": target_bundle,
        "MatchedObjects": matched,
        "ChangedObjects": len(modified),
        "RemappedPointers": sum(pointer_counts.values()),
        "ExternalFileIdMap": file_ids,
        "AddedExternalRefs": added_external_refs,
        "Files": records,
    }
    (output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return manifest


def verify(package: Path):
    package = Path(package).resolve()
    manifest = json.loads((package / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("Scope") != "witcher-scene-external-remap" or manifest.get("Version") != 1:
        raise ValueError("Unknown scene package")
    if {record["Path"] for record in manifest["Files"]} != {manifest["TargetBundle"], "bundledassets.json"}:
        raise ValueError("Unexpected package file set")
    for record in manifest["Files"]:
        for part, hash_key in (("backup", "Before"), ("payload", "After")):
            if sha((package / part / record["Path"]).read_bytes()) != record[hash_key]:
                raise ValueError("Package checksum mismatch")
    env = UnityPy.load(str(package / "payload" / manifest["TargetBundle"]))
    af = env.objects[0].assets_file
    for obj in env.objects:
        try:
            tree = obj.read_typetree()
        except Exception:
            continue
        refs = []
        def collect(value):
            if isinstance(value, dict):
                if "m_FileID" in value and "m_PathID" in value: refs.append(value)
                else:
                    for child in value.values(): collect(child)
            elif isinstance(value, (list, tuple)):
                for child in value: collect(child)
        collect(tree)
        if any(ref["m_FileID"] < 0 or ref["m_FileID"] > len(af.externals) for ref in refs):
            raise ValueError(f"Invalid external reference in {obj.path_id}")
    return manifest


def transfer(package: Path, client: Path, restore=False):
    package = Path(package).resolve()
    manifest = verify(package)
    target_root = art_swap.streaming_assets(client)
    if str(target_root).casefold() != manifest["TargetClient"].casefold():
        raise ValueError("Package belongs to a different client")
    process_check = subprocess.run(
        ["powershell.exe", "-NoLogo", "-NoProfile", "-Command",
         "if (Get-Process -Name Gwent -ErrorAction SilentlyContinue) { exit 2 }"],
        capture_output=True, check=False)
    if process_check.returncode != 0:
        raise ValueError("Close Gwent.exe or verify that the game process check is available")
    current_key, desired_key = ("After", "Before") if restore else ("Before", "After")
    source_part = "backup" if restore else "payload"
    records = manifest["Files"]
    if restore and all(sha(art_swap.safe_path(target_root, r["Path"]).read_bytes()) == r["Before"] for r in records):
        return {"Completed": True, "Restored": True, "AlreadyRestored": True, "Files": 0}
    targets = [(art_swap.safe_path(target_root, r["Path"]), r) for r in records]
    for path, record in targets:
        if sha(path.read_bytes()) != record[current_key]:
            raise ValueError(f"Client changed; refusing overwrite: {record['Path']}")
    staged, committed = [], []
    try:
        for path, record in targets:
            temporary = path.with_name(path.name + ".scene-remap-" + uuid.uuid4().hex)
            staged.append((temporary, path, record))
            shutil.copyfile(art_swap.safe_path(package / source_part, record["Path"]), temporary)
        for temporary, path, record in staged:
            os.replace(temporary, path)
            committed.append((path, record))
            if sha(path.read_bytes()) != record[desired_key]:
                raise ValueError(f"Installed checksum mismatch: {record['Path']}")
    except Exception:
        undo_part = "payload" if restore else "backup"
        for path, record in reversed(committed):
            shutil.copyfile(art_swap.safe_path(package / undo_part, record["Path"]), path)
        raise
    finally:
        for temporary, _, _ in staged:
            temporary.unlink(missing_ok=True)
    return {"Completed": True, "Restored": restore, "Files": len(targets), "Target": str(target_root)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    builder = sub.add_parser("build")
    builder.add_argument("--source-root", type=Path, required=True)
    builder.add_argument("--source-bundle", required=True)
    builder.add_argument("--target-root", type=Path, required=True)
    builder.add_argument("--target-bundle", required=True)
    builder.add_argument("--output", type=Path, required=True)
    verifier = sub.add_parser("verify")
    verifier.add_argument("--package", type=Path, required=True)
    for command in ("install", "restore"):
        action = sub.add_parser(command)
        action.add_argument("--package", type=Path, required=True)
        action.add_argument("--client", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "build":
        result = build(args.source_root, args.source_bundle, args.target_root, args.target_bundle, args.output)
    elif args.command == "verify":
        result = verify(args.package)
    else:
        result = transfer(args.package, args.client, args.command == "restore")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
