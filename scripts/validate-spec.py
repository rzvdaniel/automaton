#!/usr/bin/env python3
"""Validate catalog.json and automaton.json files against the published spec v1 schemas.

Usage: validate-spec.py catalog <file-or-url>... | app <file>...
Exit code 1 when any file does not validate."""
import json, sys, urllib.request
from pathlib import Path
from jsonschema import Draft202012Validator

SCHEMAS = Path(__file__).resolve().parent.parent / "src/automaton-vue/public/schemas"

def load(source):
    if source.startswith("https://"):
        with urllib.request.urlopen(source, timeout=30) as response:
            return json.load(response)
    return json.loads(Path(source).read_text(encoding="utf-8"))

def main(argv):
    if len(argv) < 3 or argv[1] not in ("catalog", "app"):
        print(__doc__); return 2
    schema = json.loads((SCHEMAS / ("catalog.schema.json" if argv[1] == "catalog" else "automaton.schema.json")).read_text(encoding="utf-8"))
    validator = Draft202012Validator(schema)
    failed = False
    for source in argv[2:]:
        errors = sorted(validator.iter_errors(load(source)), key=lambda e: list(e.path))
        for error in errors:
            failed = True
            print(f"{source}: {'/'.join(map(str, error.path)) or '(root)'}: {error.message}")
        if not errors: print(f"{source}: valid")
    return 1 if failed else 0

if __name__ == "__main__":
    sys.exit(main(sys.argv))
