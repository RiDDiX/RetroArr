#!/usr/bin/env python3
"""
Example metadata plugin for RetroArr.
Reads a game title from stdin JSON and returns supplemental metadata.
"""
import json
import sys

def main():
    try:
        raw = sys.stdin.readline()
        data = json.loads(raw) if raw.strip() else {}
    except Exception:
        data = {}

    title = data.get("title", "Unknown")

    # Example: return supplemental metadata
    result = {
        "source": "example-metadata-plugin",
        "title": title,
        "tags": ["indie", "retro"],
        "notes": f"Metadata enrichment for '{title}' provided by example plugin."
    }

    print(json.dumps(result))

if __name__ == "__main__":
    main()
