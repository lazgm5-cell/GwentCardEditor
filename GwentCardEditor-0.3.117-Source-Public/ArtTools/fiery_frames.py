"""Reversible animated-frame packages with strict runtime-version ownership checks."""
import json
import os
import re
from pathlib import Path
import shutil
import tempfile
import uuid
import art_swap as art
import global_visuals
from custom_audio import client_root, supported

PREFIX = "BepInEx/plugins/GwentCardEditor.FieryFiend.Experimental/"
RUNTIME = Path(__file__).resolve().parent.parent / "OptionalPlugins/FieryFiend"
DLL = "GwentCardEditor.FieryFiend.Experimental.dll"
SCOPES = ("global-card-visuals-animated", "animated-frame-removal")
GLOBAL_FILES = (global_visuals.GLOBAL, "bundledassets.json")
CONFIG = dict(Version=1, Enabled=True, AllGold=True, TargetCards=[])


def configuration(raw):
    try:
        if len(raw) > 65536:
            raise ValueError()
        data = json.loads(raw.decode("utf-8-sig"))
        if not isinstance(data, dict) or set(data) - {"Version", "Enabled", "AllGold", "TargetCards"}:
            raise ValueError()
        if type(data.get("Version", 1)) is not int or data.get("Version", 1) != 1 or type(data.get("Enabled")) is not bool or type(data.get("AllGold", False)) is not bool:
            raise ValueError()
        ids = data["TargetCards"]
        if not isinstance(ids, list) or len(ids) > 20 or any(type(i) is not int or not 0 < i < 2**31 for i in ids):
            raise ValueError()
        if data["Enabled"] and (data.get("AllGold", False) and ids or not data.get("AllGold", False) and not ids):
            raise ValueError()
        return data
    except (ValueError, KeyError, TypeError, UnicodeError) as error:
        raise ValueError("FieryConflict") from error


def releases(runtime=RUNTIME):
    runtime = Path(runtime)
    try:
        versions = json.loads((runtime / "known-runtimes.json").read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as error:
        raise ValueError("FieryRuntime") from error
    if versions.get("Version") != 1 or not versions.get("Releases"):
        raise ValueError("FieryRuntime")
    keys = set(versions["Releases"][0]["Files"])
    if len(keys) != 12 or DLL not in keys or "assets/scene.json" not in keys or any(set(v["Files"]) != keys for v in versions["Releases"]):
        raise ValueError("FieryRuntime")
    for key in keys:
        if key not in (DLL, "assets/scene.json") and not re.fullmatch(r"assets/textures/-?[0-9]+\.png", key):
            raise ValueError("FieryRuntime")
    return versions["Releases"], sorted(keys) + ["config.json"]


def group(records, field, base, known):
    values = {r["Path"].removeprefix(PREFIX): r[field] for r in records if r["Path"].startswith(PREFIX)}
    if all(v is None for v in values.values()):
        return
    if any(v is None for v in values.values()):
        raise ValueError("FieryConflict")
    fixed = {k: v for k, v in values.items() if k != "config.json"}
    if not any(fixed == release["Files"] for release in known):
        raise ValueError("FieryConflict")
    configuration(art.safe_path(base, PREFIX + "config.json").read_bytes())


def installed(root, known, names):
    records = []
    for name in names:
        path = art.safe_path(root, PREFIX + name)
        records.append(dict(Path=PREFIX + name, Before=art.digest(path.read_bytes()) if path.exists() else None))
    group(records, "Before", root, known)
    return records


def verify(package, runtime=RUNTIME):
    package = Path(package).resolve()
    value = json.loads((package / "manifest.json").read_text(encoding="utf-8-sig"))
    known, names = releases(runtime)
    if value.get("Version") != 1 or value.get("Scope") not in SCOPES:
        raise ValueError("FieryConflict")
    expected = [PREFIX + n for n in names] + (list(GLOBAL_FILES) if value["Scope"] == SCOPES[0] else [])
    if sorted(r["Path"] for r in value["Files"]) != sorted(expected):
        raise ValueError("FieryConflict")
    for record in value["Files"]:
        for part, key in (("backup", "Before"), ("payload", "After")):
            if record[key] is not None and art.digest(art.safe_path(package / part, record["Path"]).read_bytes()) != record[key]:
                raise ValueError("FieryConflict")
        if record["Path"] in GLOBAL_FILES and (record["Before"] is None or record["After"] is None):
            raise ValueError("FieryConflict")
    group(value["Files"], "Before", package / "backup", known)
    group(value["Files"], "After", package / "payload", known)
    if value["Scope"] == SCOPES[0]:
        if any(r["After"] is None for r in value["Files"]) or configuration(art.safe_path(package / "payload", PREFIX + "config.json").read_bytes()) != CONFIG:
            raise ValueError("FieryConflict")
    elif any(r["After"] is not None for r in value["Files"]):
        raise ValueError("FieryConflict")
    return value


def build(client, output, runtime=RUNTIME, bronze="keep", hide_banners=False, hide_rarity=False, hide_provision=False, hide_bread=False, remove=False):
    root, assets = client_root(client)
    output, runtime = Path(output).resolve(), Path(runtime)
    if output.exists() or output.is_relative_to(root):
        raise ValueError("FieryOutput")
    known, names = releases(runtime)
    old = installed(root, known, names)
    if remove and all(r["Before"] is None for r in old):
        return dict(Completed=True, AlreadyRemoved=True)
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="fiery-package-", dir=output.parent) as temporary:
        work = Path(temporary); ready = work / "package"; ready.mkdir()
        if remove:
            value = dict(Version=1, Scope=SCOPES[1], Files=[])
        else:
            static = work / "static"
            value = global_visuals.build(assets, static, bronze_frames=bronze, gold_frames="rogue", border="2087", hide_banners=hide_banners, hide_rarity=hide_rarity, hide_provision=hide_provision, hide_bread=hide_bread)
            for part in ("backup", "payload"):
                shutil.copytree(static / part, ready / part)
            value["Scope"] = SCOPES[0]
        for record in old:
            relative = record["Path"]; name = relative.removeprefix(PREFIX)
            if record["Before"] is not None:
                path = art.safe_path(ready / "backup", relative); path.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(art.safe_path(root, relative), path)
            after = None
            if not remove:
                raw = (json.dumps(CONFIG, indent=2) + "\n").encode() if name == "config.json" else (runtime / name).read_bytes()
                path = art.safe_path(ready / "payload", relative); path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(raw); after = art.digest(raw)
            value["Files"].append(dict(Path=relative, Before=record["Before"], After=after))
        value["AnimatedGoldFrames"] = not remove
        art.write_json(ready / "manifest.json", value); verify(ready, runtime)
        for record in value["Files"]:
            path = art.safe_path(assets if record["Path"] in GLOBAL_FILES else root, record["Path"])
            if (art.digest(path.read_bytes()) if path.exists() else None) != record["Before"]:
                raise ValueError("FieryConflict")
        os.replace(ready, output)
    return value


def transfer(package, client, restore=False, runtime=RUNTIME):
    root, assets = client_root(client); package = Path(package).resolve()
    value = verify(package, runtime); art.check_game_closed()
    paths = [(art.safe_path(assets if r["Path"] in GLOBAL_FILES else root, r["Path"]), r) for r in value["Files"]]
    actual = {r["Path"]: art.digest(p.read_bytes()) if p.exists() else None for p, r in paths}
    if restore and all(actual[r["Path"]] == r["Before"] for _, r in paths):
        return dict(Completed=True, Restored=True, AlreadyRestored=True, Files=0)
    animated = value["Scope"] == SCOPES[0]
    if not restore and animated and not supported(client):
        raise ValueError("FieryLoader")
    if animated and art.digest((assets / "data_definitions").read_bytes()) != value["ProtectedDataHash"]:
        raise ValueError("Client card data differs from package source")
    current, desired = ("After", "Before") if restore else ("Before", "After")
    plugin_absent = restore and animated and all(actual[r["Path"]] is None for _, r in paths if r["Path"].startswith(PREFIX)) and all(actual[r["Path"]] == r["After"] for _, r in paths if r["Path"] in GLOBAL_FILES)
    if not plugin_absent and any(actual[r["Path"]] != r[current] for _, r in paths):
        raise ValueError("FieryConflict")
    part = "backup" if restore else "payload"
    staged, committed = [], []
    # Keep exact pre-operation bytes for rollback, including an already removed plugin.
    undo = {p: p.read_bytes() if p.exists() else None for p, _ in paths}
    try:
        for path, record in paths:
            temporary = None
            if record[desired] is not None:
                path.parent.mkdir(parents=True, exist_ok=True)
                temporary = path.with_name(path.name + ".fiery-" + uuid.uuid4().hex)
                staged.append((temporary, path, record))
                shutil.copyfile(art.safe_path(package / part, record["Path"]), temporary)
            else:
                staged.append((None, path, record))
        art.check_game_closed()
        if any((art.digest(p.read_bytes()) if p.exists() else None) != actual[r["Path"]] for p, r in paths):
            raise ValueError("FieryConflict")
        for temporary, path, record in staged:
            if temporary is None: path.unlink(missing_ok=True)
            else: os.replace(temporary, path)
            committed.append(path)
            if (art.digest(path.read_bytes()) if path.exists() else None) != record[desired]:
                raise ValueError("FieryConflict")
    except Exception:
        for path in reversed(committed):
            if undo[path] is None: path.unlink(missing_ok=True)
            else: path.write_bytes(undo[path])
        raise
    finally:
        for temporary, _, _ in staged:
            if temporary is not None: temporary.unlink(missing_ok=True)
    return dict(Completed=True, Restored=restore, Files=len(paths))
