"""Developer-only extraction of the supported Rogue Mage static frame textures."""
import argparse
from pathlib import Path
from art_swap import streaming_assets, digest, write_json
from global_visuals import BORDERS, rogue_frame_image


def extract(client, output):
    source, output = streaming_assets(client), Path(output).resolve()
    if output.exists() or output.is_relative_to(source):
        raise ValueError("Output must be a new directory outside the game")
    output.mkdir(parents=True)
    records = {}
    for key in ("bronze", *BORDERS):
        relative = "bundledassets/vanityassets/cardborders/" + ("2082" if key == "bronze" else key) + "0480"
        raw = (source / relative).read_bytes()
        image = rogue_frame_image(raw, key)
        if image.size != (1024, 2048):
            raise ValueError("ArtTextureLayout")
        path = output / (key + ".png")
        image.save(path)
        records[key] = dict(Sha256=digest(path.read_bytes()), Source=relative, SourceSha256=digest(raw), Pixels=digest(image.tobytes()))
        if key == "2087":
            records[key]["Composition"] = "left then right, alpha composite; static only; no flame/eye shaders"
    write_json(output / "manifest.json", dict(Version=1, Origin="GWENT: Rogue Mage", Frames=records))
    return records


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    print(extract(args.client, args.output))
