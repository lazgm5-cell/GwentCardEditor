"""Reassign an existing native audio definition without touching Wwise banks."""
import copy
import json
import os
from pathlib import Path
import tempfile
from xml.dom import minidom
import xml.etree.ElementTree as ET
import zipfile
from art_swap import streaming_assets, digest, write_json, verify


def card_template(tree, card_id):
    rows = [c for c in tree if c.get("Type") == "CardTemplate" and c.get("Id") == str(card_id)]
    if len(rows) != 1:
        raise ValueError("AudioUnsupported")
    value = rows[0]
    if int(value.get("Availability", "0")) <= 0 or any(s in value.get("DebugName", "").lower() for s in ("tutorial", "ftux")):
        raise ValueError("AudioUnsupported")
    return value


def definitions(archive):
    root = ET.fromstring(archive.read("CardAudio.xml"))
    if root.tag != "CardAudioCollection":
        raise ValueError("AudioUnsupported")
    rows = {}
    for wrapper in root:
        if wrapper.tag != "CardAudio" or wrapper.get("Type") != "CardAudio" or not len(wrapper):
            raise ValueError("AudioUnsupported")
        for item in wrapper:
            key = item.get("id")
            if item.tag != "CardAudio" or not key or key in rows:
                raise ValueError("AudioUnsupported")
            rows[key] = item
    return rows


def describe(target, card_id):
    target = streaming_assets(target)
    with zipfile.ZipFile(target / "data_definitions") as archive:
        template = card_template(ET.fromstring(archive.read("Templates.xml")), card_id)
        audio_id = template.get("AudioId")
        if not audio_id or not audio_id.isdigit() or int(audio_id) <= 0:
            raise ValueError("AudioUnsupported")
        audio = definitions(archive).get(audio_id)
        if audio is None:
            raise ValueError("AudioUnsupported")
        voices = [v.get("key") for v in audio.iter("Voiceover")]
        effects = [int(v.get("event", "0")) & 0xffffffff for v in audio.iter("SoundEffect") if int(v.get("event", "0"))]
        return dict(CardId=card_id, AudioId=audio_id, Voices=len(voices), Effects=len(effects),
                    Triggers=len(audio.findall("./Triggers/Trigger")))


def catalog(target):
    target = streaming_assets(target)
    with zipfile.ZipFile(target / "data_definitions") as archive:
        root = ET.fromstring(archive.read("Templates.xml"))
        audio = definitions(archive)
    cards = []
    for template in root:
        if template.get("Type") != "CardTemplate":
            continue
        try:
            item = card_template(root, int(template.get("Id")))
        except (ValueError, TypeError):
            continue
        definition = audio.get(item.get("AudioId"))
        if definition is not None and (list(definition.iter("Voiceover")) or
                                      any(int(e.get("event", "0")) for e in definition.iter("SoundEffect"))):
            cards.append(int(item.get("Id")))
    return dict(Cards=cards)


def build(target, target_id, donor_id, output):
    target, output = streaming_assets(target), Path(output).resolve()
    if output.exists() or output.is_relative_to(target):
        raise ValueError("Output must be a new directory outside StreamingAssets")
    old = (target / "data_definitions").read_bytes()
    donor = describe(target, donor_id)
    recipient = describe(target, target_id)
    if donor["AudioId"] == recipient["AudioId"]:
        raise ValueError("AudioSameSource")
    if donor["Voices"] + donor["Effects"] == 0:
        raise ValueError("AudioUnsupported")
    metadata = ["audio.json", "Audio/event_inclusion.json", "Audio/soundbank_inclusion.json"]
    dependencies = {p: digest((target / p).read_bytes()) for p in metadata}
    import io
    with zipfile.ZipFile(io.BytesIO(old)) as archive:
        infos = archive.infolist()
        names = [i.filename for i in infos]
        if len(names) != len(set(n.lower() for n in names)) or names.count("Templates.xml") != 1:
            raise ValueError("AudioUnsupported")
        if any(i.file_size > 128 * 1024 * 1024 for i in infos):
            raise ValueError("AudioUnsupported")
        contents = {i.filename: archive.read(i.filename) for i in infos}
    original = ET.fromstring(contents["Templates.xml"])
    expected = copy.deepcopy(original)
    card_template(expected, target_id).set("AudioId", str(donor["AudioId"]))
    document = minidom.parseString(contents["Templates.xml"])
    nodes = [n for n in document.getElementsByTagName("Template") if n.getAttribute("Type") == "CardTemplate" and n.getAttribute("Id") == str(target_id)]
    if len(nodes) != 1:
        raise ValueError("AudioUnsupported")
    nodes[0].setAttribute("AudioId", str(donor["AudioId"]))
    changed_xml = document.toxml(encoding="utf-8")
    if ET.tostring(ET.fromstring(changed_xml)) != ET.tostring(expected):
        raise ValueError("Unrelated card data changed")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="audio-reference-", dir=output.parent) as temporary:
        work = Path(temporary)
        (work / "payload").mkdir(); (work / "backup").mkdir()
        result_path = work / "payload/data_definitions"
        with zipfile.ZipFile(result_path, "w") as archive:
            for info in infos:
                archive.writestr(copy.copy(info), changed_xml if info.filename == "Templates.xml" else contents[info.filename])
        with zipfile.ZipFile(result_path) as archive:
            if archive.testzip() is not None or archive.namelist() != names:
                raise ValueError("Audio archive verification failed")
            for name in names:
                if archive.read(name) != (changed_xml if name == "Templates.xml" else contents[name]):
                    raise ValueError("Unrelated archive entry changed")
        (work / "backup/data_definitions").write_bytes(old)
        if (target / "data_definitions").read_bytes() != old or any(digest((target / p).read_bytes()) != h for p, h in dependencies.items()):
            raise ValueError("Client changed during build")
        manifest = dict(Version=1, Scope="native-card-audio-reference", TargetClient=str(target),
                        TargetCard=target_id, DonorCard=donor_id, BeforeAudio=recipient, AfterAudio=donor,
                        ProtectedAudioMetadata=dependencies, BanksUnchanged=True,
                        Files=[dict(Path="data_definitions", Before=digest(old), After=digest(result_path.read_bytes()))])
        write_json(work / "manifest.json", manifest)
        verify(work)
        os.replace(work, output)
    return manifest
