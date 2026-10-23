#!/usr/bin/env python3
"""Regenerates functions.golden.jsonl from function-expressions.txt with a real `terraform console`.

Usage: python3 generate.py [path/to/terraform]

Each line of function-expressions.txt is a Terraform expression. It is wrapped in jsonencode(...)
so the result can be compared structurally. Errors are recorded as {"ok": false}.
"""
import json
import os
import re
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor

terraform = sys.argv[1] if len(sys.argv) > 1 else "terraform"
here = os.path.dirname(os.path.abspath(__file__))
work = tempfile.mkdtemp()

version = subprocess.run([terraform, "version", "-json"], capture_output=True, text=True, cwd=work).stdout
version = json.loads(version)["terraform_version"]


ESCAPES = {"n": "\n", "t": "\t", "r": "\r", "\\": "\\", '"': '"'}


def unquote(quoted, expression):
    """Decodes the quoted string the console prints (Go-style escapes, HCL template escapes)."""
    if not (quoted.startswith('"') and quoted.endswith('"')):
        raise SystemExit(f"unexpected console output for {expression!r}: {quoted!r}")
    body = quoted[1:-1]
    result = []
    i = 0
    while i < len(body):
        ch = body[i]
        if ch != "\\":
            result.append(ch)
            i += 1
            continue
        kind = body[i + 1]
        if kind in ESCAPES:
            result.append(ESCAPES[kind])
            i += 2
        elif kind in "xuU":
            width = {"x": 2, "u": 4, "U": 8}[kind]
            result.append(chr(int(body[i + 2 : i + 2 + width], 16)))
            i += 2 + width
        else:
            raise SystemExit(f"unknown escape in console output for {expression!r}: {quoted!r}")
    return "".join(result).replace("$${", "${").replace("%%{", "%{")


def evaluate(expression):
    with tempfile.TemporaryDirectory() as directory:
        run = subprocess.run(
            [terraform, "console"],
            input=f"jsonencode({expression})\n",
            capture_output=True,
            text=True,
            cwd=directory,
        )
    output = (run.stdout + run.stderr).strip()
    if "Error" in output or run.returncode != 0:
        plain = re.sub(r"\x1b\[[0-9;]*m", "", output)
        lines = [l.strip(" │╷╵") for l in plain.splitlines() if l.strip(" │╷╵")]
        return {"expr": expression, "ok": False, "error": " ".join(lines[:3])}
    return {"expr": expression, "ok": True, "json": unquote(output, expression)}


with open(os.path.join(here, "function-expressions.txt"), encoding="utf-8") as handle:
    expressions = [line.rstrip("\n") for line in handle if line.strip()]

with ThreadPoolExecutor(max_workers=8) as pool:
    cases = list(pool.map(evaluate, expressions))

with open(os.path.join(here, "functions.golden.jsonl"), "w", encoding="utf-8") as out:
    out.write(json.dumps({"terraform": version}) + "\n")
    for case in cases:
        out.write(json.dumps(case, ensure_ascii=False) + "\n")

print(f"wrote {len(cases)} cases from terraform {version}")
