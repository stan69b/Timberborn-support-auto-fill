import sys, json

d = json.load(sys.stdin)
cmd = d.get("tool_input", {}).get("command", "")
if "dotnet build" not in cmd:
    sys.exit(0)

manifest = "/Users/stan/Documents/Timberborn/Mods/Platform Autofill/manifest.json"
with open(manifest) as f:
    m = json.load(f)

v = m["Version"].split(".")
v[2] = str(int(v[2]) + 1)
m["Version"] = ".".join(v)

with open(manifest, "w") as f:
    json.dump(m, f, indent=2)

print("[PlatformAutofill] Version bumped to " + m["Version"])
