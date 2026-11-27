import json, os, subprocess, sys, tempfile
here = os.path.dirname(os.path.abspath(__file__))
script = os.path.join(here, "..", "ci", "scan-report.py")
d = tempfile.mkdtemp()
def run(alerts, nmap="", allow="", name="t"):
    z = os.path.join(d, "z.json"); json.dump({"site": [{"@name": "s", "alerts": alerts}]}, open(z, "w"))
    a = os.path.join(d, "a.txt"); open(a, "w").write(allow)
    n = os.path.join(d, "n.gnmap"); open(n, "w").write(nmap)
    r = subprocess.run([sys.executable, "-I", script, "--allowlist", a, "--zap", f"{name}={z}", "--nmap", n], capture_output=True, text=True)
    return r.returncode, r.stdout
csp = {"pluginid": "10038", "alert": "CSP Header Not Set", "riskcode": "2", "instances": [{"uri": "https://x/store"}]}
med = {"pluginid": "90001", "alert": "Some medium", "riskcode": "2", "instances": [{"uri": "https://x/"}]}
ok = "Host: 1.2.3.4 ()\tPorts: 22/open/tcp//ssh///, 443/open/tcp//https///"
assert run([], ok)[0] == 0
assert run([med], ok)[0] == 0
rc, out = run([csp], ok); assert rc == 1 and "CSP Header Not Set" in out
assert run([csp], ok, "10038 /store accepted for now\n")[0] == 0
assert run([], ok + ", 5432/open/tcp//postgresql///")[0] == 1
assert run([{"pluginid": "1", "alert": "h", "riskcode": "3", "instances": []}], ok)[0] == 1
leak = {"pluginid": "90022", "alert": "Application Error Disclosure", "riskcode": "2", "instances": [{"uri": "https://x/api/apps/1"}]}
assert run([leak], ok)[0] == 0  # unsigned baseline: only a medium
rc, out = run([leak], ok, name="api-spec"); assert rc == 1 and "Application Error Disclosure" in out
fj = os.path.join(d, "f.json")
z = os.path.join(d, "z.json"); json.dump({"site": [{"@name": "s", "alerts": [csp, med]}]}, open(z, "w"))
a = os.path.join(d, "a.txt"); open(a, "w").write("")
subprocess.run([sys.executable, "-I", script, "--allowlist", a, "--zap", f"t={z}", "--failing-json", fj], capture_output=True, text=True)
assert [f["id"] for f in json.load(open(fj))] == ["10038"]  # only the failing finding, not the medium
print("ok")
