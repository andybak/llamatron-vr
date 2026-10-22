"""Index retained REA function Evidence, verifying analysis-entry source identity."""

import argparse
import hashlib
import json
import struct
from pathlib import Path


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=Path("artifacts/reconstruction"))
    parser.add_argument("--original", type=Path, default=Path("artifacts/analysis/GAME86.unpacked.exe"))
    parser.add_argument("--output", type=Path, default=Path("docs/rea-evidence-index.json"))
    args = parser.parse_args()
    original = args.original.read_bytes()
    original_hash = digest(original)
    header_bytes = struct.unpack_from("<H", original, 8)[0] * 16
    image_hash = digest(original[header_bytes:])
    entries = []
    files = sorted(set(args.directory.glob("rea-*-function.json")) | set(args.directory.glob("rea-*-code.json")))
    for path in files:
        raw = path.read_bytes()
        record = json.loads(raw)
        evidence = record.get("structuredContent", {})
        if record.get("isError") or not evidence.get("evidence_id"):
            raise ValueError(f"Expected successful retained REA Evidence: {path}")
        subject = evidence["subject"]
        item = {"record": path.as_posix(), "record_sha256": digest(raw),
                "evidence_id": evidence["evidence_id"], "operation": evidence["operation"],
                "provider": evidence["provider"], "input_sha256": subject["digest"]["sha256"],
                "input_kind": "unpacked original"}
        if ".entry-" in subject["name"]:
            derived_path = args.directory / subject["name"]
            proof_path = derived_path.with_suffix(".provenance.json")
            proof = json.loads(proof_path.read_text())
            derived = derived_path.read_bytes()
            if (proof["source_sha256"] != original_hash or
                proof["derived_sha256"] != item["input_sha256"] or digest(derived) != item["input_sha256"] or
                proof["unchanged_load_image_sha256"] != image_hash or
                struct.unpack_from("<H", derived, 20)[0] != proof["derived_initial_ip"] or
                derived[:20] != original[:20] or derived[22:] != original[22:]):
                raise ValueError(f"Entry-hint provenance or source mismatch: {path}")
            item.update(input_kind="analysis-only header entry hint", provenance=proof_path.as_posix(),
                        original_unpacked_sha256=original_hash, unchanged_load_image_sha256=image_hash,
                        hint_cs_offset=f"0x{proof['derived_initial_ip']:04x}")
        elif item["input_sha256"] != original_hash:
            raise ValueError(f"Unexpected analysis target: {path}")
        result = evidence["normalized_result"]
        if isinstance(result, dict) and "procedure" in result:
            item["function"] = result["procedure"]["address"]
            item["body"] = result["procedure"]["body"]
        entries.append(item)
    gameplay = json.loads((args.directory / "gameplay.json").read_text())
    if gameplay["image_sha256"] != image_hash:
        raise ValueError("Gameplay recovery and REA use different load images")
    report = {
        "rea_version": "6.5.0", "host": "Ubuntu WSL Linux x86_64",
        "ghidra_version": "12.1.4", "jdk_version": "Temurin 21.0.12.1+1",
        "packed_original_sha256": gameplay["original_sha256"],
        "load_address_note": "Ghidra address 0x10000 + original CS offset; DS relocates from 0x2559 to 0x3559.",
        "records": entries,
        "limitations": [
            "Full Evidence records are local ignored artifacts, not embedded in this index.",
            "Hinted inputs change only initial IP; code, data and relocations are unchanged.",
            "Ghidra pseudocode has register/flag and stack artifacts; consult assembly and original bytes.",
            "Static Evidence does not establish runtime parity or complete game reconstruction."]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(f"{json.dumps(report, indent=2)}\n", encoding="utf-8")
    print(f"Indexed {len(entries)} REA Evidence records; target bytes and entry-hint provenance verified.")


if __name__ == "__main__":
    main()
