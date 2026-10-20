#!/usr/bin/env python3
"""Regenerates web/frontend/src/content/monsterAttacks.json from the three monster prefabs' AttackDefinition tables.
Run from the repository root:  python3 web/tools/extract-monster-attacks.py
(name, category and windUp are serialized as plain YAML in the prefab; categories are MonsterAI.AttackCategory.)"""
import json, re

CATS = {0: "Standard", 1: "Heavy", 2: "IdleSlam", 3: "Special", 4: "Combo"}
FILES = {"Easy": "Monster_Easy_HipHop", "Medium": "Monster_Medium_VampireLust", "Hard": "Monster_Hard_TwinAxeBrute"}
OUT = "web/frontend/src/content/monsterAttacks.json"

old = json.load(open(OUT))
levels = {}
for level, f in FILES.items():
    text = open(f"Assets/Prefabs/Monsters/{f}.prefab").read()
    attacks = {}
    for block in re.split(r"\n\s+- name: ", text)[1:]:
        name = block.split("\n")[0].strip()
        w = re.search(r"\n\s+windUp: ([0-9.eE+-]+)", block)
        c = re.search(r"\n\s+category: (\d+)", block)
        if w and c:
            attacks[name] = {"category": CATS[int(c.group(1))], "windUp": round(float(w.group(1)), 3)}
    levels[level] = attacks
old["levels"] = levels
json.dump(old, open(OUT, "w"), indent=2)
print({k: len(v) for k, v in levels.items()}, "->", OUT)
