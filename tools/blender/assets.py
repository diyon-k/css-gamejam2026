"""BLINK mansion hero assets. One function per asset; each builds into the empty scene and
returns the root object. Front = -Y, origin = base centre unless the README says otherwise."""
import math

from mathutils import Matrix

from lib import (box, box_z, child, cyl, dup, empty, extrude_poly, grid, jitter, join, lathe,
                 radial, rng, rounded_box, set_mat, set_origin, solidify, sweep_x, torus, tube, xf)

BUILDERS = {}
VIEW = {}   # preview camera direction overrides
SHARP = {}  # sharp-edge angle overrides


def asset(view=None, sharp=None):
    def deco(fn):
        BUILDERS[fn.__name__] = fn
        if view:
            VIEW[fn.__name__] = view
        if sharp:
            SHARP[fn.__name__] = sharp
        return fn
    return deco


# ---------------------------------------------------------------- shared bits
def candle_stick(name, base_z, h, r=0.012, drips=2):
    """Wax candle standing at base_z with a melted dip, wick and a couple of drips. Returns (parts, wick_top_z)."""
    top = base_z + h
    parts = [lathe(name, [(0, base_z), (r, base_z), (r * 1.02, top - 0.012), (r * 0.9, top - 0.002),
                          (r * 0.55, top - 0.006), (0, top - 0.007)], "Wax", 10)]
    for i in range(drips):
        a = rng.uniform(0, 2 * math.pi)
        ln = rng.uniform(0.02, 0.05)
        d = lathe(name + "_drip", [(0, -ln), (0.0022, -ln + 0.003), (0.0026, -0.004), (0.0015, 0.0), (0, 0.001)],
                  "Wax", 6)
        parts.append(xf(d, (math.cos(a) * r * 0.93, math.sin(a) * r * 0.93, top - 0.008)))
    parts.append(cyl(name + "_wick", 0.0015, 0.014, "Iron", (0, 0, top - 0.008), seg=5))
    return parts, top + 0.006


def bobeche(name, z, r=0.05):
    """Brass drip-pan + candle socket; candle sits at returned z."""
    return lathe(name, [(0, z - 0.035), (0.012, z - 0.035), (0.016, z - 0.02), (r * 0.9, z - 0.004),
                        (r, z + 0.002), (r * 0.92, z + 0.004), (0.02, z), (0.018, z + 0.02),
                        (0.0, z + 0.02)], "Brass", 16), z + 0.02


# ---------------------------------------------------------------- 1 Chandelier
@asset(view=(0.9, -1.6, 0.3))
def Chandelier():
    p = []
    # hang ring + chain (origin = hang point at top)
    p.append(torus("Ring", 0.022, 0.005, "Brass", (0, 0, -0.025), (90, 0, 0), seg=12, rseg=5))
    z = -0.05
    for i in range(7):
        p.append(torus("Link", 0.016, 0.0035, "Iron", (0, 0, z - 0.02), (90, 0, 90 * (i % 2)),
                       seg=10, rseg=4, scale=(1, 1.5, 1)))
        z -= 0.04
    top = z - 0.01  # ~-0.34
    # turned brass body
    prof = [(0, top), (0.022, top), (0.03, top - 0.02), (0.016, top - 0.05), (0.016, top - 0.11),
            (0.045, top - 0.15), (0.06, top - 0.2), (0.05, top - 0.24), (0.025, top - 0.27), (0.02, top - 0.3),
            (0.085, top - 0.36), (0.1, top - 0.38), (0.1, top - 0.4), (0.085, top - 0.42), (0.035, top - 0.46),
            (0.045, top - 0.52), (0.03, top - 0.58), (0.012, top - 0.64), (0.02, top - 0.67), (0, top - 0.7)]
    p.append(lathe("Body", prof, "Brass", 16))
    hub_z = top - 0.39
    # arms: S-curve out and up to a drip pan; one candle per arm
    n = 6
    R = 0.44
    anchors = []
    for i in range(n):
        a = 360.0 * i / n
        arm = tube("Arm", [(0.08, 0, hub_z), (0.2, 0, hub_z - 0.1), (0.34, 0, hub_z - 0.07),
                           (R, 0, hub_z + 0.04), (R, 0, hub_z + 0.1)], 0.011, "Brass", res=5, bevres=1)
        set_mat(arm, "Brass")
        cup, cz = bobeche("Bobeche", hub_z + 0.1 + 0.035)
        xf(cup, (R, 0, 0))
        h = rng.uniform(0.08, 0.14)
        cparts, wick = candle_stick("Candle", cz, h)
        for c in cparts:
            xf(c, (R, 0, 0))
        drops = []
        for k in (-1, 1):
            d = lathe("Drop", [(0, 0), (0.008, -0.012), (0.006, -0.026), (0, -0.04)], "Glass", 6)
            drops.append(xf(d, (R + 0.045 * k * 0.0, 0.045 * k, hub_z + 0.1)))
        group = [arm, cup] + cparts + drops
        for g in group:
            xf(g, rot=(0, 0, a))
        p += group
        anchors.append((R * math.cos(math.radians(a)), R * math.sin(math.radians(a)), wick))
    # ring of crystal drops under the hub
    for i in range(12):
        a = 2 * math.pi * (i + 0.5) / 12
        d = lathe("Drop", [(0, 0), (0.007, -0.01), (0.005, -0.03), (0, -0.045)], "Glass", 6)
        p.append(xf(d, (0.095 * math.cos(a), 0.095 * math.sin(a), hub_z - 0.02)))
    root = join(p, "Chandelier")
    for i, an in enumerate(anchors):
        empty(f"FlameAnchor_{i}", an, root)
    return root


# ---------------------------------------------------------------- 2 Fireplace
@asset()
def Fireplace():
    S = "Stone"
    D = 0.42
    y0, y1 = -D / 2, D / 2
    j = 0.003
    p = []
    for sx in (-1, 1):
        x = sx * 0.72
        p.append(box_z("Plinth", (x - 0.18, x + 0.18), (y0 - 0.02, y1), (0, 0.13), S, bev=0.012, jit=j))
        p.append(box_z("Pilaster", (x - 0.15, x + 0.15), (y0, y1), (0.13, 1.0), S, bev=0.01, jit=j))
        for k in (-1, 0, 1):  # fluting reads as three raised reeds
            p.append(box_z("Reed", (x + k * 0.08 - 0.022, x + k * 0.08 + 0.022), (y0 - 0.015, y0 + 0.01),
                           (0.2, 0.93), S, bev=0.01, jit=j))
        p.append(box_z("Capital", (x - 0.18, x + 0.18), (y0 - 0.03, y1), (1.0, 1.06), S, bev=0.012, jit=j))
        p.append(box_z("Cheek", (sx * 0.45, sx * 0.57) if sx > 0 else (-0.57, -0.45), (y0 + 0.02, y1),
                       (0, 0.85), S, bev=0.01, jit=j))
    p.append(box_z("Frieze", (-0.57, 0.57), (y0 + 0.01, y1), (0.85, 1.0), S, bev=0.01, jit=j))
    p.append(box_z("Lintel", (-0.5, 0.5), (y0 - 0.005, y1), (0.8, 0.86), S, bev=0.008, jit=j))
    key = extrude_poly("Keystone", [(-0.05, 0.79), (0.05, 0.79), (0.07, 1.0), (-0.07, 1.0)], 0.06, S, bev=0.006)
    p.append(xf(key, (0, y0 - 0.01, 0)))
    p.append(box_z("Cornice", (-0.94, 0.94), (y0 - 0.05, y1), (1.06, 1.1), S, bev=0.015, jit=j))
    shelf = sweep_x("Shelf", [(y0 - 0.1, 1.1), (y1, 1.1), (y1, 1.17), (y0 - 0.08, 1.17), (y0 - 0.1, 1.155),
                              (y0 - 0.11, 1.13), (y0 - 0.1, 1.11)], -1.0, 1.0, S)
    p.append(jitter(shelf, j))
    # firebox (sooty iron lining) + hearth
    p.append(box_z("BackWall", (-0.45, 0.45), (y1 - 0.06, y1), (0, 0.8), "Iron", bev=0.004))
    for sx in (-1, 1):
        side = box_z("FireSide", (-0.03, 0.0), (y0 + 0.02, y1 - 0.05), (0, 0.8), "Iron", bev=0.004)
        p.append(xf(side, rot=(0, 0, sx * 12), loc=(sx * 0.45, 0, 0)))
    p.append(box_z("SmokeShelf", (-0.45, 0.45), (y0 + 0.04, y1), (0.78, 0.8), "Iron", bev=0.003))
    p.append(box_z("Hearth", (-1.0, 1.0), (y0 - 0.5, y0 + 0.02), (0, 0.05), S, bev=0.012, jit=0.004))
    p.append(box_z("InnerHearth", (-0.45, 0.45), (y0, y1), (0, 0.04), S, bev=0.006))
    # grate + andirons
    for k in range(5):
        p.append(cyl("GrateBar", 0.008, 0.6, "Iron", (-0.3, -0.12 + k * 0.05, 0.12), (0, 90, 0), seg=6))
    for sx in (-1, 1):
        x = sx * 0.3
        p.append(box_z("GrateLeg", (x - 0.01, x + 0.01), (-0.15, 0.1), (0.04, 0.13), "Iron", bev=0.003))
        p.append(lathe("Andiron", [(0, 0.04), (0.03, 0.04), (0.012, 0.06), (0.01, 0.2), (0.022, 0.22),
                                   (0.028, 0.245), (0.02, 0.27), (0, 0.28)], "Brass", 10, loc=(x, y0 - 0.02, 0)))
        p.append(box_z("AndironFoot", (x - 0.012, x + 0.012), (y0 - 0.05, -0.05), (0.04, 0.06), "Iron", bev=0.003))
    root = join(p, "Fireplace")
    # logs: separate child so it can be swapped/lit
    logs = []
    for (lx, ly, lz, rz, ry, r) in ((0, -0.07, 0.19, 8, 0, 0.065), (0.02, 0.08, 0.19, -6, 0, 0.06),
                                    (0, 0.0, 0.29, 35, -8, 0.055)):
        lg = lathe("Log", [(0, -0.3), (r * 0.8, -0.3), (r, -0.28), (r * 1.05, 0), (r * 0.95, 0.28),
                           (r * 0.75, 0.3), (0, 0.3)], "Wood_Dark", 9)
        jitter(lg, 0.008)
        logs.append(xf(lg, (lx, ly, lz), (90, 0, 90 + rz)))
        xf(logs[-1], rot=(0, ry, 0))
        stub = cyl("Branch", 0.02, 0.08, "Wood_Dark", (lx + 0.1, ly, lz), (rng.uniform(-50, 50), 60, 0), seg=6)
        logs.append(stub)
    lg = join(logs, "Logs")
    set_origin(lg, (0, 0, 0.12))
    child(lg, root)
    return root


# ---------------------------------------------------------------- 3 GrandfatherClock
@asset()
def GrandfatherClock():
    W = "Wood_Dark"
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(lathe("Foot", [(0, 0), (0.03, 0), (0.036, 0.02), (0.03, 0.045), (0.0, 0.05)], W, 10,
                           loc=(sx * 0.22, sy * 0.1, 0)))
    p.append(box_z("Plinth", (-0.27, 0.27), (-0.145, 0.145), (0.045, 0.12), W, bev=0.008))
    p.append(box_z("Base", (-0.245, 0.245), (-0.125, 0.125), (0.12, 0.56), W, bev=0.006))
    p.append(box_z("BasePanel", (-0.17, 0.17), (-0.135, -0.12), (0.18, 0.5), "Wood_Light", bev=0.006))
    p.append(box_z("Waist1", (-0.255, 0.255), (-0.135, 0.135), (0.56, 0.6), W, bev=0.008))
    # hollow trunk with a glazed door showing the pendulum
    tz0, tz1 = 0.6, 1.47
    p.append(box_z("TrunkBack", (-0.19, 0.19), (0.09, 0.11), (tz0, tz1), W, bev=0.003))
    for sx in (-1, 1):
        p.append(box_z("TrunkSide", (sx * 0.19 - 0.02, sx * 0.19), (-0.11, 0.11), (tz0, tz1), W, bev=0.004)
                 if sx > 0 else box_z("TrunkSide", (-0.19, -0.17), (-0.11, 0.11), (tz0, tz1), W, bev=0.004))
        p.append(box_z("Stile", (sx * 0.17 - 0.035, sx * 0.17) if sx > 0 else (-0.17, -0.135),
                       (-0.125, -0.1), (tz0 + 0.02, tz1 - 0.02), W, bev=0.005))
    p.append(box_z("RailLow", (-0.17, 0.17), (-0.125, -0.1), (tz0 + 0.02, tz0 + 0.12), W, bev=0.005))
    p.append(box_z("RailTop", (-0.17, 0.17), (-0.125, -0.1), (tz1 - 0.08, tz1 - 0.02), W, bev=0.005))
    p.append(box_z("TrunkGlass", (-0.135, 0.135), (-0.113, -0.109), (tz0 + 0.12, tz1 - 0.08), "Glass", bev=0))
    p.append(lathe("Keyhole", [(0, 0), (0.008, 0), (0.008, 0.003), (0, 0.003)], "Brass", 8,
                   loc=(0.15, -0.125, 1.0), rot=(90, 0, 0)))
    p.append(box_z("Waist2", (-0.225, 0.225), (-0.13, 0.13), (tz1, tz1 + 0.05), W, bev=0.008))
    # hood
    hz0, hz1 = tz1 + 0.05, 1.95
    p.append(box_z("Hood", (-0.23, 0.23), (-0.12, 0.13), (hz0, hz1), W, bev=0.006))
    for sx in (-1, 1):
        p.append(lathe("Column", [(0, 0), (0.022, 0), (0.022, 0.02), (0.014, 0.03), (0.012, 0.36),
                                  (0.02, 0.38), (0.02, 0.4), (0, 0.4)], "Wood_Light", 10,
                       loc=(sx * 0.215, -0.135, hz0 + 0.02)))
        p.append(lathe("ColCap", [(0, 0), (0.016, 0), (0.016, 0.012), (0, 0.012)], "Brass", 10,
                       loc=(sx * 0.215, -0.135, hz0 + 0.02)))
    dial_z = 1.72
    dy = -0.12
    p.append(box_z("DialPlate", (-0.19, 0.19), (dy - 0.01, dy), (1.53, 1.92), "Brass", bev=0.003))
    p.append(cyl("Dial", 0.155, 0.008, "Ceramic", (0, dy - 0.01, dial_z), (90, 0, 0), seg=32))
    p.append(torus("Bezel", 0.158, 0.006, "Brass", (0, dy - 0.018, dial_z), (90, 0, 0), seg=32, rseg=5))
    p.append(torus("Chapter", 0.11, 0.0015, "Iron", (0, dy - 0.019, dial_z), (90, 0, 0), seg=32, rseg=3,
                   scale=(1, 1, 1)))
    for i in range(12):
        big = i % 3 == 0
        mk = box("Marker", (0.007 if big else 0.004, 0.002, 0.03 if big else 0.018), "Iron", bev=0)
        xf(mk, (0, 0, 0.128))
        xf(mk, rot=(0, i * 30, 0))
        p.append(xf(mk, (0, dy - 0.0185, dial_z)))
    p.append(cyl("Winder", 0.006, 0.006, "Brass", (-0.06, dy - 0.012, dial_z - 0.04), (90, 0, 0), seg=8))
    p.append(cyl("Winder", 0.006, 0.006, "Brass", (0.06, dy - 0.012, dial_z - 0.04), (90, 0, 0), seg=8))
    # bonnet: cornice + swan-neck pediment + finials
    p.append(box_z("Cornice", (-0.26, 0.26), (-0.15, 0.15), (hz1, hz1 + 0.05), W, bev=0.01))
    for sx in (-1, 1):
        ped = extrude_poly("Pediment", [(0.02, 0), (0.24, 0), (0.24, 0.05), (0.12, 0.1), (0.04, 0.13),
                                        (0.02, 0.1)], 0.04, W, bev=0.004)
        if sx < 0:
            ped.data.transform(Matrix.Diagonal((-1, 1, 1, 1)))
            ped.data.flip_normals()
        p.append(xf(ped, (0, -0.125, hz1 + 0.05)))
        p.append(lathe("Finial", [(0, 0), (0.02, 0), (0.02, 0.012), (0.01, 0.02), (0.016, 0.035), (0.01, 0.05),
                                  (0.003, 0.07), (0, 0.075)], "Brass", 10, loc=(sx * 0.23, -0.1, hz1 + 0.05)))
    p.append(lathe("FinialTop", [(0, 0), (0.03, 0), (0.025, 0.02), (0.012, 0.03), (0.022, 0.05), (0.015, 0.075),
                                 (0.004, 0.1), (0, 0.105)], "Brass", 12, loc=(0, -0.105, hz1 + 0.05)))
    root = join(p, "GrandfatherClock")

    # hands: built pointing to 12 at the dial centre, in front of the dial
    def hand(name, length, width, y):
        pts = [(0, length), (-width * 0.5, length * 0.75), (-width * 0.25, length * 0.6), (-width * 0.15, 0.0),
               (-width * 0.5, -0.025), (0, -0.032), (width * 0.5, -0.025), (width * 0.15, 0.0),
               (width * 0.25, length * 0.6), (width * 0.5, length * 0.75)]
        h = extrude_poly(name, pts, 0.002, "Iron")
        xf(h, (0, y, dial_z))
        hub = cyl(name + "_hub", 0.009, 0.002, "Brass", (0, y - 0.001, dial_z), (90, 0, 0), seg=10)
        h = join([h, hub], name)
        set_origin(h, (0, y, dial_z))
        return child(h, root)

    hand("HourHand", 0.085, 0.022, dy - 0.024)
    hand("MinuteHand", 0.13, 0.016, dy - 0.03)
    # pendulum swings about X at its top (behind the trunk glass)
    pz = tz1 - 0.04
    rod = box_z("Rod", (-0.004, 0.004), (0.0, 0.006), (pz - 0.62, pz), "Brass", bev=0)
    bob = lathe("Bob", [(0, -0.008), (0.06, -0.005), (0.07, 0.0), (0.06, 0.005), (0, 0.008)], "Brass", 24)
    xf(bob, (0, 0.003, pz - 0.65), (90, 0, 0))
    pen = join([rod, bob], "Pendulum")
    set_origin(pen, (0, 0.003, pz))
    child(pen, root)
    return root


# ---------------------------------------------------------------- 4 FourPosterBed
def drape(name, p0, p1, z_top, height, m, pleats=8, amp=0.02, tatter=0.08, n=40, outward=1):
    """Pleated hanging fabric strip from p0 to p1 (XY), hanging from z_top, tattered hem."""
    g = grid(name, 1, 1, n, 4, m)
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    ln = math.hypot(dx, dy)
    nx, ny = dy / ln * outward, -dx / ln * outward
    hem = [rng.uniform(0, tatter) for _ in range(n + 2)]
    for v in g.data.vertices:
        u, t = v.co.x + 0.5, 0.5 - v.co.y            # u along, t 0 top -> 1 bottom
        k = min(int(u * n), n)
        h = height - hem[k] * t * t
        s = amp * math.sin(u * pleats * 2 * math.pi)
        v.co = (p0[0] + dx * u + nx * s, p0[1] + dy * u + ny * s, z_top - t * h)
    return solidify(g, 0.006, 0)


@asset()
def FourPosterBed():
    W = "Wood_Dark"
    X, Y = 0.76, 1.06
    p = []
    turned = [(0, 0), (0.036, 0), (0.042, 0.02), (0.03, 0.05), (0.027, 0.15), (0.044, 0.3), (0.048, 0.36),
              (0.03, 0.5), (0.025, 0.9), (0.034, 1.0), (0.024, 1.05), (0.022, 1.5), (0.03, 1.55), (0.03, 1.58),
              (0.02, 1.6), (0.025, 1.62), (0.012, 1.66), (0, 1.68)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * X, sy * Y
            p.append(box_z("PostBlock", (x - 0.045, x + 0.045), (y - 0.045, y + 0.045), (0.03, 0.55), W, bev=0.008))
            p.append(lathe("PostFoot", [(0, 0), (0.04, 0), (0.05, 0.015), (0.04, 0.03), (0, 0.03)], W, 12,
                           loc=(x, y, 0)))
            p.append(lathe("Post", turned, W, 10, loc=(x, y, 0.55)))
    for sx in (-1, 1):
        p.append(box_z("SideRail", (sx * X - 0.022, sx * X + 0.022), (-Y + 0.045, Y - 0.045), (0.28, 0.46), W,
                       bev=0.008))
        p.append(box_z("CanopyRail", (sx * X - 0.025, sx * X + 0.025), (-Y, Y), (2.0, 2.07), W, bev=0.006))
    for sy in (-1, 1):
        p.append(box_z("EndRail", (-X + 0.045, X - 0.045), (sy * Y - 0.022, sy * Y + 0.022), (0.28, 0.46), W,
                       bev=0.008))
        p.append(box_z("CanopyRail", (-X, X), (sy * Y - 0.025, sy * Y + 0.025), (2.0, 2.07), W, bev=0.006))
    for sx in (-1, 1):
        p.append(box_z("Cornice", (sx * X - 0.05, sx * X + 0.05), (-Y - 0.05, Y + 0.05), (2.07, 2.1), W, bev=0.01))
    for sy in (-1, 1):
        p.append(box_z("Cornice", (-X + 0.05, X - 0.05), (sy * Y - 0.05, sy * Y + 0.05), (2.07, 2.1), W, bev=0.01))
    p.append(box_z("Platform", (-0.73, 0.73), (-1.0, 1.0), (0.38, 0.42), "Wood_Light", bev=0.003))
    # headboard with arched crest + raised panels, lower footboard
    p.append(box_z("Headboard", (-X + 0.04, X - 0.04), (Y - 0.02, Y + 0.015), (0.46, 1.3), W, bev=0.005))
    xs = [(-X + 0.04) + i * (2 * X - 0.08) / 16 for i in range(17)]
    arch = [(X - 0.04, 0), (-X + 0.04, 0)] + [(x, 0.08 + 0.14 * math.cos(math.pi * x / 1.44)) for x in xs]
    p.append(xf(extrude_poly("Crest", arch, 0.05, W, bev=0.006), (0, Y, 1.27)))
    for i in range(3):
        cx = -0.46 + i * 0.46
        p.append(box_z("HeadPanel", (cx - 0.18, cx + 0.18), (Y - 0.032, Y - 0.015), (0.68, 1.22), W, bev=0.015))
    p.append(box_z("Footboard", (-X + 0.04, X - 0.04), (-Y - 0.015, -Y + 0.02), (0.46, 0.86), W, bev=0.005))
    p.append(box_z("FootCap", (-X + 0.03, X - 0.03), (-Y - 0.03, -Y + 0.03), (0.86, 0.9), W, bev=0.01))
    for i in range(3):
        cx = -0.46 + i * 0.46
        p.append(box_z("FootPanel", (cx - 0.18, cx + 0.18), (-Y - 0.028, -Y), (0.52, 0.8), W, bev=0.015))
    # soft goods
    mt = rounded_box("Mattress", (1.42, 1.96, 0.22), 0.06, "Fabric_Linen", (0, 0, 0.53), cuts=5, bulge=0.012)
    for v in mt.data.vertices:
        if v.co.z > 0.55:
            v.co.z -= 0.035 * max(0, 1 - (v.co.x / 0.71) ** 2) * max(0, 1 - (v.co.y / 0.98) ** 2)
    p.append(jitter(mt, 0.003))
    for sx in (-1, 1):
        pil = rounded_box("Pillow", (0.6, 0.36, 0.14), 0.06, "Fabric_Linen", cuts=5, bulge=0.035, jit=0.004)
        p.append(xf(pil, (sx * 0.34, 0.8, 0.7), (-25, sx * 4, sx * 6)))
    bl = grid("Blanket", 1.64, 1.5, 18, 15, "Fabric_Red")
    top = 0.672
    for v in bl.data.vertices:
        x, y = v.co.x, v.co.y - 0.27
        z = top - 0.035 * max(0, 1 - (x / 0.71) ** 2) * max(0, 1 - (y / 0.98) ** 2)
        z += 0.01 * (1 + math.sin(x * 9 + y * 3) * math.sin(y * 7 + 1)) + rng.uniform(0, 0.003)
        dx = max(0.0, abs(x) - 0.72)
        dy = max(0.0, -1.0 - y)
        if dx > 0:
            x = math.copysign(0.725 + 0.03 * min(1, dx / 0.1) + 0.012 * math.sin(y * 25), x)
            z -= dx
        if dy > 0:
            y = -1.005 - 0.03 * min(1, dy / 0.1) - 0.012 * math.sin(x * 25)
            z -= dy
        if y > 0.38:  # turned-back top edge bunches up
            z += 0.04 * math.sin((y - 0.38) / 0.1 * math.pi) ** 2
        v.co = (x, y, z)
    p.append(solidify(bl, 0.012, 1))
    # tattered red valance + head curtains
    o = 0.04
    for a, b in (((-X - o, -Y - o), (X + o, -Y - o)), ((X + o, -Y - o), (X + o, Y + o)),
                 ((X + o, Y + o), (-X - o, Y + o)), ((-X - o, Y + o), (-X - o, -Y - o))):
        p.append(drape("Valance", a, b, 2.09, 0.3, "Fabric_Red", pleats=10, amp=0.012, tatter=0.12, n=16))
    for sx in (-1, 1):
        p.append(drape("Curtain", (sx * (X + 0.06), Y - 0.02), (sx * (X + 0.06), Y - 0.45), 2.0, 1.75,
                       "Fabric_Red", pleats=4, amp=0.03, tatter=0.25, n=20, outward=sx))
    return join(p, "FourPosterBed")


# ---------------------------------------------------------------- 5 Wardrobe
def knob(name, loc, m="Brass", r=0.016):
    return lathe(name, [(0, 0), (0.01, 0), (0.006, 0.01), (0.006, 0.018), (r, 0.026), (r * 0.9, 0.036),
                        (0, 0.038)], m, 10, loc=loc, rot=(90, 0, 0))


@asset()
def Wardrobe():
    W = "Wood_Dark"
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(lathe("BunFoot", [(0, 0), (0.03, 0), (0.045, 0.03), (0.035, 0.06), (0.03, 0.08), (0, 0.08)],
                           W, 10, loc=(sx * 0.55, sy * 0.25, 0)))
    p.append(box_z("Plinth", (-0.62, 0.62), (-0.32, 0.31), (0.08, 0.2), W, bev=0.01))
    p.append(box_z("Side", (-0.6, -0.575), (-0.3, 0.3), (0.2, 1.95), W, bev=0.004))
    p.append(box_z("Side", (0.575, 0.6), (-0.3, 0.3), (0.2, 1.95), W, bev=0.004))
    p.append(box_z("Back", (-0.575, 0.575), (0.28, 0.3), (0.2, 1.95), W, bev=0.002))
    p.append(box_z("Bottom", (-0.575, 0.575), (-0.3, 0.28), (0.2, 0.24), W, bev=0.003))
    p.append(box_z("Top", (-0.575, 0.575), (-0.3, 0.28), (1.9, 1.95), W, bev=0.003))
    p.append(box_z("Shelf", (-0.575, 0.575), (-0.26, 0.28), (1.62, 1.64), "Wood_Light", bev=0.003))
    p.append(cyl("HangRail", 0.012, 1.15, "Brass", (-0.575, 0.0, 1.52), (0, 90, 0), seg=10))
    for sx in (-1, 1):  # side panels
        p.append(box_z("SidePanel", (sx * 0.6 - 0.008, sx * 0.6 + 0.008), (-0.22, 0.22), (0.35, 1.8), W, bev=0.01))
    p.append(box_z("Cornice", (-0.64, 0.64), (-0.335, 0.32), (1.95, 2.0), W, bev=0.01))
    p.append(sweep_x("Crown", [(0.33, 2.0), (0.33, 2.1), (-0.37, 2.1), (-0.375, 2.085), (-0.36, 2.075),
                               (-0.355, 2.05), (-0.345, 2.03), (-0.335, 2.0)], -0.67, 0.67, W))
    root = join(p, "Wardrobe")

    def door(name, xa, xb, hinge):
        d = [box_z(name, (xa, xb), (-0.325, -0.302), (0.245, 1.895), W, bev=0.004)]
        for z0, z1 in ((0.32, 0.9), (1.0, 1.82)):
            d.append(box_z("Panel", (xa + 0.075, xb - 0.075), (-0.336, -0.322), (z0, z1), W, bev=0.016))
            d.append(box_z("PanelInset", (xa + 0.11, xb - 0.11), (-0.338, -0.33), (z0 + 0.035, z1 - 0.035), W,
                           bev=0.004))
        free = xb - 0.05 if hinge < 0 else xa + 0.05
        d.append(knob("Knob", (free, -0.325, 1.0)))
        if hinge > 0:
            d.append(extrude_poly("Escutcheon", [(-0.012, -0.03), (0.012, -0.03), (0.014, 0.0), (0.0, 0.03),
                                                 (-0.014, 0.0)], 0.003, "Brass"))
            xf(d[-1], (free, -0.3265, 0.93))
        for z in (0.4, 1.7):
            d.append(cyl("Hinge", 0.007, 0.08, "Brass", (hinge, -0.3, z), seg=8))
        o = join(d, name)
        set_origin(o, (hinge, -0.3, 0.245))
        return child(o, root)

    door("Door_L", -0.598, -0.002, -0.6)
    door("Door_R", 0.002, 0.598, 0.6)
    return root


# ---------------------------------------------------------------- 6 ChestOfDrawers
def bail(name, x, y, z):
    """Brass back-plate + hanging bail handle."""
    parts = [xf(extrude_poly(name + "_plate", [(-0.045, -0.012), (0.045, -0.012), (0.05, 0.0), (0.045, 0.012),
                                               (0.0, 0.02), (-0.045, 0.012), (-0.05, 0.0)], 0.003, "Brass"),
                (x, y, z))]
    parts.append(set_mat(tube(name + "_bail", [(x - 0.035, y - 0.006, z), (x - 0.03, y - 0.012, z - 0.025),
                                               (x, y - 0.014, z - 0.035), (x + 0.03, y - 0.012, z - 0.025),
                                               (x + 0.035, y - 0.006, z)], 0.0035, "Brass", res=4, bevres=1),
                         "Brass"))
    return parts


@asset()
def ChestOfDrawers():
    W = "Wood_Dark"
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(box_z("Foot", (sx * 0.47 - 0.04, sx * 0.47 + 0.04), (sy * 0.22 - 0.04, sy * 0.22 + 0.04),
                           (0, 0.09), W, bev=0.012))
    p.append(box_z("Plinth", (-0.515, 0.515), (-0.265, 0.255), (0.08, 0.125), W, bev=0.01))
    p.append(box_z("Side", (-0.5, -0.48), (-0.25, 0.25), (0.125, 0.92), W, bev=0.004))
    p.append(box_z("Side", (0.48, 0.5), (-0.25, 0.25), (0.125, 0.92), W, bev=0.004))
    p.append(box_z("Back", (-0.48, 0.48), (0.23, 0.25), (0.125, 0.92), W, bev=0.002))
    p.append(box_z("Top", (-0.525, 0.525), (-0.275, 0.26), (0.92, 0.955), W, bev=0.012))
    zones = [(0.145, 0.33), (0.35, 0.53), (0.55, 0.72), (0.74, 0.9)]
    rails = [(0.125, 0.145), (0.33, 0.35), (0.53, 0.55), (0.72, 0.74), (0.9, 0.92)]
    for z0, z1 in rails:
        p.append(box_z("Rail", (-0.48, 0.48), (-0.25, 0.23), (z0, z1), W, bev=0.003))
    root = join(p, "ChestOfDrawers")
    for i, (z0, z1) in enumerate(zones):
        zc = (z0 + z1) / 2
        d = [box_z("Front", (-0.476, 0.476), (-0.272, -0.25), (z0 + 0.003, z1 - 0.003), W, bev=0.007)]
        d.append(box_z("Cockbead", (-0.45, 0.45), (-0.276, -0.27), (z0 + 0.025, z1 - 0.025), W, bev=0.003))
        for sx in (-1, 1):
            d.append(box_z("DSide", (sx * 0.46 - 0.008, sx * 0.46 + 0.008), (-0.25, 0.2), (z0 + 0.01, z1 - 0.03),
                           "Wood_Light", bev=0.002))
            d += bail("Handle", sx * 0.25, -0.276, zc + 0.01)
        d.append(box_z("DBack", (-0.452, 0.452), (0.185, 0.2), (z0 + 0.01, z1 - 0.03), "Wood_Light", bev=0.002))
        d.append(box_z("DBottom", (-0.452, 0.452), (-0.25, 0.2), (z0 + 0.01, z0 + 0.02), "Wood_Light", bev=0.0))
        d.append(xf(extrude_poly("Keyhole", [(-0.01, -0.016), (0.01, -0.016), (0.012, 0.016), (-0.012, 0.016)],
                                 0.003, "Brass"), (0, -0.277, zc)))
        o = join(d, f"Drawer_{i}")
        set_origin(o, (0, -0.272, zc))
        child(o, root)
    return root


# ---------------------------------------------------------------- 7 TableLamp / Candelabra / Candle
@asset()
def TableLamp():
    p = [lathe("Base", [(0, 0), (0.09, 0), (0.095, 0.012), (0.08, 0.02), (0.06, 0.03), (0.075, 0.08),
                        (0.09, 0.14), (0.085, 0.2), (0.05, 0.26), (0.025, 0.29), (0.03, 0.3), (0, 0.3)],
               "Ceramic", 20)]
    p.append(torus("FootRing", 0.092, 0.007, "Brass", (0, 0, 0.007), seg=20, rseg=5))
    p.append(lathe("Collar", [(0, 0.28), (0.032, 0.28), (0.036, 0.3), (0.02, 0.32), (0, 0.32)], "Brass", 14))
    p.append(cyl("Stem", 0.008, 0.1, "Brass", (0, 0, 0.32), seg=8))
    p.append(lathe("Socket", [(0, 0.4), (0.014, 0.4), (0.019, 0.415), (0.018, 0.45), (0, 0.45)], "Brass", 12))
    p.append(lathe("Bulb", [(0, 0.445), (0.012, 0.45), (0.025, 0.475), (0.028, 0.495), (0.02, 0.515),
                            (0, 0.52)], "Glass", 12))
    p.append(set_mat(tube("Harp", [(-0.02, 0, 0.42), (-0.075, 0, 0.5), (-0.06, 0, 0.58), (0, 0, 0.6),
                                   (0.06, 0, 0.58), (0.075, 0, 0.5), (0.02, 0, 0.42)], 0.003, "Brass", res=5,
                          bevres=1), "Brass"))
    p.append(lathe("HarpFinial", [(0, 0.6), (0.01, 0.6), (0.012, 0.615), (0.004, 0.63), (0, 0.632)], "Brass", 8))
    p.append(lathe("Shade", [(0.215, 0.36), (0.19, 0.42), (0.15, 0.52), (0.115, 0.6), (0.111, 0.6),
                             (0.146, 0.52), (0.186, 0.42), (0.211, 0.36)], "Fabric_Linen", 24, closed=True))
    p.append(torus("ShadeTrim", 0.214, 0.005, "Fabric_Red", (0, 0, 0.362), seg=24, rseg=4))
    for i in range(24):
        a = 2 * math.pi * i / 24
        t = lathe("Tassel", [(0, -0.04), (0.007, -0.03), (0.003, -0.01), (0, 0)], "Fabric_Red", 5)
        p.append(xf(t, (0.214 * math.cos(a), 0.214 * math.sin(a), 0.36)))
    root = join(p, "TableLamp")
    empty("LightAnchor", (0, 0, 0.485), root)
    return root


@asset(view=(0.9, -1.6, 0.6))
def Candelabra():
    p = [lathe("Stand", [(0, 0), (0.08, 0), (0.085, 0.008), (0.07, 0.018), (0.045, 0.03), (0.04, 0.045),
                         (0.02, 0.06), (0.014, 0.1), (0.024, 0.13), (0.012, 0.16), (0.012, 0.22), (0.024, 0.24),
                         (0.026, 0.26), (0.014, 0.27), (0.01, 0.3), (0.016, 0.31), (0, 0.32)], "Brass", 16)]
    anchors = []
    for i in range(4):
        a = 45 + 90 * i
        arm = set_mat(tube("Arm", [(0.01, 0, 0.25), (0.07, 0, 0.22), (0.12, 0, 0.25), (0.13, 0, 0.3)], 0.006,
                           "Brass", res=6, bevres=1), "Brass")
        cup, cz = bobeche("Cup", 0.33, 0.032)
        xf(cup, (0.13, 0, 0))
        cp, wick = candle_stick("Candle", cz, rng.uniform(0.11, 0.16), 0.01)
        for c in cp:
            xf(c, (0.13, 0, 0))
        for g in [arm, cup] + cp:
            xf(g, rot=(0, 0, a))
        p += [arm, cup] + cp
        anchors.append((0.13 * math.cos(math.radians(a)), 0.13 * math.sin(math.radians(a)), wick))
    cup, cz = bobeche("Cup", 0.345, 0.035)
    cp, wick = candle_stick("Candle", cz, 0.17, 0.011)
    p += [cup] + cp
    anchors.insert(0, (0, 0, wick))
    root = join(p, "Candelabra")
    for i, an in enumerate(anchors):
        empty(f"FlameAnchor_{i}", an, root)
    return root


@asset(view=(0.9, -1.4, 1.0))
def Candle():
    p = [lathe("Dish", [(0, 0), (0.058, 0), (0.064, 0.006), (0.062, 0.014), (0.056, 0.01), (0.02, 0.007),
                        (0.017, 0.012), (0.016, 0.034), (0.021, 0.04), (0.014, 0.042), (0, 0.042)], "Brass", 12)]
    p.append(torus("Loop", 0.017, 0.004, "Brass", (0.072, 0, 0.022), (90, 0, 0), seg=8, rseg=4))
    p.append(xf(extrude_poly("Thumb", [(0.06, -0.008), (0.1, -0.011), (0.106, 0.0), (0.1, 0.011), (0.06, 0.008)], 0.003, "Brass",
                             plane='XY'), (0, 0, 0.042)))
    cp, wick = candle_stick("Candle", 0.042, 0.11, 0.011, drips=2)
    p += cp
    root = join(p, "Candle")
    empty("FlameAnchor", (0, 0, wick), root)
    return root


# ---------------------------------------------------------------- 8 Bookshelf / seating / table / piano
COVERS = ["Leather_Brown", "Leather_Brown", "Leather_Green", "Fabric_Red", "Wood_Light", "Wood_Dark"]


def book_parts(x0, z0, w, h, d, y_front, cover, lean=0.0):
    """Upright book standing at x0 (left edge), spine facing -Y. Cover box + page block peeking out the top."""
    b = box_z("Book", (0, w), (0, d), (0, h), cover, bev=0)
    pg = box_z("Pages", (0.003, w - 0.003), (0.004, d + 0.002), (h - 0.004, h + 0.0005), "Paper", bev=0)
    for o in (b, pg):
        xf(o, rot=(0, lean, 0))
        xf(o, (x0, y_front, z0))
    return [b, pg]


@asset()
def Bookshelf():
    W = "Wood_Dark"
    w, d, h = 1.0, 0.35, 2.0
    p = [box_z("Plinth", (-0.52, 0.52), (-0.19, 0.175), (0, 0.1), W, bev=0.01)]
    p.append(box_z("Side", (-0.5, -0.47), (-0.175, 0.175), (0.1, 1.92), W, bev=0.004))
    p.append(box_z("Side", (0.47, 0.5), (-0.175, 0.175), (0.1, 1.92), W, bev=0.004))
    p.append(box_z("Back", (-0.47, 0.47), (0.155, 0.175), (0.1, 1.92), W, bev=0.002))
    p.append(box_z("Cornice", (-0.53, 0.53), (-0.2, 0.18), (1.92, 1.96), W, bev=0.01))
    p.append(box_z("CorniceTop", (-0.545, 0.545), (-0.215, 0.185), (1.96, 2.0), W, bev=0.012))
    levels = [0.1, 0.46, 0.82, 1.18, 1.54]
    for z in levels:
        p.append(box_z("Shelf", (-0.47, 0.47), (-0.175, 0.155), (z, z + 0.025), W, bev=0.003))
        p.append(box_z("ShelfLip", (-0.47, 0.47), (-0.18, -0.165), (z - 0.01, z + 0.03), W, bev=0.004))
    root_parts = p
    for li, z in enumerate(levels):
        z0 = z + 0.025
        gap = 0.33 if li < 4 else 1.92 - z0
        x = -0.465
        end = 0.465
        stack_at = rng.choice([None, 0.25, -0.2])
        while x < end - 0.02:
            if stack_at is not None and abs(x - stack_at) < 0.02:   # horizontal stack
                sw = rng.uniform(0.2, 0.26)
                zz = z0
                for k in range(rng.randint(2, 4)):
                    t = rng.uniform(0.025, 0.045)
                    bk = box_z("Book", (0, sw - k * 0.01), (0, rng.uniform(0.16, 0.22)), (0, t),
                               rng.choice(COVERS), bev=0)
                    root_parts.append(xf(bk, (x + 0.005, -0.15, zz), (0, 0, rng.uniform(-4, 4))))
                    zz += t
                x += sw + 0.01
                stack_at = None
                continue
            if rng.random() < 0.06:          # gap
                x += rng.uniform(0.04, 0.1)
                continue
            bw = rng.uniform(0.022, 0.06)
            bh = min(rng.uniform(0.19, 0.31), gap - 0.02)
            bd = rng.uniform(0.16, 0.24)
            if x + bw > end:
                break
            if rng.random() < 0.08 and x + bh * 0.5 < end:   # leaning book
                lean = rng.uniform(12, 25)
                root_parts += book_parts(x + math.sin(math.radians(lean)) * bh, z0, bw, bh, bd, -0.16,
                                         rng.choice(COVERS), lean=-lean)
                x += bw + math.sin(math.radians(lean)) * bh + 0.01
                continue
            root_parts += book_parts(x, z0, bw, bh, bd, -0.16 + rng.uniform(0, 0.025), rng.choice(COVERS))
            x += bw + rng.uniform(0.0, 0.004)
    return join(root_parts, "Bookshelf")


def turned_leg(name, h, r, m="Wood_Dark", seg=10, loc=(0, 0, 0)):
    k = h
    prof = [(0, 0), (r * 0.7, 0), (r * 0.8, 0.02 * k), (r * 0.6, 0.06 * k), (r * 0.75, 0.2 * k), (r, 0.3 * k),
            (r * 0.55, 0.45 * k), (r * 0.6, 0.7 * k), (r * 0.9, 0.78 * k), (r * 0.65, 0.84 * k), (r, 0.88 * k),
            (r, k), (0, k)]
    return lathe(name, prof, m, seg, loc=loc)


@asset()
def Armchair():
    F = "Fabric_Red"
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(lathe("Leg", [(0, 0), (0.02, 0), (0.024, 0.02), (0.018, 0.06), (0.03, 0.12), (0.032, 0.15),
                                   (0, 0.15)], "Wood_Dark", 10, loc=(sx * 0.33, sy * 0.3, 0)))
    p.append(rounded_box("Base", (0.8, 0.76, 0.25), 0.04, F, (0, 0.0, 0.27), cuts=4, jit=0.003))
    p.append(rounded_box("Cushion", (0.56, 0.62, 0.13), 0.05, F, (0, -0.06, 0.44), cuts=5, bulge=0.025,
                         jit=0.003))
    back = rounded_box("Back", (0.62, 0.18, 0.78), 0.06, F, cuts=5, bulge=0.02, jit=0.003)
    p.append(xf(back, (0, 0.3, 0.76), (-10, 0, 0)))
    for sx in (-1, 1):
        p.append(rounded_box("Arm", (0.11, 0.66, 0.26), 0.03, F, (sx * 0.345, -0.03, 0.52), cuts=4, jit=0.002))
        roll = lathe("ArmRoll", [(0, -0.33), (0.06, -0.33), (0.07, -0.31), (0.072, 0.3), (0.06, 0.33), (0, 0.33)],
                     F, 12)
        p.append(xf(roll, (sx * 0.345, -0.03, 0.66), (90, 0, 0), (1, 1, 1)))
        p.append(xf(cyl("ArmFace", 0.05, 0.01, "Wood_Dark", seg=12), (sx * 0.345, -0.364, 0.66), (90, 0, 0)))
        wing = rounded_box("Wing", (0.09, 0.32, 0.5), 0.04, F, cuts=4, bulge=0.01, jit=0.002)
        p.append(xf(wing, (sx * 0.36, 0.16, 0.92), (0, sx * 6, sx * 18)))
    for sx in (-1, 1):  # brass nail-head trim along the front of the base
        for i in range(9):
            p.append(lathe("Nail", [(0, 0), (0.007, 0), (0.005, 0.005), (0, 0.006)], "Brass", 6,
                           loc=(sx * (0.06 + i * 0.04), -0.381, 0.18), rot=(90, 0, 0)))
    return join(p, "Armchair")


@asset()
def DiningChair():
    W = "Wood_Dark"
    p = []
    for sx in (-1, 1):
        p.append(turned_leg("FrontLeg", 0.43, 0.022, loc=(sx * 0.2, -0.2, 0)))
        post = box_z("BackPost", (-0.017, 0.017), (-0.02, 0.02), (0, 1.0), W, bev=0.006)
        jitter(post, 0.001)
        p.append(xf(post, (sx * 0.19, 0.2, 0), (-7, 0, 0)))
        p.append(lathe("Finial", [(0, 0), (0.02, 0), (0.022, 0.015), (0.012, 0.025), (0.016, 0.04), (0, 0.06)],
                       W, 8, loc=(sx * 0.19, 0.2 + math.sin(math.radians(7)) * 0.99, 0.99)))
        p.append(box_z("SideApron", (sx * 0.2 - 0.012, sx * 0.2 + 0.012), (-0.2, 0.2), (0.33, 0.42), W, bev=0.004))
        p.append(box_z("Stretcher", (sx * 0.2 - 0.01, sx * 0.2 + 0.01), (-0.2, 0.2), (0.12, 0.14), W, bev=0.004))
    p.append(box_z("FrontApron", (-0.2, 0.2), (-0.212, -0.188), (0.33, 0.42), W, bev=0.004))
    p.append(box_z("BackApron", (-0.19, 0.19), (0.19, 0.21), (0.33, 0.42), W, bev=0.004))
    p.append(cyl("FrontStretcher", 0.01, 0.4, W, (-0.2, -0.15, 0.17), (0, 90, 0), seg=8))
    p.append(rounded_box("Seat", (0.46, 0.45, 0.06), 0.025, "Fabric_Red", (0, 0.0, 0.445), cuts=4, bulge=0.012,
                         jit=0.002))
    # back: curved top rail + vase splat, tilted with the posts
    rail = [(-0.19, 0.0), (0.19, 0.0), (0.2, 0.06), (0.1, 0.085), (0.0, 0.09), (-0.1, 0.085), (-0.2, 0.06)]
    splat = [(-0.03, 0), (0.03, 0), (0.05, 0.08), (0.075, 0.2), (0.06, 0.3), (0.035, 0.36), (0.05, 0.4),
             (-0.05, 0.4), (-0.035, 0.36), (-0.06, 0.3), (-0.075, 0.2), (-0.05, 0.08)]
    back = [xf(extrude_poly("TopRail", rail, 0.025, W, bev=0.004), (0, 0, 0.88)),
            xf(extrude_poly("Splat", splat, 0.015, W, bev=0.003), (0, 0, 0.48)),
            box_z("ShoeRail", (-0.19, 0.19), (-0.015, 0.015), (0.44, 0.48), W, bev=0.004)]
    for b in back:
        xf(b, rot=(-7, 0, 0))
        xf(b, (0, 0.2, 0))
    p += back
    return join(p, "DiningChair")


@asset(view=(1.2, -1.5, 1.0))
def LongDiningTable():
    W = "Wood_Dark"
    L, D, H = 3.0, 1.0, 0.76
    top = sweep_x("Top", [(-D / 2, H - 0.05), (D / 2, H - 0.05), (D / 2 + 0.01, H - 0.035), (D / 2, H),
                          (-D / 2, H), (-D / 2 - 0.01, H - 0.035)], -L / 2, L / 2, W)
    p = [jitter(top, 0.002)]
    p.append(box_z("TopUnder", (-L / 2 + 0.02, L / 2 - 0.02), (-D / 2 + 0.02, D / 2 - 0.02), (H - 0.07, H - 0.05), W,
                   bev=0.008))
    p.append(box_z("Apron", (-L / 2 + 0.1, L / 2 - 0.1), (-D / 2 + 0.08, -D / 2 + 0.1), (H - 0.17, H - 0.07), W,
                   bev=0.004))
    p.append(box_z("Apron", (-L / 2 + 0.1, L / 2 - 0.1), (D / 2 - 0.1, D / 2 - 0.08), (H - 0.17, H - 0.07), W,
                   bev=0.004))
    for sx in (-1, 1):
        p.append(box_z("Apron", (sx * (L / 2 - 0.1) - 0.01, sx * (L / 2 - 0.1) + 0.01), (-D / 2 + 0.08, D / 2 - 0.08),
                       (H - 0.17, H - 0.07), W, bev=0.004))
    for x in (-L / 2 + 0.1, 0, L / 2 - 0.1):
        for sy in (-1, 1):
            leg = [(0, 0), (0.03, 0), (0.035, 0.03), (0.025, 0.06), (0.03, 0.1), (0.055, 0.2), (0.06, 0.26),
                   (0.045, 0.34), (0.03, 0.4), (0.028, 0.46), (0.04, 0.5), (0.03, 0.53), (0.04, 0.56),
                   (0.04, H - 0.07), (0, H - 0.07)]
            p.append(lathe("Leg", leg, W, 12, loc=(x, sy * (D / 2 - 0.09), 0)))
    p.append(box_z("Stretcher", (-L / 2 + 0.1, L / 2 - 0.1), (-0.02, 0.02), (0.12, 0.16), W, bev=0.006))
    for x in (-L / 2 + 0.1, 0, L / 2 - 0.1):
        p.append(box_z("CrossStretcher", (x - 0.02, x + 0.02), (-D / 2 + 0.09, D / 2 - 0.09), (0.12, 0.16), W,
                       bev=0.006))
    return join(p, "LongDiningTable")


@asset(view=(0.8, -1.6, 0.7))
def Piano():
    W = "Wood_Dark"
    p = []
    p.append(box_z("UpperCase", (-0.72, 0.72), (-0.02, 0.3), (0.05, 1.22), W, bev=0.006))
    p.append(box_z("Lid", (-0.74, 0.74), (-0.04, 0.31), (1.22, 1.26), W, bev=0.01))
    p.append(box_z("Plinth", (-0.73, 0.73), (-0.03, 0.31), (0.0, 0.06), W, bev=0.008))
    for i, (x0, x1) in enumerate(((-0.6, -0.22), (-0.18, 0.18), (0.22, 0.6))):
        p.append(box_z("UpperPanel", (x0, x1), (-0.032, -0.02), (0.88, 1.16), W, bev=0.012))
    for x0, x1 in ((-0.6, -0.03), (0.03, 0.6)):
        p.append(box_z("KneePanel", (x0, x1), (-0.032, -0.02), (0.14, 0.55), W, bev=0.014))
    for sx in (-1, 1):
        x = sx * 0.69
        cheek = extrude_poly("Cheek", [(-0.3, 0.0), (0.0, 0.0), (0.0, 0.3), (-0.3, 0.3), (-0.33, 0.26),
                                       (-0.33, 0.04)], 0.05, W, plane='XZ', bev=0.008)
        p.append(xf(cheek, rot=(0, 0, 90)))
        xf(cheek, (x, 0.0, 0.6))
        p.append(lathe("Pillar", [(0, 0), (0.03, 0), (0.035, 0.03), (0.022, 0.06), (0.03, 0.2), (0.022, 0.4),
                                  (0.026, 0.5), (0.03, 0.55), (0, 0.55)], W, 12, loc=(sx * 0.66, -0.27, 0.06)))
        p.append(box_z("ToeBlock", (sx * 0.66 - 0.04, sx * 0.66 + 0.04), (-0.32, -0.02), (0.0, 0.06), W, bev=0.008))
        # swing-arm candle sconces either side of the music desk
        sc = [xf(set_mat(tube("SconceArm", [(0, 0.0, 0.0), (0, -0.06, 0.02), (0, -0.12, 0.06)], 0.005, "Brass",
                                   res=4, bevres=1), "Brass"), (sx * 0.62, -0.02, 0.98))]
        cup, cz = bobeche("SconceCup", 1.08, 0.03)
        sc.append(xf(cup, (sx * 0.62, -0.14, 0)))
        cp, _ = candle_stick("Candle", cz, rng.uniform(0.04, 0.09), 0.01, drips=2)
        sc += [xf(c, (sx * 0.62, -0.14, 0)) for c in cp]
        p += sc
    p.append(box_z("Keybed", (-0.67, 0.67), (-0.31, -0.02), (0.6, 0.66), W, bev=0.006))
    p.append(box_z("KeySlip", (-0.67, 0.67), (-0.33, -0.31), (0.6, 0.69), W, bev=0.004))
    p.append(box_z("Fallboard", (-0.64, 0.64), (-0.13, -0.1), (0.68, 0.8), W, bev=0.006))
    p.append(xf(box_z("MusicDesk", (-0.45, 0.45), (-0.01, 0.01), (0.0, 0.22), W, bev=0.005), (0, -0.05, 0.86)))
    p.append(xf(box_z("DeskLedge", (-0.45, 0.45), (-0.07, 0.0), (0.0, 0.02), W, bev=0.004), (0, -0.04, 0.85)))
    # keyboard: 52 white + 36 black, a few sunk/missing keys
    n = 52
    kw = 1.24 / n
    for i in range(n):
        if rng.random() < 0.03:
            continue
        x = -0.62 + i * kw
        dz = -0.006 if rng.random() < 0.08 else 0.0
        p.append(box_z("WhiteKey", (x + 0.0006, x + kw - 0.0006), (-0.3, -0.135), (0.66 + dz, 0.682 + dz), "Ceramic",
                       bev=0))
    names = "ABCDEFG"
    for i in range(n - 1):
        if names[i % 7] in "ACDFG":
            x = -0.62 + (i + 1) * kw
            p.append(box_z("BlackKey", (x - 0.0065, x + 0.0065), (-0.235, -0.135), (0.682, 0.695), "Iron",
                           bev=0))
    for k in (-1, 1):
        p.append(xf(set_mat(tube("Pedal", [(0, 0.0, 0), (0, -0.07, -0.005), (0, -0.11, 0.01)], 0.008, "Brass",
                                   res=4, bevres=1), "Brass"), (k * 0.08, -0.03, 0.09)))
    root = join(p, "Piano")
    return root


# ---------------------------------------------------------------- 9 Doors, frames, windows
def make_door(name, w, h, t=0.045):
    """Victorian 4-panel door. Origin on the hinge edge at the floor (door spans +X), mid-thickness."""
    W = "Wood_Dark"
    st = 0.12
    lock0 = h * 0.44
    p = [box_z("Stile", (0, st), (-t / 2, t / 2), (0, h), W, bev=0.004, jit=0.001),
         box_z("Stile", (w - st, w), (-t / 2, t / 2), (0, h), W, bev=0.004, jit=0.001),
         box_z("RailBottom", (st, w - st), (-t / 2, t / 2), (0, 0.22), W, bev=0.004),
         box_z("RailLock", (st, w - st), (-t / 2, t / 2), (lock0, lock0 + 0.17), W, bev=0.004),
         box_z("RailTop", (st, w - st), (-t / 2, t / 2), (h - 0.13, h), W, bev=0.004),
         box_z("Muntin", (w / 2 - 0.05, w / 2 + 0.05), (-t / 2, t / 2), (0.22, h - 0.13), W, bev=0.004)]
    for z0, z1 in ((0.22, lock0), (lock0 + 0.17, h - 0.13)):
        for x0, x1 in ((st, w / 2 - 0.05), (w / 2 + 0.05, w - st)):
            p.append(box_z("Panel", (x0 - 0.004, x1 + 0.004), (-t * 0.32, t * 0.32), (z0 - 0.004, z1 + 0.004), W,
                           bev=0.014))
            for sy in (-1, 1):   # bolection moulding around each panel
                for (a0, a1), (b0, b1) in ((((x0, x1), (z0, z0 + 0.015))), (((x0, x1), (z1 - 0.015, z1))),
                                           (((x0, x0 + 0.015), (z0, z1))), (((x1 - 0.015, x1), (z0, z1)))):
                    p.append(box_z("Mould", (a0, a1), (sy * t / 2 - 0.004, sy * t / 2 + 0.004), (b0, b1), W,
                                   bev=0.004))
    kx = w - 0.065
    for sy in (-1, 1):
        p.append(knob("Knob", (kx, sy * t / 2, 1.0)) if sy < 0 else
                 lathe("Knob", [(0, 0), (0.01, 0), (0.006, 0.01), (0.006, 0.018), (0.016, 0.026),
                                (0.0144, 0.036), (0, 0.038)], "Brass", 10, loc=(kx, t / 2, 1.0), rot=(-90, 0, 0)))
        plate = extrude_poly("Backplate", [(-0.02, -0.09), (0.02, -0.09), (0.022, 0.06), (0.0, 0.08),
                                           (-0.022, 0.06)], 0.003, "Brass")
        p.append(xf(plate, (kx, sy * (t / 2 + 0.0015), 1.0)))
        p.append(xf(extrude_poly("Keyhole", [(-0.003, -0.012), (0.003, -0.012), (0.0015, 0.0), (0.004, 0.006),
                                             (0.0, 0.01), (-0.004, 0.006), (-0.0015, 0.0)], 0.002, "Iron"),
                    (kx, sy * (t / 2 + 0.003), 0.94)))
    for z in (0.25, h * 0.5, h - 0.3):
        p.append(cyl("HingeKnuckle", 0.008, 0.1, "Brass", (0, -t / 2 - 0.002, z), seg=8))
    o = join(p, name)
    return o


@asset(view=(1.0, -1.6, 0.5))
def PanelDoor():
    return make_door("PanelDoor", 0.9, 2.1)


@asset(view=(1.0, -1.6, 0.5))
def PanelDoor_Tall():
    return make_door("PanelDoor_Tall", 0.7, 2.29)


def casing(sy, wt, x_half, z_top, plinths=True):
    """Architrave on one wall face (sy=-1 front/-Y, +1 back) around an opening of half-width x_half."""
    W = "Wood_Dark"
    y0 = sy * wt / 2
    out = lambda d: tuple(sorted((y0, y0 + sy * d)))  # noqa: E731
    p = []
    zb = 0.25 if plinths else 0.0
    for sx in (-1, 1):
        a, b = sorted((sx * (x_half - 0.005), sx * (x_half + 0.1)))
        p.append(box_z("Casing", (a, b), out(0.022), (zb, z_top), W, bev=0.005))
        a2, b2 = sorted((sx * (x_half + 0.015), sx * (x_half + 0.035)))
        p.append(box_z("Bead", (a2, b2), out(0.03), (zb, z_top), W, bev=0.006))
        if plinths:
            p.append(box_z("PlinthBlock", sorted((sx * (x_half - 0.01), sx * (x_half + 0.11))), out(0.032), (0, 0.25),
                           W, bev=0.006))
        c0, c1 = sorted((sx * (x_half - 0.01), sx * (x_half + 0.11)))
        p.append(box_z("CornerBlock", (c0, c1), out(0.028), (z_top, z_top + 0.12), W, bev=0.006))
        p.append(lathe("Rosette", [(0, 0), (0.045, 0), (0.042, 0.006), (0.03, 0.008), (0.022, 0.014), (0.01, 0.016),
                                   (0, 0.018)], W, 14, loc=((c0 + c1) / 2, y0 + sy * 0.028, z_top + 0.06),
                       rot=(-90 * sy, 0, 0)))
    p.append(box_z("HeadCasing", (-x_half + 0.01, x_half - 0.01), out(0.022), (z_top, z_top + 0.12), W, bev=0.005))
    p.append(box_z("HeadBead", (-x_half + 0.01, x_half - 0.01), out(0.03), (z_top + 0.04, z_top + 0.08), W,
                   bev=0.006))
    p.append(box_z("Cap", (-x_half - 0.14, x_half + 0.14), out(0.045), (z_top + 0.12, z_top + 0.155), W, bev=0.008))
    return p


@asset(view=(0.9, -1.6, 0.4))
def DoorFrame():
    W = "Wood_Dark"
    ow, oh, wt = 1.4, 2.3, 0.2
    x = ow / 2
    p = [box_z("Jamb", (-x, -x + 0.025), (-wt / 2, wt / 2), (0, oh), W, bev=0.003),
         box_z("Jamb", (x - 0.025, x), (-wt / 2, wt / 2), (0, oh), W, bev=0.003),
         box_z("HeadJamb", (-x, x), (-wt / 2, wt / 2), (oh - 0.025, oh), W, bev=0.003)]
    for sy in (-1, 1):
        p += casing(sy, wt, x, oh)
    return join(p, "DoorFrame")


@asset(view=(0.9, -1.6, 0.4))
def WindowFrame():
    W = "Wood_Dark"
    ow, oh, wt = 1.4, 1.2, 0.2
    x = ow / 2
    p = [box_z("Jamb", (-x, -x + 0.025), (-wt / 2, wt / 2), (0, oh), W, bev=0.003),
         box_z("Jamb", (x - 0.025, x), (-wt / 2, wt / 2), (0, oh), W, bev=0.003),
         box_z("HeadJamb", (-x, x), (-wt / 2, wt / 2), (oh - 0.025, oh), W, bev=0.003),
         box_z("SillBoard", (-x, x), (-wt / 2, wt / 2), (0, 0.02), W, bev=0.003),
         box_z("Stool", (-x - 0.13, x + 0.13), (-wt / 2 - 0.06, 0.0), (0.0, 0.03), W, bev=0.008),
         box_z("Apron", (-x - 0.08, x + 0.08), (-wt / 2 - 0.022, -wt / 2), (-0.14, 0.0), W, bev=0.005),
         box_z("ParticBead", (-x + 0.025, x - 0.025), (0.005, 0.02), (0.02, oh - 0.025), W, bev=0.003),
         box_z("StoneSill", (-x - 0.1, x + 0.1), (wt / 2 - 0.03, wt / 2 + 0.07), (-0.07, 0.0), "Stone", bev=0.01,
               jit=0.003)]
    for sx in (-1, 1):  # side + top of the exterior stop bead
        a, b = sorted((sx * (x - 0.025), sx * (x - 0.045)))
        p.append(box_z("Stop", (a, b), (wt / 2 - 0.04, wt / 2 - 0.02), (0.02, oh - 0.025), W, bev=0.003))
    p += casing(-1, wt, x, oh, plinths=False)
    # casing legs stop on the stool
    for o in p:
        if o.name.startswith(("Casing", "Bead")):
            for v in o.data.vertices:
                v.co.z = max(v.co.z, 0.03)
    root = join(p, "WindowFrame")

    def sash(name, z0, z1, y, meet_top):
        sw = ow - 0.05
        s = []
        fw = 0.05
        s.append(box_z("SashStile", (-sw / 2, -sw / 2 + fw), (y - 0.02, y + 0.02), (z0, z1), W, bev=0.004))
        s.append(box_z("SashStile", (sw / 2 - fw, sw / 2), (y - 0.02, y + 0.02), (z0, z1), W, bev=0.004))
        bot = 0.035 if not meet_top else fw * 1.4
        top = 0.035 if meet_top else fw
        s.append(box_z("SashRail", (-sw / 2 + fw, sw / 2 - fw), (y - 0.02, y + 0.02), (z0, z0 + bot), W, bev=0.004))
        s.append(box_z("SashRail", (-sw / 2 + fw, sw / 2 - fw), (y - 0.02, y + 0.02), (z1 - top, z1), W, bev=0.004))
        gz0, gz1 = z0 + bot, z1 - top
        s.append(box_z("GlazingBar", (-0.012, 0.012), (y - 0.014, y + 0.014), (gz0, gz1), W, bev=0.004))
        gx = sw / 2 - fw
        for x0, x1 in ((-gx, -0.012), (0.012, gx)):
            if rng.random() < 0.35:  # broken pane: a few jagged shards left in the frame
                for corner in ((x0, gz0), (x1, gz1)):
                    cx, cz = corner
                    dx = (x1 - x0) * rng.uniform(0.3, 0.6) * (1 if cx == x0 else -1)
                    dz = (gz1 - gz0) * rng.uniform(0.3, 0.6) * (1 if cz == gz0 else -1)
                    shard = extrude_poly("Shard", [(cx, cz), (cx + dx, cz), (cx + dx * 0.4, cz + dz * 0.35),
                                                   (cx + dx * 0.15, cz + dz * 0.5), (cx, cz + dz)], 0.004, "Glass")
                    s.append(xf(shard, (0, y, 0)))
            else:
                s.append(box_z("Pane", (x0, x1), (y - 0.002, y + 0.002), (gz0, gz1), "Glass", bev=0))
        o = join(s, name)
        set_origin(o, (0, y, z0))
        return child(o, root)

    sash("Sash_Lower", 0.02, 0.62, -0.035, True)
    sash("Sash_Upper", 0.58, 1.175, 0.01, False)
    return root


# ---------------------------------------------------------------- 10 Ladder, banisters, newel
@asset(view=(1.0, -1.6, 0.3))
def Ladder():
    L = 3.0
    p = []
    for sx in (-1, 1):
        r = box_z("Rail", (-0.016, 0.016), (-0.035, 0.035), (0, L), "Wood_Light", bev=0.005, jit=0.0015)
        for v in r.data.vertices:
            v.co.x += sx * (0.22 - 0.04 * v.co.z / L)
        p.append(r)
        p.append(xf(cyl("Shoe", 0.02, 0.03, "Iron", seg=8, bev=0.003), (sx * 0.22, 0, 0)))
    for i in range(9):
        z = 0.3 + i * 0.3
        half = 0.22 - 0.04 * z / L
        rung = lathe("Rung", [(0, -half), (0.015, -half), (0.018, 0), (0.015, half), (0, half)], "Wood_Light", 10)
        p.append(jitter(xf(rung, (0, 0, z), (0, 90, 0)), 0.001))
        if i % 4 == 0:
            p.append(xf(cyl("TieRod", 0.004, 2 * half + 0.04, "Iron", seg=6), (-half - 0.02, 0, z - 0.04), (0, 90, 0)))
    return join(p, "Ladder")


def make_banister(name, run, rise, x0):
    W = "Wood_Dark"
    k = rise / run
    p = []
    rail = sweep_x("Handrail", [(-0.03, 0.0), (0.03, 0.0), (0.036, 0.02), (0.03, 0.045), (0.015, 0.06),
                                (-0.015, 0.06), (-0.03, 0.045), (-0.036, 0.02)], x0, x0 + run, W)
    base = sweep_x("BaseRail", [(-0.035, 0.0), (0.035, 0.0), (0.035, 0.05), (0.025, 0.07), (-0.025, 0.07),
                                (-0.035, 0.05)], x0, x0 + run, W)
    for o, dz in ((rail, 0.86), (base, 0.0)):
        for v in o.data.vertices:
            v.co.z += dz + k * (v.co.x - x0)
        p.append(o)
    bal = [(0, 0), (0.02, 0), (0.02, 0.08), (0.015, 0.1), (0.011, 0.14), (0.022, 0.25), (0.024, 0.3), (0.013, 0.4),
           (0.01, 0.6), (0.015, 0.66), (0.015, 0.71), (0.02, 0.72), (0.02, 0.8), (0, 0.8)]
    n = int(run / 0.13)
    step = run / n
    for i in range(n):
        x = x0 + step * (i + 0.5)
        zb = 0.07 + k * x - k * x0
        if rng.random() < 0.06:   # a snapped baluster stub
            p.append(lathe("Baluster", [(z_r, z * 0.3) for z_r, z in bal[:6]] + [(0, 0.12)], W, 8, loc=(x, 0, zb)))
            continue
        h = 0.79
        p.append(lathe("Baluster", [(r, z * h / 0.8) for r, z in bal], W, 8, loc=(x, 0, zb)))
    return join(p, name)


@asset(view=(0.6, -1.6, 0.5))
def Banister():
    return make_banister("Banister", 2.0, 0.0, -1.0)


@asset(view=(0.6, -1.6, 0.5))
def Banister_Stair():
    return make_banister("Banister_Stair", 2.0, 2.0 * 0.2 / 0.28, 0.0)


@asset()
def NewelPost():
    W = "Wood_Dark"
    p = [box_z("Plinth", (-0.09, 0.09), (-0.09, 0.09), (0, 0.15), W, bev=0.012),
         box_z("Shaft", (-0.07, 0.07), (-0.07, 0.07), (0.15, 1.0), W, bev=0.006),
         box_z("CapMould", (-0.085, 0.085), (-0.085, 0.085), (1.0, 1.04), W, bev=0.01),
         box_z("Cap", (-0.095, 0.095), (-0.095, 0.095), (1.04, 1.07), W, bev=0.01)]
    for a in range(4):
        pan = box_z("Panel", (-0.045, 0.045), (-0.078, -0.07), (0.25, 0.9), W, bev=0.006)
        p.append(xf(pan, rot=(0, 0, 90 * a)))
    p.append(lathe("Finial", [(0, 1.07), (0.05, 1.07), (0.05, 1.085), (0.03, 1.1), (0.035, 1.115), (0.06, 1.15),
                              (0.07, 1.18), (0.06, 1.215), (0.035, 1.24), (0.015, 1.255), (0, 1.26)], W, 16))
    return join(p, "NewelPost")


# ---------------------------------------------------------------- 11 Pickups (lie flat, origin base centre)
@asset(view=(0.4, -0.8, 1.4))
def Key():
    I = "Iron"
    p = [torus("Bow", 0.014, 0.0028, I, (-0.042, 0, 0.003), seg=14, rseg=5, scale=(1, 0.85, 1))]
    for x in (-0.027, -0.021):
        p.append(cyl("Collar", 0.0042, 0.003, I, (x, 0, 0.003), (0, 90, 0), seg=8))
    p.append(cyl("Shaft", 0.003, 0.08, I, (-0.028, 0, 0.003), (0, 90, 0), seg=8))
    bit = extrude_poly("Bit", [(0.03, 0.0), (0.05, 0.0), (0.05, -0.017), (0.046, -0.017), (0.046, -0.01),
                               (0.041, -0.01), (0.041, -0.017), (0.036, -0.017), (0.036, -0.012), (0.03, -0.012)],
                       0.0034, I, plane='XY')
    p.append(xf(bit, (0, 0, 0.003)))
    p.append(lathe("Tip", [(0, 0.05), (0.0035, 0.05), (0.003, 0.054), (0, 0.055)], I, 8, loc=(0, 0, 0.003),
                   rot=(0, 90, 0)))
    k = join(p, "Key")
    jitter(k, 0.0002)
    return k


def flatware_bend(o, x_head, x_tail, lift=0.15):
    for v in o.data.vertices:
        if v.co.x > x_head:
            v.co.z += (v.co.x - x_head) * lift
        if v.co.x < x_tail:
            v.co.z += (x_tail - v.co.x) * lift * 0.6
    zmin = min(v.co.z for v in o.data.vertices)
    for v in o.data.vertices:
        v.co.z -= zmin
    return o


def handle_outline(x_end, x_neck, hw_end, hw_neck):
    """Upper half of a fiddle-ish handle outline from neck back to the rounded end."""
    pts = []
    for i in range(9):
        t = i / 8
        x = x_neck + (x_end - x_neck) * t
        hw = hw_neck + (hw_end - hw_neck) * (t ** 1.6)
        pts.append((x, hw))
    for i in range(1, 5):
        a = math.pi / 2 * i / 5
        pts.append((x_end - math.sin(a) * hw_end * 0.6, math.cos(a) * hw_end))
    return pts


def mirror_outline(upper):
    """Close an outline given its upper half (ordered from right to left)."""
    return upper + [(x, -y) for x, y in reversed(upper) if abs(y) > 1e-9]


@asset(view=(0.3, -0.6, 1.5))
def Fork():
    tines = []
    tw, gap = 0.00375, 0.003
    y = -0.012
    for i in range(4):
        tines += [(0.06, y), (0.097, y + tw * 0.3), (0.097, y + tw * 0.7), (0.06, y + tw)] if i else \
                 [(0.097, y + tw * 0.3), (0.097, y + tw * 0.7), (0.06, y + tw)]
        y += tw + gap
    head = [(0.03, -0.005), (0.045, -0.01), (0.06, -0.012)] + tines + [(0.045, 0.01), (0.03, 0.005)]
    h = extrude_poly("ForkHead", head, 0.0025, "Silver", plane='XY')
    hd = extrude_poly("ForkHandle", mirror_outline(handle_outline(-0.1, 0.031, 0.011, 0.0045)), 0.0028, "Silver",
                      plane='XY')
    return flatware_bend(join([h, hd], "Fork"), 0.035, -0.06)


@asset(view=(0.3, -0.6, 1.5))
def Spoon():
    bowl = lathe("Bowl", [(0, 0), (0.012, 0.001), (0.019, 0.004), (0.022, 0.009), (0.0205, 0.0095),
                          (0.0175, 0.0052), (0.0115, 0.0027), (0, 0.0017)], "Silver", 16, closed=True,
                 scale=(1.45, 1, 1))
    xf(bowl, (0.06, 0, -0.004))
    hd = extrude_poly("SpoonHandle", mirror_outline(handle_outline(-0.1, 0.034, 0.011, 0.0045)), 0.0028, "Silver",
                      plane='XY')
    return flatware_bend(join([bowl, hd], "Spoon"), 0.04, -0.06, lift=0.1)


@asset(view=(0.3, -0.6, 1.5))
def Knife():
    blade = extrude_poly("Blade", [(0.0, -0.008), (0.09, -0.009), (0.106, -0.006), (0.114, 0.0), (0.11, 0.006),
                                   (0.0, 0.0085)], 0.0016, "Silver", plane='XY')
    xf(blade, (0, 0, 0.0055))
    bolster = cyl("Bolster", 0.0065, 0.012, "Silver", (-0.012, 0, 0.0055), (0, 90, 0), seg=10)
    handle = rounded_box("Handle", (0.09, 0.017, 0.011), 0.005, "Ceramic", (-0.057, 0, 0.0055), cuts=3)
    return flatware_bend(join([blade, bolster, handle], "Knife"), 2.0, -2.0)


@asset(view=(0.4, -0.8, 1.2))
def Plate():
    pl = lathe("Plate", [(0, 0.003), (0.055, 0.003), (0.058, 0.0), (0.064, 0.0), (0.068, 0.006), (0.118, 0.016),
                         (0.13, 0.021), (0.127, 0.024), (0.116, 0.02), (0.07, 0.0105), (0, 0.0095)], "Ceramic", 22)
    return pl


@asset(view=(0.6, -0.8, 1.0))
def Book():
    c = rng.choice(["Leather_Brown", "Leather_Green", "Fabric_Red"])
    p = [box_z("BoardBottom", (-0.075, 0.075), (-0.11, 0.11), (0, 0.004), c, bev=0.0015, seg=1),
         box_z("BoardTop", (-0.075, 0.075), (-0.11, 0.11), (0.034, 0.038), c, bev=0.0015, seg=1),
         box_z("Pages", (-0.07, 0.07), (-0.105, 0.105), (0.004, 0.034), "Paper", bev=0.001, seg=1),
         xf(cyl("Spine", 0.019, 0.22, c, seg=12), (-0.073, -0.11, 0.019), (-90, 0, 0), (1, 1, 1))]
    for v in p[-1].data.vertices:
        v.co.x = -0.073 + (v.co.x + 0.073) * 0.35
    for yb in (-0.06, 0.0, 0.06):
        p.append(box_z("Band", (-0.0805, -0.0775), (yb - 0.004, yb + 0.004), (0.006, 0.032), c, bev=0.001))
    return join(p, "Book")
