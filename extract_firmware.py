#!/usr/bin/env python3
"""Extract firmware resources (code_2M, param_128K) from the decompiled .resx file."""

import base64
import xml.etree.ElementTree as ET
import sys

RESX = "decompiled/WindowsFormsApplication1.Properties.Resources.resx"

tree = ET.parse(RESX)
root = tree.getroot()

for data in root.findall("data"):
    name = data.get("name")
    if name in ("code_2M", "param_128K"):
        value_el = data.find("value")
        raw = base64.b64decode(value_el.text)
        out = f"{name}.bin"
        with open(out, "wb") as f:
            f.write(raw)
        print(f"{name}: {len(raw)} bytes -> {out}")
