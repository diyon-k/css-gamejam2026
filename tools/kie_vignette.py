"""Generate 4 blood-vignette stages with Kie (Nano Banana) on white, then key white to alpha.
Usage: python kie_vignette.py <repo_root> <model>"""
import json, sys, time, urllib.request
from pathlib import Path
from PIL import Image

repo = Path(sys.argv[1]); model = sys.argv[2]
key = (repo / ".env.kie").read_text().split("=", 1)[1].strip()
out = repo / "Unity/Assets/Art/Effects/DamageVignette"; out.mkdir(parents=True, exist_ok=True)
raw = Path(__file__).parent / "_kie_raw"; raw.mkdir(exist_ok=True)

BASE = ("First-person video game damage overlay. Pure flat white #FFFFFF background filling the whole frame, "
        "completely white and empty in the centre. Realistic dark red blood splatter, smears, drips and spray "
        "only around the screen edges and corners, as if blood is on the camera lens. No text, no objects, "
        "no people, no shadows, no frame, no gradient on the white. ")
STAGES = [
    "Stage 1 of 4, very light: a few small thin blood specks and one small smear in the corners only; at least 85% of the image is pure white.",
    "Stage 2 of 4, moderate: blood smears and splatter in all four corners creeping a little along the edges; about 70% of the image is pure white.",
    "Stage 3 of 4, heavy: thick blood smears and drips covering the corners and running along all edges; about 50% of the image is pure white, centre clear.",
    "Stage 4 of 4, extreme: very heavy thick dark blood soaking all edges and corners deep toward the centre with long drips; only an oval in the centre about 30% of the image stays pure white.",
]

def call(url, body=None):
    req = urllib.request.Request(url, data=json.dumps(body).encode() if body else None,
                                 headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"})
    r = json.load(urllib.request.urlopen(req, timeout=60))
    if r.get("code") != 200: raise RuntimeError(r)
    return r["data"]

def generate(prompt):
    tid = call("https://api.kie.ai/api/v1/jobs/createTask",
               {"model": model, "input": {"prompt": prompt, "aspect_ratio": "16:9", "image_size": "16:9",
                                          "resolution": "2K", "output_format": "png"}})["taskId"]
    for _ in range(120):
        time.sleep(3)
        d = call(f"https://api.kie.ai/api/v1/jobs/recordInfo?taskId={tid}")
        if d["state"] == "success": return json.loads(d["resultJson"])["resultUrls"][0]
        if d["state"] == "fail": raise RuntimeError(d.get("failMsg"))
    raise TimeoutError(tid)

def key_white(src, dst):
    """Over white: c = a*C + (1-a). Blood has ~0 green, so a = 1 - G; then un-blend the colour."""
    im = Image.open(src).convert("RGB")
    px = []
    for r, g, b in im.getdata():
        a = 1 - g / 255
        a = 0 if a < 0.04 else a  # kill off-white noise
        if a == 0: px.append((0, 0, 0, 0)); continue
        un = lambda c: max(0, min(255, round((c / 255 - (1 - a)) / a * 255)))
        px.append((un(r), un(g), un(b), round(a * 255)))
    o = Image.new("RGBA", im.size); o.putdata(px); o.save(dst)

stages = [int(s) for s in sys.argv[3:]] or [1, 2, 3, 4]
for i in stages:
    url = generate(BASE + STAGES[i - 1])
    src = raw / f"stage{i}.png"
    urllib.request.urlretrieve(url, src)
    key_white(src, out / f"DamageVignette_Stage{i}.png")
    print("stage", i, "ok", url)
