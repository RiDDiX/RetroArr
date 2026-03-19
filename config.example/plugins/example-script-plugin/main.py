#!/usr/bin/env python3
"""
Example script plugin for RetroArr.
Reads game file info from stdin JSON and returns suggested tags based on file extensions.
"""
import json
import sys

EXTENSION_TAGS = {
    ".nsp": ["Nintendo Switch", "NSP"],
    ".xci": ["Nintendo Switch", "XCI"],
    ".iso": ["Disc Image", "ISO"],
    ".bin": ["Disc Image", "BIN"],
    ".cue": ["Disc Image", "CUE"],
    ".nes": ["NES", "ROM"],
    ".sfc": ["SNES", "ROM"],
    ".smc": ["SNES", "ROM"],
    ".gba": ["Game Boy Advance", "ROM"],
    ".gb": ["Game Boy", "ROM"],
    ".gbc": ["Game Boy Color", "ROM"],
    ".n64": ["Nintendo 64", "ROM"],
    ".z64": ["Nintendo 64", "ROM"],
    ".exe": ["PC", "Windows"],
    ".app": ["macOS"],
}

def main():
    try:
        raw = sys.stdin.readline()
        data = json.loads(raw) if raw.strip() else {}
    except Exception:
        data = {}

    files = data.get("files", [])
    tags = set()

    for f in files:
        ext = f.rsplit(".", 1)[-1] if "." in f else ""
        ext_key = f".{ext.lower()}"
        if ext_key in EXTENSION_TAGS:
            tags.update(EXTENSION_TAGS[ext_key])

    result = {
        "source": "example-script-plugin",
        "suggestedTags": sorted(tags),
        "fileCount": len(files)
    }

    print(json.dumps(result))

if __name__ == "__main__":
    main()
