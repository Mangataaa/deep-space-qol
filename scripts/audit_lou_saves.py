"""Read-only Lou story-box audit. Can back up inputs; never rewrites a save."""

import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path


def parse_es3(text):
    # ES3 permits integer dictionary keys and literal backslashes in display names.
    # Convert only a temporary in-memory view; the original bytes are never changed.
    def token(match):
        if match["key"] is not None:
            return json.dumps(match["key"])
        return re.sub(r'\\(?!["\\/bfnrtu])', r"\\\\", match[0])
    normalized = re.sub(r'"(?:\\.|[^"\\])*"|(?P<key>(?<=[{,])-?\d+)(?=\s*:)', token, text)
    return json.loads(normalized, strict=False)


def tags(node):
    return dict(zip(node.get("_keys", []), node.get("_values", [])))


def enabled(node, tag):
    return tags(node).get(tag, {}).get("valueEnabled", False)


def audit(path):
    raw = path.read_bytes()
    data = parse_es3(raw.decode("utf-8-sig"))
    store = data["playerStore"]["value"]
    boxes = []
    for key, value in store.items():
        if not key.endswith("InvJSON") or not value:
            continue
        nodes = parse_es3(value)["saveItems"]
        by_id = {n["uuid"]: n for n in nodes}
        if len(by_id) != len(nodes):
            raise ValueError(f"Duplicate inventory UUID in {key}")
        for node in nodes:
            if len(node["childItems"]) != len(node["childItemInventoryNode"]):
                raise ValueError(f"Mismatched inventory references in {key}")
            if any(child not in by_id for child in node["childItems"]):
                raise ValueError(f"Missing child record in {key}")
            if not enabled(node, "LOU_TOY_BOX_TAG"):
                continue
            descendants, seen = [], set()
            pending = list(node["childItems"])
            while pending:
                uid = pending.pop(0)
                if uid in seen:
                    raise ValueError("Cycle or duplicate membership in Lou's box")
                seen.add(uid)
                child = by_id[uid]
                descendants.append(child)
                pending.extend(child["childItems"])
            toys = [c for c in descendants if enabled(c, "LOU_TOY_TAG")]
            expected = tags(node).get("LOU_TOY_COUNT_INT", {}).get("valueInt", 0)
            opened = enabled(node, "OPENED_TAG")
            boxes.append({
                "inventory": key, "uniqueId": node["uniqueId"],
                "expectedToyCount": expected, "actualToyCount": len(toys), "opened": opened,
                "nativeContentOutcome": "Taken" if len(toys) < expected else "Peeked" if opened else "Untouched",
                "toys": [{"id": c["identifier"], "uniqueId": c["uniqueId"], "units": c["unitCount"]} for c in toys],
            })
    return {"file": path.name, "sha256": hashlib.sha256(raw).hexdigest(),
            "day": data["storeStation"]["value"]["dayCounter"],
            "cash": store["playerCash"], "boxes": boxes, "saveRewritten": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("saves", type=Path, nargs="+")
    parser.add_argument("--backup-dir", type=Path)
    args = parser.parse_args()
    if len({p.name.casefold() for p in args.saves}) != len(args.saves):
        parser.error("Use distinct input filenames to avoid ambiguous backup names")
    reports = [audit(p) for p in args.saves]
    if args.backup_dir:
        # Fail if the backup exists rather than replacing another recovery snapshot.
        args.backup_dir.mkdir(parents=True, exist_ok=False)
        for path, report in zip(args.saves, reports):
            target = args.backup_dir / path.name
            shutil.copy2(path, target)
            expected = report["sha256"]
            if any(hashlib.sha256(p.read_bytes()).hexdigest() != expected for p in (path, target)):
                raise RuntimeError("Backup/source hash verification failed")
        (args.backup_dir / "audit.json").write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(reports, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
