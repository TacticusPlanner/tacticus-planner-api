"""Round-6 catalog transform (runs on the post-round-5 Data tree).

Normalize the challenge battle ids so the trailing node segment has no leading zero:
`DGSC03B` -> `DGSC3B` (node 3), while `DGSC13B` / `DGSC25B` keep their two-digit node. Scope is
the event Challenge battles only (difficulty `eventStandardChallenge` / `eventExtremisChallenge`,
i.e. the `...<node>B` ids); standard/elite/mirror/event nodes keep their existing zero-padded ids.

Battle ids are not referenced by any other catalog dataset and are not stored in the manifest
(keys/files only; hashes are computed at runtime), so no schemaVersion bump is needed.
"""
import json
import os
import glob
import re

API = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(API, "src", "TacticusPlanner.Catalog", "Data")
CB_DIR = os.path.join(DATA, "campaign-battles")

CHALLENGE_DIFFS = {"eventStandardChallenge", "eventExtremisChallenge"}


def load(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def write(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
        f.write("\n")


def normalize_id(battle_id):
    # strip leading zeros from the trailing "<digits>B" node segment
    return re.sub(r"0*(\d+)B$", r"\1B", battle_id)


renamed = 0
for path in sorted(glob.glob(os.path.join(CB_DIR, "campaign-battles-*.json"))):
    g = load(path)
    changed = False
    for b in g["battles"]:
        if b["difficulty"] in CHALLENGE_DIFFS:
            new_id = normalize_id(b["id"])
            if new_id != b["id"]:
                b["id"] = new_id
                renamed += 1
                changed = True
    if changed:
        write(path, g)

# fail loudly if any challenge id still carries a leading-zero node segment
for path in glob.glob(os.path.join(CB_DIR, "campaign-battles-*.json")):
    for b in load(path)["battles"]:
        if b["difficulty"] in CHALLENGE_DIFFS:
            assert b["id"] == normalize_id(b["id"]), b["id"]

print("renamed challenge battle ids:", renamed)
