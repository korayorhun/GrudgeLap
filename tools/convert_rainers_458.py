"""Converts RainerS's "Ferrari 458 Italia" (BlendSwap #5151, CC BY 3.0: ferrari_studio.blend, made in Blender 2.62 for
Cycles) into the glTF binary that tools/car_models.json "458" draws: unreal/Import/Models/ferrari-458-italia-rainers.glb.

Run it headless, with the file's own scripts disabled (a downloaded .blend may carry Python that runs on load). The
.blend is only read; nothing is ever saved back to it:

  blender -b --factory-startup --disable-autoexec <ferrari_studio.blend> --python tools/convert_rainers_458.py -- \
      --out <ferrari-458-italia-rainers.glb> [--report <report.txt>] [--wheelbase 2.650] [--ring 0.58] [--badges]

What it makes, and why (each step's reasons are at its code in main()):
  - only the car: the file is a studio scene (a cyclorama, light planes, cameras) around a rig whose root, CarCtrl,
    holds the car; the rig's steering handle and a lit duplicate of the daytime lamps are left out too;
  - the rig reset: the studio pose has the car turned -193.06 degrees, scaled 1.7747 and its front wheels steered by
    WheelsRot; set straight, unturned and scaled so that 1 unit is 1 m at the 458's 2650 mm wheelbase (measured from
    the tyres' centres), origin on the ground under the middle of the wheelbase, nose at Blender -Y (the exporter's
    Y-up conversion makes that glTF +Z, and Blender's +X, the car's left, stays glTF +X);
  - every node at identity: constraints, then modifiers, then each object's world transform are baked into its
    vertices (in that order: the Mirror modifiers mirror about each object's own origin). tools/car_models.py reads
    boxes from the accessors, and a wheel rolled about its axle has a box 37 % too big; 24 objects sit under a
    negative scale, so their faces are turned back to face out once the scale is in the vertices;
  - duplicate faces removed (four grilles mirror over their own middle and list every centre face twice);
  - the windows and the tail lamp covers baked as single sheets (their Solidify off): the game draws glass and clear
    lenses alpha-blended from both faces, so a pane with a thickness counts twice;
  - each corner's 13 caliper parts joined into one, and each wiper's parts into one;
  - readable node names: tools/car_models.json selects by them. Never "Body" (tools/prepare_car_models.py looks its
    own group up by that name), none of the names the preparation gives its own nodes, no Godot import hint suffix;
  - about 0.54 M triangles (the file has 1.0 M at its viewport levels, 0.95 M in the parts kept): the body keeps its
    subdivision; the rims, the headlamp insides, the exhaust, the engine, the seals and a wiper arm lose a level, and
    the grilles, the calipers, the exhaust's mesh surround and the wiper blades are decimated;
  - every material rebuilt as one Principled BSDF (the glTF exporter reads nothing else; the Cycles 2.62 trees would
    export as glTF's white defaults), with the values of the v72 survey of the file's node trees; glass with
    transmission, so the lamp covers count as glass for the preparation and become the game's clear lenses;
  - no textures: the lamp faces, the side repeaters and the fog lamps were photographs of other lamps, the wing
    shields' texture carries a logo site's watermark, the rest are the maker's marks or are not needed by the game;
  - the maker's marks left out (the Ferrari shields, horses and lettering, the Pininfarina script), as the BRZ's
    Toyota badges are; --badges keeps their modelled shapes in plain colours, named Badge... (the entry's "badge"
    drop selector would still keep them out of the game);
  - the tail lamps' faces split by their UV radius into the red LED ring (TailLampLED: the entry's brake lamp, lit
    by the game) and the chrome concentric centre (TailLampChrome), as the real lamp is; the red lens tube behind
    each face a translucent red of its own, never paint.

Python 3.11 or later (Blender 5.2 bundles 3.13); no absolute paths, no third-party modules. Blender 5.2.3 gave the
same file, byte for byte, on every run.
"""
import argparse
import hashlib
import json
import math
import os
import struct
import sys

import bmesh
import bpy
from mathutils import Vector

# --- The parts -------------------------------------------------------------------------------------------------------

# The file this script was written for (as downloaded from BlendSwap on 2026-11-08): another file may need other tables.
SOURCE_SHA256 = "20169e8145eeb086cdfcd1408d2a7eaf7975ad46ea69853ccb026e460c3b5147"

# Inside the car's hierarchy but not drawn: the rig's control mesh (render-disabled), the steering handle's ball, and a
# copy of the daytime lamps in their lit material (Collection 3, the "Headlights" render layer) laid over the unlit
# strip in the same place.
LEFT_OUT = {"CarCtrl", "Icosphere", "Cube.004"}

# The maker's marks, left out unless --badges: the design owner's (Ferrari's and Pininfarina's) trademarks, which the
# author's CC BY grant cannot cover (THIRD_PARTY.md; the BRZ's Toyota badges are left out the same way). The front
# horse's stalk goes with the horse, or it would hang in the grille on its own.
MARKS = {
    "Plane.196": "BadgeWingShields",       # its texture carried a logo site's watermark
    "Plane.047": "BadgeBonnet",
    "Plane.051": "BadgeHorseFront", "Plane.052": "BadgeHorseFrontStalk",
    "Plane.195": "BadgeHorseRear",
    "Plane.045": "BadgeSideScript",        # 45,312 triangles at the viewport's level 2 (rendered by the author at 1)
    "Text": "BadgeTailLettering",
}
SIDED_MARKS = {"BadgeEngineLettering": ("Text.000", "Text.003")}   # on the engine's intake plenums

# Tyres and rolling parts, one object per corner. The corner is read from where each part is, not from this order;
# the rig's own names for the four tyres (Wheel_F.L ...) are checked against it.
CORNER_PARTS = {
    "Tyre": ("Plane.136", "Plane.011", "Plane.165", "Plane.190"),          # the tread band, Tire.000: the entry's tyres
    "TyreWall": ("Plane.097", "Plane.164", "Plane.177", "Plane.178"),      # the sidewalls, Tire.002
    "RimFace": ("Plane.096", "Plane.009", "Plane.166", "Plane.189"),       # the spokes and rim lip
    "RimBarrel": ("Circle.028", "Circle.000", "Circle.021", "Circle.041"),
    "RimHub": ("Circle.025", "Circle.017", "Circle.031", "Circle.038"),
    "RimHubFace": ("Circle.024", "Circle.018", "Circle.032", "Circle.037"),
    "RimCentre": ("Plane.095", "Plane.010", "Plane.167", "Plane.188"),
    "BrakeDisc": ("Circle.035", "Circle.016", "Circle.042", "Circle.047"),
    "WheelCap": ("Circle.027", "Circle.015", "Circle.029", "Circle.040"),  # not "..._Wheel": a Godot import hint
}
RIG_CORNERS = {"Wheel_F.L": "FL", "Wheel_F.R": "FR", "Wheel_R.L": "RL", "Wheel_R.R": "RR"}

# Parts in pairs, named for their side (left is +X with the nose at -Y, as glTF's +X is with the nose at +Z).
SIDED_PARTS = {
    "TailLampFace": ("Cylinder.006", "Cylinder.016"),
    "TailLampTube": ("Cylinder.008", "Cylinder.015"),
    "TailLampTubeEnd": ("Cylinder.014", "Cylinder.002"),
    "TailLampCover": ("Cylinder", "Cylinder.017"),
    "InteriorSeat": ("Plane.091", "Plane.092"),
    "InteriorHeadrest": ("Cube.042", "Cube.043"),
}

# Everything else by its object name. The model's own cabin is named Interior...: the entry drops it with one selector
# while the game borrows the McLaren 570S's, so no other part may have "interior" in its name (checked), nor "badge".
# Glass words (window, glass, windscreen) stay out of the names of parts that are not glass: the preparation skips
# glass-named parts when it looks for what is over the driver's head and where the dials go.
NAMES = {
    # Paint (MainBody.001, MainBody.000).
    "Plane.008": "Shell", "Cube.007": "MirrorHousings", "Plane.039": "ShellBehindCabin", "Plane.060": "ShellSideStrips",
    "Plane.041": "ShellLidEdge_1", "Plane.042": "ShellLidEdge_2",
    # Glass.
    "Plane.035": "Windscreen", "Plane.036": "SideWindows", "Plane.034": "QuarterWindows", "Plane.037": "EngineCoverGlass",
    "Cube.012": "WingMirrorGlass",
    # The interior mirror: its glass is the entry's mirror_glass (the game's live mirror goes there).
    "Plane.094": "RearViewMirror", "Plane.129": "RearViewMirrorGlass",
    # Trim.
    "Plane.026": "Underfloor", "Circle.030": "ArchLinersFront", "Circle.026": "ArchLinersRear",
    "Plane.073": "InnerShell", "Plane.074": "InnerShellRear", "Plane.063": "ExhaustBlocker",
    "Plane.029": "Seal_1", "Plane.030": "Seal_2", "Plane.031": "Seal_3", "Plane.032": "Seal_4", "Plane.033": "Seal_5",
    "Plane.040": "ScreenCorners", "Plane.043": "Scuttle", "Plane.044": "EngineCoverFrame", "Plane.048": "EngineCoverFrameInner",
    "Plane.093": "ScreenInnerBand", "Plane.126": "FrontBumperLower",
    "Plane.056": "GrilleFront", "Plane.057": "GrilleRear", "Plane.049": "EngineLidVents", "Plane.055": "HeadlampSlots",
    "Plane.062": "DiffuserGrille", "Plane.130": "DiffuserSurround",
    "Plane.131": "Diffuser_1", "Plane.132": "Diffuser_2", "Plane.133": "Diffuser_3",
    "Plane.194": "TailPipes", "Cube.024": "TailPipeBridge", "Plane.193": "ExhaustSurround", "Plane.192": "ExhaustBackPlate",
    "Plane.046": "Keyholes", "Cube.011": "NumberPlateFront", "Cube.013": "NumberPlateRear",
    # Lamps.
    "Plane.027": "HeadlampCovers", "Plane.028": "HeadlampHousings", "Cube.005": "HeadlampInnerHousings",
    "Cube.006": "HeadlampUpperHousings", "Circle.033": "HeadlampBezels", "Circle.034": "HeadlampBackPlates",
    "Circle.043": "HeadlampReflectors", "Circle.044": "HeadlampRings", "Cube.010": "DaytimeLEDs",
    "Circle.023": "SideRepeaters", "Cube.009": "StopLampLEDs", "Plane.058": "StopLampCase", "Plane.059": "StopLampCover",
    "Plane.061": "RearFogLamps",
    # The model's own cabin (dropped by the entry).
    "Plane.090": "InteriorSteeringWheel", "Plane.086": "InteriorBulkhead", "Plane.087": "InteriorTub",
    "Plane.088": "InteriorFootwells", "Plane.089": "InteriorDash",
}
# The engine's parts (everything under the empty Empty.001 but its lettering) by their first material; numbered.
ENGINE_ROOT = "Empty.001"
ENGINE_STEMS = {
    "Engine_MainCover.000": "EngineCover", "Engine_MetalRing.000": "EngineRing", "Engine_MiddleMetal.000": "EngineMetal",
    "Engine_Polts.000": "EngineBolts", "Engine_Wires.001": "EngineWires", "Engine_WiresYellow.000": "EngineWiresYellow",
    "Engine_CoverWithBump.000": "EngineCrinkle", "Engine_InsideCover.000": "EngineBayLiner", "DefaultMat.000": "EngineBayLabel",
}
ENGINE_NAMES = {"Plane.025": "EnginePlenums"}   # three materials: the red crinkle plenums and their covers
CALIPER_MATERIAL = "Breaks.000"                 # 13 parts a corner: joined per corner, named Caliper_<corner>
WIPER_ROOTS = ("NewWiperCtrl1", "NewWiperCtrl1.001")   # each wiper's 5 parts: joined, named Wiper_<side>

# The names tools/prepare_car_models.py gives the prepared file's own nodes (a model node of the same name is found
# instead of them: a node named Body crashed the v72 trial's preparation), and the prefixes it and the game read.
RESERVED = {"body", "carmodel", "wheel0", "wheel1", "wheel2", "wheel3", "steering", "spin", "eye", "dials", "gear",
            "mirror", "mirrorhung", "liner0", "liner1", "liner2", "liner3", "plate"}
RESERVED_PREFIXES = ("Inside_", "Seat_")
# Godot 4.7's scene importer (resource_importer_scene.cpp, _teststr) strips a name's trailing digits, spaces and
# underscores, and then takes a name ending "-x" or "_x", or holding "$x", case aside, as an import hint for these x.
GODOT_HINTS = ("col", "colonly", "convcol", "convcolonly", "navmesh", "noimp", "occ", "occonly", "rigid", "vehicle", "wheel")

# --- Triangle budget ---------------------------------------------------------------------------------------------------

# Subdivision levels set before the bake (the viewport levels, which the bake evaluates). The body keeps level 2.
LEVELS = {
    # The four wheels alike at level 1: the author left three rim faces at level 2 (40,832 triangles each) and the
    # front left's at 1, its hub at 0; a rim fills a few dozen pixels in the game.
    **{name: 1 for name in CORNER_PARTS["RimFace"] + CORNER_PARTS["RimBarrel"] + CORNER_PARTS["RimHub"]
       + CORNER_PARTS["RimHubFace"] + CORNER_PARTS["RimCentre"]},
    "Plane.045": 1,       # the side script (with --badges): 45,312 triangles at level 2; the author rendered it at 1
    "Plane.194": 1,       # the tail pipes
    "Cube.006": 1, "Plane.028": 1, "Circle.034": 1, "Circle.043": 1, "Circle.044": 1, "Cube.010": 1,  # headlamp insides
    "Cylinder.014": 1, "Cylinder.002": 1,   # the red tubes' ends: 2 in the viewport, 1 in the author's renders
    "Plane.126": 1,       # the front bumper's black lower part
    "Plane.029": 1, "Plane.030": 1, "Plane.031": 1, "Plane.032": 1, "Plane.033": 1,   # window seals
    "Plane.065": 1,       # a wiper arm
    # The engine, seen only through the engine cover's tinted glass.
    "Plane.000": 1, "Plane.024": 1, "Plane.084": 1,
    "Plane.019": 0, "Plane.025": 0, "Plane.017": 0, "Plane.021": 0, "Plane.085": 0,
}
# Panes baked as one sheet, their Solidify modifiers off: the game draws its glass and its clear lenses (GLP_Glass,
# GLP_Lens) alpha-blended from both faces, so a pane with a thickness drew as two layers of tint: the windows 0.96
# opaque from outside where the game means 0.8, and the tail lamp covers, a solid tube over the whole lamp, laid two
# to four layers of the lens's white over the LED ring and turned it pink in the first renders. The windscreen, the
# side windows, the engine cover's glass and the two tail lamp covers:
SINGLE_SHEETS = ("Plane.035", "Plane.036", "Plane.037", "Cylinder", "Cylinder.017")
# Collapse decimation after the bake, by final name: meshes without a subdivision to lower.
DECIMATE = {
    # The grilles' wire mesh: 18,260 and 17,532 triangles once their duplicates go; at chase-camera distance a
    # diamond is a pixel or two.
    "GrilleFront": 0.6, "GrilleRear": 0.6, "EngineLidVents": 0.6, "HeadlampSlots": 0.6,
    "ExhaustSurround": 0.4,                 # the perforated surround round the tail pipes (21,220)
    "HeadlampInnerHousings": 0.4,           # behind the headlamp cover (11,840 at level 1)
    "Caliper_FL": 0.35, "Caliper_FR": 0.35, "Caliper_RL": 0.35, "Caliper_RR": 0.35,   # 12-17k a corner, behind the spokes
    "Wiper_L": 0.35, "Wiper_R": 0.35,       # blades of 11,816 triangles each
}
DECIMATE_MARKS = {"BadgeEngineLettering_L": 0.5, "BadgeEngineLettering_R": 0.5}   # with --badges
BUDGET = 620_000   # triangles: the old model's prepared file drew 457k, the trial conversion 942k

# --- Materials ---------------------------------------------------------------------------------------------------------

# One Principled BSDF per material, with the values of the v72 survey of the file's Cycles 2.62 node trees (its
# conversion map: each tree read node by node; colours linear, as Blender's sockets and glTF's factors are; roughness in
# today's convention). base: linear RGB; m: metallic; r: roughness; t: transmission (glass: KHR_materials_transmission,
# which the preparation counts as glass); a: alpha (blended; on glass a see-through fallback for readers without
# transmission, on a coloured lens what keeps its colour); coat: clearcoat weight and roughness; sheen: velvet.
# Departures from the map: the tail lamp face is split (TailLampLED, TailLampChrome, from the 458 research), the red
# lens tube is translucent, and glass keeps Blender's IOR 1.5 (1.45 would add KHR_materials_ior for nothing).
MATERIALS = {
    # Paint: the game draws its own over both (GLP_Paint); the entry sets their factory colour to Rosso Corsa.
    "MainBody.001": dict(base=(0.807, 0.0, 0.0152), m=0.0, r=0.3, coat=(1.0, 0.03)),
    # The strips inside the front lid's shut lines (the file's CarPaint group, a flaked red).
    "MainBody.000": dict(base=(0.78, 0.0, 0.0), m=0.3, r=0.35, coat=(1.0, 0.03)),
    # Glass (the game's GLP_Glass by name).
    "Windshield.000": dict(base=(0.761, 0.799, 0.738), m=0.0, r=0.0, t=1.0, a=0.2),
    "SideWindows.000": dict(base=(0.761, 0.799, 0.738), m=0.0, r=0.0, t=1.0, a=0.2),
    "RearWindow.000": dict(base=(0.859, 0.902, 0.833), m=0.0, r=0.0, t=1.0, a=0.15),
    # Clear lamp covers: glass by their transmission and lamps by "Light" in their names, so the game's GLP_Lens.
    "Front_Light_Covers.000": dict(base=(0.74, 0.72, 0.74), m=0.0, r=0.0, t=1.0, a=0.1),
    "Rear_Light_Cover.000": dict(base=(1.0, 0.723, 0.671), m=0.0, r=0.0, t=1.0, a=0.1),
    # The high-mount stop lamp's red cover: blended, so it keeps its red over the LEDs (with transmission the
    # preparation would make it a clear lens).
    "Break_Light_Cover.000": dict(base=(0.8, 0.032, 0.032), m=0.0, r=0.0, a=0.45),
    # The tail lamps' red lens tubes: a translucent red lens of their own, never the paint (the earlier model painted
    # them, and they turned yellow or white with the car; the real tube is red on every colour).
    "Rear_Light_RedSide.000": dict(base=(0.55, 0.02, 0.02), m=0.0, r=0.25, a=0.8),
    # The tail lamp faces (Rear_Light_Tex.000, a photograph of another lamp) split in two (split_tail_lamp), as the 458
    # research (v70) describes the real lamp: the LED ring, a red prismatic lens (the entry's brake: the game lights
    # it when braking)...
    "TailLampLED": dict(base=(0.30, 0.01, 0.01), m=0.0, r=0.15),
    # ...and the concentric reflector and the clear inner ring inside it, chrome (a mirror's F0, not white).
    "TailLampChrome": dict(base=(0.6, 0.6, 0.62), m=1.0, r=0.1),
    "Break_Light_Highlight.000": dict(base=(0.869, 0.0055, 0.0), m=0.0, r=0.05),   # stop lamp LEDs (brake)
    "Brake_Light_Dull.000": dict(base=(0.142, 0.0099, 0.0067), m=0.0, r=0.7),
    "Break_Light_Case.000": dict(base=(0.277, 0.13, 0.12), m=0.0, r=0.8),
    # Rear fog lamps and side repeaters: plain lenses, unlit (their textures were photographs of other lamps).
    "Rear_Bumper_Lights.000": dict(base=(0.35, 0.03, 0.03), m=0.0, r=0.1),
    "SideBlinker.000": dict(base=(0.6, 0.15, 0.02), m=0.0, r=0.05),
    "Main_Light.000": dict(base=(0.8, 0.8, 0.8), m=1.0, r=0.17),
    "Main_Light_Ring.000": dict(base=(0.57, 0.57, 0.57), m=1.0, r=0.32),
    "Main_Light_Cover.000": dict(base=(0.123, 0.123, 0.123), m=0.8, r=0.53),
    "LEDsOff.000": dict(base=(0.8, 0.8, 0.8), m=0.0, r=0.22),
    # Wheels.
    "Rims.000": dict(base=(0.8, 0.82, 0.82), m=1.0, r=0.25),
    "Logo_Wheel.000": dict(base=(0.7, 0.55, 0.02), m=0.0, r=0.15),      # the caps: plain yellow (photograph left out)
    "Tire.000": dict(base=(0.035, 0.035, 0.035), m=0.0, r=0.85),
    "Tire.002": dict(base=(0.035, 0.035, 0.035), m=0.0, r=0.85),
    "Break_Discs.000": dict(base=(0.1, 0.1, 0.1), m=0.6, r=0.4),
    "Breaks.000": dict(base=(0.3, 0.3, 0.3), m=0.6, r=0.3),             # grey, as the node tree has it ("Ferrari" left out)
    # Trim.
    "Bottom.000": dict(base=(0.035, 0.035, 0.035), m=0.0, r=0.95),
    "WheelArches.000": dict(base=(0.035, 0.035, 0.035), m=0.0, r=0.9),
    "DiffuseBlockers.000": dict(base=(0.035, 0.035, 0.035), m=0.0, r=1.0),
    "RubberSeals.000": dict(base=(0.035, 0.035, 0.035), m=0.0, r=0.35),
    "WindowTriangle.000": dict(base=(0.03, 0.03, 0.03), m=0.0, r=0.2),
    "HoodWindowConnector.000": dict(base=(0.03, 0.03, 0.03), m=0.0, r=0.45),
    "RearWindowEdges.000": dict(base=(0.025, 0.025, 0.025), m=0.0, r=0.1),
    "WindowInteriorEdge.000": dict(base=(0.035, 0.035, 0.035), m=0.0, r=0.9),
    "FrontBumper.000": dict(base=(0.05, 0.05, 0.05), m=0.0, r=0.8),
    "Grills_Back.000": dict(base=(0.12, 0.12, 0.12), m=0.0, r=0.45),
    "Grills_BackCarbon.000": dict(base=(0.12, 0.12, 0.12), m=0.0, r=0.45),
    "Grills_Front.000": dict(base=(0.64, 0.64, 0.64), m=0.2, r=0.3),
    "RearFibreSurroundings.000": dict(base=(0.12, 0.12, 0.12), m=0.3, r=0.35),
    "CarbonFibre.000": dict(base=(0.03, 0.03, 0.03), m=0.0, r=0.3, coat=(1.0, 0.05)),   # its weave image is missing
    "Exhaust.000": dict(base=(0.33, 0.34, 0.32), m=1.0, r=0.55),
    "Exhaust_Cover.000": dict(base=(0.237, 0.237, 0.237), m=0.0, r=0.8),
    "DefaultMat.000": dict(base=(0.035, 0.035, 0.035), m=0.0, r=0.9),
    "Keyhole.000": dict(base=(0.184, 0.184, 0.184), m=1.0, r=0.32),
    "Numberplate_ins": dict(base=(0.7, 0.7, 0.7), m=0.0, r=0.3),        # blank plates (the "728 ACF" texture left out)
    "numberplate_outs": dict(base=(0.05, 0.05, 0.05), m=0.0, r=0.3),
    "Wipers.000": dict(base=(0.04, 0.04, 0.04), m=0.0, r=0.45),
    # Mirrors.
    "SideMirrorsReflective.000": dict(base=(0.8, 0.8, 0.8), m=1.0, r=0.02),
    "RearViewMirror.000": dict(base=(0.03, 0.03, 0.03), m=0.0, r=0.7),
    "RearViewMirror.Reflection.000": dict(base=(0.926, 0.926, 0.926), m=1.0, r=0.0),
    # The model's own cabin (dropped by the entry; kept in the file).
    "Interior.000": dict(base=(0.35, 0.2, 0.11), m=0.0, r=0.7, sheen=0.3),
    "SteeringWheel.000": dict(base=(0.035, 0.035, 0.035), m=0.0, r=0.6),
    # Engine.
    "Engine_Red.000": dict(base=(0.6, 0.03, 0.03), m=0.0, r=0.55),
    "Engine_MainCover.000": dict(base=(0.115, 0.115, 0.115), m=0.5, r=0.6),
    "Engine_InsideCover.000": dict(base=(0.035, 0.035, 0.035), m=0.0, r=0.6),
    "Engine_CoverWithBump.000": dict(base=(0.07, 0.07, 0.07), m=0.0, r=0.55),
    "Engine_MetalRing.000": dict(base=(0.8, 0.8, 0.8), m=1.0, r=0.6),
    "Engine_MiddleMetal.000": dict(base=(0.64, 0.64, 0.64), m=1.0, r=0.6),
    "Engine_MiddleMetalCap.000": dict(base=(0.359, 0.349, 0.28), m=1.0, r=0.32),
    "Engine_Polts.000": dict(base=(0.05, 0.05, 0.05), m=1.0, r=0.45),
    "Engine_Wires.001": dict(base=(0.04, 0.04, 0.04), m=0.0, r=0.5),
    "Engine_WiresYellow.000": dict(base=(0.487, 0.472, 0.26), m=0.0, r=0.67),
    # The marks, plain (with --badges only): no texture of theirs is used.
    "Logo_Hood.000": dict(base=(0.7, 0.55, 0.02), m=0.0, r=0.1),
    "Ferrari_Badge.000": dict(base=(0.7, 0.55, 0.02), m=0.0, r=0.1),
    "FerrariHorse.000": dict(base=(0.75, 0.75, 0.75), m=1.0, r=0.15),
    "FerrariText.000": dict(base=(0.58, 0.58, 0.58), m=1.0, r=0.35),
    "PinifarniaLogo.000": dict(base=(0.47, 0.47, 0.47), m=1.0, r=0.35),
    "Engine_FerrariText.000": dict(base=(0.58, 0.58, 0.58), m=1.0, r=0.55),
}
TAIL_LAMP_FACE_MATERIAL = "Rear_Light_Tex.000"
# The materials that may carry transmission: the windows and the clear lamp covers (checked in the written file).
TRANSMISSIVE = {"Windshield.000", "SideWindows.000", "RearWindow.000", "Front_Light_Covers.000", "Rear_Light_Cover.000"}


def arguments():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="convert_rainers_458.py")
    parser.add_argument("--out", required=True, help="the glb to write")
    parser.add_argument("--report", help="a text report of what was done and measured")
    parser.add_argument("--wheelbase", type=float, default=2.650, help="metres between the axles (the 458's 2650 mm)")
    # The LED ring's inner edge, in the face's radius: the face's relief has the inner ring's chrome lip at 0.54-0.57,
    # a groove at 0.58-0.59 and the LED ring's floor from 0.60 out; the author's photograph turns red at 0.55-0.60, and
    # the real lamp's red annulus starts at about 55 % of the face (v70 research).
    parser.add_argument("--ring", type=float, default=0.58, help="the LED ring's inner radius over the face's radius")
    parser.add_argument("--badges", action="store_true", help="keep the maker's marks as plain shapes (Badge...)")
    return parser.parse_args(argv)


class Report:
    def __init__(self):
        self.lines = []

    def __call__(self, text=""):
        self.lines.append(text)
        print(text)

    def write(self, path):
        if path:
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write("\n".join(self.lines) + "\n")


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def descendants(ob):
    out = []
    for child in ob.children:
        out.append(child)
        out.extend(descendants(child))
    return out


def under(ob, root_name):
    parent = ob.parent
    while parent is not None:
        if parent.name == root_name:
            return True
        parent = parent.parent
    return False


def triangles(mesh):
    mesh.calc_loop_triangles()
    return len(mesh.loop_triangles)


def evaluated_triangles(ob, depsgraph):
    evaluated = ob.evaluated_get(depsgraph)
    try:
        mesh = evaluated.to_mesh()
    except RuntimeError:
        return 0
    count = triangles(mesh) if mesh is not None else 0
    evaluated.to_mesh_clear()
    return count


def world_points(ob, depsgraph):
    """An object's evaluated vertices in world space (modifiers and constraints applied)."""
    evaluated = ob.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    matrix = evaluated.matrix_world.copy()
    points = [matrix @ v.co for v in mesh.vertices]
    evaluated.to_mesh_clear()
    return points


def box(points):
    lo = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return lo, hi


def mesh_box(mesh):
    return box([v.co for v in mesh.vertices])


def centre_of(mesh):
    lo, hi = mesh_box(mesh)
    return (lo + hi) / 2


def godot_hint(name):
    what = name.replace(".", "_")
    while what and (what[-1].isdigit() or ord(what[-1]) <= 32 or what[-1] == "_"):
        what = what[:-1]
    low = what.lower()
    return next((h for h in GODOT_HINTS if f"${h}" in low or low.endswith(f"-{h}") or low.endswith(f"_{h}")), None)


def reset_rig(say, wheelbase):
    """Straightens the front wheels and puts the car at the origin, unturned, in metres; returns the tyres' centres."""
    car = bpy.data.objects["CarCtrl"]
    say(f"studio pose: CarCtrl at {tuple(round(v, 4) for v in car.location)}, turned "
        f"{tuple(round(math.degrees(v), 2) for v in car.rotation_euler)} deg, scale {car.scale.x:.4f}; WheelsRot "
        f"{tuple(round(math.degrees(v), 2) for v in bpy.data.objects['WheelsRot'].rotation_euler)} deg")
    # The front wheels and their discs copy WheelsRot's turn through COPY_ROTATION constraints: at 0 they point ahead.
    bpy.data.objects["WheelsRot"].rotation_euler = (0.0, 0.0, 0.0)
    car.location = (0.0, 0.0, 0.0)
    car.rotation_euler = (0.0, 0.0, 0.0)
    car.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()

    def tread_centres():
        out = {}
        for name in CORNER_PARTS["Tyre"]:
            lo, hi = box(world_points(bpy.data.objects[name], depsgraph))
            out[name] = ((lo + hi) / 2, lo, hi)
        return out

    centres = tread_centres()
    # Unturned, the car's long axis is the rig's Y; the nose is the end the headlamp covers are at.
    ys = sorted(c[0].y for c in centres.values())
    front_y = (ys[0] + ys[1]) / 2
    rear_y = (ys[2] + ys[3]) / 2
    lamps = box(world_points(bpy.data.objects["Plane.027"], depsgraph))
    if not (lamps[0].y + lamps[1].y) / 2 < front_y:
        raise SystemExit("the headlamps are not at -Y once CarCtrl is unturned: not the file this script was written for")
    units = abs(rear_y - front_y)
    scale = wheelbase / units
    car.scale = (scale, scale, scale)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    centres = tread_centres()
    middle = sum((c[0] for c in centres.values()), Vector()) / 4
    ground = min(c[1].z for c in centres.values())
    car.location = (-middle.x, -middle.y, -ground)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    centres = tread_centres()
    say(f"rig reset: WheelsRot 0; CarCtrl unturned, scale {scale:.6f} (the tyres' centres {units:.4f} rig units apart: "
        f"1 rig unit = {scale:.5f} m), moved by ({-middle.x:+.4f} {-middle.y:+.4f} {-ground:+.4f}) m")
    return depsgraph, {name: c[0] for name, c in centres.items()}


def corner_of(point, hubs):
    """FL/FR/RL/RR of the nearest hub: front is -Y (the nose), left is +X."""
    name = min(hubs, key=lambda n: (hubs[n] - point).length)
    hub = hubs[name]
    return ("F" if hub.y < 0 else "R") + ("L" if hub.x > 0 else "R")


def set_materials(mesh, materials):
    """Puts materials in a mesh's slots, in order, keeping its faces' material indices. Mesh.materials.clear() would
    set every face to the first slot: the stop lamp's strip, the plates' edges and the engine's red plenums lost
    their second materials that way in a first draft."""
    while len(mesh.materials) < len(materials):
        mesh.materials.append(None)
    for index, material in enumerate(materials):
        mesh.materials[index] = material


def face_materials(mesh):
    """The names of the materials a mesh's faces use (not merely its slots)."""
    indices = {polygon.material_index for polygon in mesh.polygons}
    return {mesh.materials[i].name for i in indices if i < len(mesh.materials) and mesh.materials[i] is not None}


def bake(ob, depsgraph):
    """The object's evaluated mesh (modifiers, curves' bevels) in world space, so its node can stay at identity. Under a
    negative scale the faces are turned back: Mesh.transform moves the vertices but keeps the winding, so a mirrored
    part would face into the car."""
    evaluated = ob.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
    matrix = evaluated.matrix_world.copy()
    mesh.transform(matrix)
    flipped = matrix.determinant() < 0
    if flipped:
        mesh.flip_normals()
    # The materials of the object's slots, in slot order, on the mesh (some 2.62 slots are linked to the object).
    set_materials(mesh, [slot.material for slot in ob.material_slots])
    mesh.update()
    return mesh, flipped


def remove_duplicate_faces(mesh):
    """Faces drawn twice. Two kinds are in the file: the four grilles' Mirror merges their middle's vertices without
    clipping, so every face there is listed twice over the same vertices, wound both ways (the exporter's mesh
    validation dropped both copies in the v72 trial, leaving holes); and parts mirrored on two axes, or made of two
    copies of one disc (the rim hub faces, the wiper blades), hold faces with their own vertices in the very same place
    (within 10 micrometres: the file's shortest edge is 48), facing the same way. A pair facing opposite ways over
    vertices of their own is a sheet seen from both sides, and stays."""
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.normal_update()
    seen = {}
    doomed = []
    for face in bm.faces:
        key = tuple(sorted((round(v.co.x, 5), round(v.co.y, 5), round(v.co.z, 5)) for v in face.verts))
        other = seen.get(key)
        if other is None:
            seen[key] = face
        elif {v.index for v in other.verts} == {v.index for v in face.verts} or other.normal.dot(face.normal) > 0.0:
            doomed.append(face)
    if doomed:
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        bm.to_mesh(mesh)
        mesh.update()
    bm.free()
    return len(doomed)


def join(name, meshes):
    """One mesh of several (world-space meshes: no transforms to apply), materials merged by name."""
    bm = bmesh.new()
    materials = []
    for mesh in meshes:
        remap = []
        for material in mesh.materials:
            if material not in materials:
                materials.append(material)
            remap.append(materials.index(material))
        start = len(bm.faces)
        bm.from_mesh(mesh)   # appends to what the bmesh holds
        bm.faces.ensure_lookup_table()
        for i in range(start, len(bm.faces)):
            face = bm.faces[i]
            face.material_index = remap[face.material_index] if remap else 0
    out = bpy.data.meshes.new(name)
    bm.to_mesh(out)
    bm.free()
    for material in materials:
        out.materials.append(material)
    expected = sum(len(m.polygons) for m in meshes)
    if len(out.polygons) != expected:
        raise SystemExit(f"joining {name}: {len(out.polygons)} faces, expected {expected}")
    return out


def decimate(mesh, ratio):
    """A collapse decimation of a world-space mesh (through a temporary object: Blender decimates in a modifier)."""
    holder = bpy.data.objects.new("decimate_holder", mesh)
    bpy.context.scene.collection.objects.link(holder)
    modifier = holder.modifiers.new("Decimate", "DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = ratio
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    out = bpy.data.meshes.new_from_object(holder.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
    set_materials(out, list(mesh.materials))
    bpy.data.objects.remove(holder, do_unlink=True)
    return out


def split_tail_lamp(mesh, led, chrome, ring):
    """Gives the face's polygons the LED ring's material at or past ring x the face's radius (in its UV map, a flat
    projection of the face centred on the lamp's axis: the v72 survey found the UV radius proportional to the
    distance from the axis, 0 at it) and the chrome's inside it. Each lamp's own centre and radius: the two faces' UV
    maps are not quite alike (centres 0.492/0.509 and 0.483/0.506)."""
    layer = mesh.uv_layers.active or mesh.uv_layers[0]
    uvs = [Vector(d.uv) for d in layer.data]
    lo = Vector((min(u.x for u in uvs), min(u.y for u in uvs)))
    hi = Vector((max(u.x for u in uvs), max(u.y for u in uvs)))
    centre = (lo + hi) / 2
    radius = max((u - centre).length for u in uvs)
    mesh.materials.clear()
    mesh.materials.append(led)
    mesh.materials.append(chrome)
    counts = [0, 0]
    for polygon in mesh.polygons:
        loops = range(polygon.loop_start, polygon.loop_start + polygon.loop_total)
        mean = sum((uvs[i] for i in loops), Vector((0.0, 0.0))) / polygon.loop_total
        index = 0 if (mean - centre).length / radius >= ring else 1
        polygon.material_index = index
        counts[index] += 1
    mesh.update()
    return centre, radius, counts


def principled(material, spec):
    """Replaces a material's node tree with one Principled BSDF holding spec's values."""
    if material.node_tree is None:
        material.use_nodes = True   # only for a material without a tree (Blender 5 gives new ones a tree)
    tree = material.node_tree
    tree.nodes.clear()
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (-300.0, 0.0)
    tree.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])

    def put(socket, value):
        if socket not in bsdf.inputs:
            raise SystemExit(f"Principled BSDF has no input {socket!r} in this Blender")
        bsdf.inputs[socket].default_value = value

    base = spec["base"]
    alpha = spec.get("a", 1.0)
    put("Base Color", (base[0], base[1], base[2], 1.0))
    put("Metallic", spec["m"])
    put("Roughness", spec["r"])
    put("Transmission Weight", spec.get("t", 0.0))
    put("Alpha", alpha)
    coat, coat_roughness = spec.get("coat", (0.0, 0.03))
    put("Coat Weight", coat)
    put("Coat Roughness", coat_roughness)
    put("Sheen Weight", spec.get("sheen", 0.0))
    put("Emission Strength", 0.0)
    put("Emission Color", (0.0, 0.0, 0.0, 1.0))
    # Drawn from both faces (glTF doubleSided): several of the file's parts are single sheets seen from either side.
    material.use_backface_culling = False
    # Blended where alpha is under 1 (the exporter writes alphaMode BLEND then), opaque otherwise.
    material.surface_render_method = "BLENDED" if alpha < 1.0 else "DITHERED"
    # The viewport colour too, so a Workbench look at the result shows the same colours.
    material.diffuse_color = (base[0], base[1], base[2], alpha)
    material.metallic = spec["m"]
    material.roughness = spec["r"]


def read_glb(path):
    """The JSON chunk and the binary chunk of a glb."""
    with open(path, "rb") as f:
        data = f.read()
    length = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20 + length])
    offset = 20 + length
    blob = b""
    if offset < len(data):
        blob_length = struct.unpack_from("<I", data, offset)[0]
        blob = data[offset + 8:offset + 8 + blob_length]
    return doc, blob


def check_written(doc, blob, say, used):
    """What tools/prepare_car_models.py and the game rely on, read back from the written file (v72 pipeline, B9).
    Raises when something is off: the file goes to a release asset and its SHA-256 into the repository."""
    problems = []
    written = {m.get("name") for m in doc.get("materials", [])}
    if written != used:
        problems.append(f"materials the faces use but not written: {sorted(used - written)}; written but unused: {sorted(written - used)}")
    nodes = doc.get("nodes", [])
    moved = [n.get("name") for n in nodes if any(k in n for k in ("translation", "rotation", "scale", "matrix"))]
    if moved:
        problems.append(f"nodes with a transform (their boxes would not be their vertices'): {moved}")
    if doc.get("extensionsRequired"):
        problems.append(f"extensionsRequired {doc['extensionsRequired']}")
    if doc.get("images") or doc.get("textures"):
        problems.append(f"{len(doc.get('images', []))} image(s), {len(doc.get('textures', []))} texture(s) written")
    for material in doc.get("materials", []):
        name = material.get("name", "")
        pbr = material.get("pbrMetallicRoughness", {})
        if "KHR_materials_transmission" in material.get("extensions", {}) and name not in TRANSMISSIVE:
            problems.append(f"{name} has transmission: the preparation would take it for glass")
        if pbr.get("metallicFactor", 1.0) >= 0.999 and pbr.get("roughnessFactor", 1.0) >= 0.999:
            problems.append(f"{name} has glTF's default metallic 1 / roughness 1 (an unconverted material)")
    for name in TRANSMISSIVE:
        if not any(m.get("name") == name and "KHR_materials_transmission" in m.get("extensions", {}) for m in doc.get("materials", [])):
            problems.append(f"{name} has no transmission: the preparation would not take it for glass")
    accessors = doc.get("accessors", [])
    total = 0
    for mesh in doc.get("meshes", []):
        for primitive in mesh["primitives"]:
            if primitive.get("mode", 4) != 4:
                problems.append(f"{mesh.get('name')}: a primitive that is not triangles")
            if "COLOR_0" in primitive["attributes"]:
                problems.append(f"{mesh.get('name')}: vertex colours (Godot would tint the material by them)")
            if any(k in primitive for k in ("extensions",)):
                problems.append(f"{mesh.get('name')}: a compressed primitive (the preparation reads plain accessors)")
            total += accessors[primitive["indices"]]["count"] // 3
    if total > BUDGET:
        problems.append(f"{total:,} triangles, over the budget of {BUDGET:,}")
    if blob is None or not blob:
        problems.append("no binary chunk")
    say(f"  written: {len(nodes)} nodes, {len(doc.get('meshes', []))} meshes, {len(doc.get('materials', []))} materials, "
        f"{total:,} triangles, no transforms, no images, no extensionsRequired, transmission only on {sorted(TRANSMISSIVE)}")
    if problems:
        for problem in problems:
            say(f"  CHECK FAILED: {problem}")
        raise SystemExit("the written file failed its checks")


def main():
    args = arguments()
    say = Report()
    blend = bpy.data.filepath
    digest = sha256(blend)
    say(f"source: {os.path.basename(blend)} ({os.path.getsize(blend):,} bytes, SHA-256 {digest})")
    if digest != SOURCE_SHA256:
        say(f"  NOTE: not the file this script was written for (SHA-256 {SOURCE_SHA256}); its checks will tell what differs")
    say(f"Blender {bpy.app.version_string}, Python {sys.version.split()[0]}; options: wheelbase {args.wheelbase} m, "
        f"ring {args.ring}, marks {'kept as plain shapes' if args.badges else 'left out'}")
    for needed in ("CarCtrl", "WheelsRot", "Plane.008", ENGINE_ROOT, *LEFT_OUT):
        if needed not in bpy.data.objects:
            raise SystemExit(f"{needed} is not in the file: not RainerS's ferrari_studio.blend")

    # 1. The car: CarCtrl's hierarchy. Everything else is the studio.
    car_root = bpy.data.objects["CarCtrl"]
    car = descendants(car_root)
    studio = [o for o in bpy.data.objects if o is not car_root and o not in car]
    say(f"car: {len(car)} objects under CarCtrl; studio left out: {sorted(o.name for o in studio)}")
    # A part hidden in the viewport would not be evaluated (the bake reads the viewport's evaluation).
    hidden = [o.name for o in car if o.hide_viewport or o.hide_get()]
    for name in hidden:
        bpy.data.objects[name].hide_viewport = False
        bpy.data.objects[name].hide_set(False)
    say(f"shown in the viewport for the bake: {hidden or 'none were hidden'}")

    # 2. The rig reset.
    depsgraph, hubs = reset_rig(say, args.wheelbase)
    corners = {name: corner_of(centre, hubs) for name, centre in hubs.items()}
    for name, corner in corners.items():
        parent = bpy.data.objects[name].parent
        if parent is None or RIG_CORNERS.get(parent.name) != corner:
            raise SystemExit(f"{name} is {corner} by position but hangs under {parent.name if parent else None}")

    # 3. What is drawn: meshes, and curves with faces (the engine's wires); not the rig's empties and rails, and not
    # the marks unless they are asked for.
    left_out = set(LEFT_OUT)
    marks = set(MARKS) | {name for pair in SIDED_MARKS.values() for name in pair}
    if not args.badges:
        left_out |= marks
    drawn = []
    for ob in car:
        if ob.name in left_out or ob.type not in {"MESH", "CURVE", "FONT", "SURFACE"} or ob.hide_render:
            continue
        if evaluated_triangles(ob, depsgraph) == 0:
            continue
        drawn.append(ob)
    skipped = sorted(o.name for o in car if o not in drawn)
    say(f"drawn: {len(drawn)} objects; not drawn: {skipped}")
    if not args.badges:
        say(f"marks left out: {sorted(f'{name} ({MARKS.get(name) or next(k for k, v in SIDED_MARKS.items() if name in v)})' for name in marks)}")

    # 4. The triangle budget's subdivision levels, before the bake evaluates the modifiers.
    before = {ob.name: evaluated_triangles(ob, depsgraph) for ob in drawn}
    drawn_names = {ob.name for ob in drawn}
    for name, level in LEVELS.items():
        if name not in drawn_names:
            continue
        ob = bpy.data.objects[name]
        subsurfs = [m for m in ob.modifiers if m.type == "SUBSURF"]
        if not subsurfs:
            raise SystemExit(f"{name} has no subdivision to set")
        for modifier in subsurfs:
            modifier.levels = level
            modifier.show_viewport = True
            # The front left rim's hub was saved at level 0, and the file came through versioning with its OpenSubdiv
            # quality at 0, which leaves the mesh unsubdivided at any level (Blender's own default is 3).
            if modifier.quality < 1:
                modifier.quality = 3
    thinned = []
    for name in SINGLE_SHEETS:
        for modifier in bpy.data.objects[name].modifiers:
            if modifier.type == "SOLIDIFY" and modifier.show_viewport:
                modifier.show_viewport = False
                thinned.append(f"{name} ({modifier.thickness:.4f} local units)")
    if len(thinned) != len(SINGLE_SHEETS):
        raise SystemExit(f"expected one Solidify on each of {SINGLE_SHEETS}, found {thinned}")
    say(f"panes baked as one sheet (Solidify off): {thinned}")
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()

    # 5. The bake: every drawn object's mesh in world space.
    baked = {}
    flipped = []
    for ob in drawn:
        mesh, negative = bake(ob, depsgraph)
        baked[ob.name] = mesh
        if negative:
            flipped.append(ob.name)
        # Every material the object's faces use is still used by the baked mesh's faces.
        evaluated = ob.evaluated_get(depsgraph)
        source = evaluated.to_mesh()
        slots = ob.material_slots
        expected = {slots[i].material.name for i in {p.material_index for p in source.polygons} if i < len(slots) and slots[i].material}
        evaluated.to_mesh_clear()
        if face_materials(mesh) != expected:
            raise SystemExit(f"{ob.name}: its faces use {sorted(expected)} but the baked mesh's {sorted(face_materials(mesh))}")
    say(f"baked {len(baked)} meshes; {len(flipped)} under a negative scale had their faces turned back: {sorted(flipped)}")
    roles = {}
    for ob in drawn:
        materials = [s.material.name for s in ob.material_slots if s.material]
        if CALIPER_MATERIAL in materials:
            roles[ob.name] = ("caliper", None)
        elif any(under(ob, w) for w in WIPER_ROOTS):
            roles[ob.name] = ("wiper", next(w for w in WIPER_ROOTS if under(ob, w)))
        elif ob.name in MARKS:
            roles[ob.name] = ("named", MARKS[ob.name])
        elif any(ob.name in v for v in SIDED_MARKS.values()):
            roles[ob.name] = ("sided", next(k for k, v in SIDED_MARKS.items() if ob.name in v))
        elif any(ob.name in v for v in SIDED_PARTS.values()):
            roles[ob.name] = ("sided", next(k for k, v in SIDED_PARTS.items() if ob.name in v))
        elif under(ob, ENGINE_ROOT):
            roles[ob.name] = ("engine", ENGINE_NAMES.get(ob.name) or ENGINE_STEMS.get(materials[0]))
        elif any(ob.name in v for v in CORNER_PARTS.values()):
            roles[ob.name] = ("corner", next(k for k, v in CORNER_PARTS.items() if ob.name in v))
        elif ob.name in NAMES:
            roles[ob.name] = ("named", NAMES[ob.name])
        else:
            roles[ob.name] = None
        if roles[ob.name] is None or roles[ob.name][1] is None and roles[ob.name][0] == "engine":
            raise SystemExit(f"{ob.name} ({materials}) has no name in this script's tables")

    # The studio and the rig go; from here on only the baked meshes matter.
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)

    # 6. Duplicate faces.
    duplicates = {}
    for name, mesh in baked.items():
        count = remove_duplicate_faces(mesh)
        if count:
            duplicates[name] = count
    say(f"duplicate faces removed: {duplicates} ({sum(duplicates.values()):,} in all)")

    # 7. Names: per corner, per side, joined calipers and wipers, the engine numbered.
    final = {}          # final name -> mesh
    origin = {}         # final name -> source objects

    def put(name, mesh, parts):
        if name in final:
            raise SystemExit(f"two parts named {name}: {origin[name]} and {parts}")
        final[name] = mesh
        origin[name] = parts

    calipers = {}
    wipers = {}
    engine = {}
    for source, (role, base) in roles.items():
        mesh = baked[source]
        if role == "caliper":
            calipers.setdefault(corner_of(centre_of(mesh), hubs), []).append(source)
        elif role == "wiper":
            wipers.setdefault(base, []).append(source)
        elif role == "engine":
            engine.setdefault(base, []).append(source)
        elif role == "corner":
            put(f"{base}_{corner_of(centre_of(mesh), hubs)}", mesh, [source])
        elif role == "sided":
            put(f"{base}_{'L' if centre_of(mesh).x > 0 else 'R'}", mesh, [source])
        else:
            put(base, mesh, [source])
    for corner, parts in sorted(calipers.items()):
        put(f"Caliper_{corner}", join(f"Caliper_{corner}", [baked[p] for p in parts]), sorted(parts))
    for root, parts in sorted(wipers.items()):
        mesh = join("wiper", [baked[p] for p in parts])
        put(f"Wiper_{'L' if centre_of(mesh).x > 0 else 'R'}", mesh, sorted(parts))
    for stem, parts in sorted(engine.items()):
        ordered = sorted(parts, key=lambda p: (round(-centre_of(baked[p]).y, 3), p))
        for index, part in enumerate(ordered, 1):
            put(stem if len(ordered) == 1 else f"{stem}_{index}", baked[part], [part])
    if len(calipers) != 4 or any(len(p) != 13 for p in calipers.values()):
        raise SystemExit(f"calipers: {[(c, len(p)) for c, p in calipers.items()]}, expected 13 parts at each of 4 corners")

    mark_names = set(MARKS.values()) | {f"{k}_{s}" for k in SIDED_MARKS for s in "LR"}
    for name in final:
        if name.lower() in RESERVED or name.startswith(RESERVED_PREFIXES) or godot_hint(name):
            raise SystemExit(f"{name}: a name the preparation or Godot reads as its own")
        if "interior" in name.lower() and not name.startswith("Interior"):
            raise SystemExit(f"{name}: the entry's drop selector \"interior\" would take it")
        if "badge" in name.lower() and name not in mark_names:
            raise SystemExit(f"{name}: the entry's drop selector \"badge\" would take it")

    # 8. Materials: one Principled BSDF each; the tail lamp face's photograph replaced by the LED ring and chrome.
    used = {m.name for mesh in final.values() for m in mesh.materials if m}
    missing = sorted(m for m in used - {TAIL_LAMP_FACE_MATERIAL} if m not in MATERIALS)
    if missing:
        raise SystemExit(f"materials without values in this script: {missing}")
    for name in sorted(used - {TAIL_LAMP_FACE_MATERIAL}):
        principled(bpy.data.materials[name], MATERIALS[name])
    led = bpy.data.materials.new("TailLampLED")
    chrome = bpy.data.materials.new("TailLampChrome")
    principled(led, MATERIALS["TailLampLED"])
    principled(chrome, MATERIALS["TailLampChrome"])
    for side in ("L", "R"):
        centre, radius, counts = split_tail_lamp(final[f"TailLampFace_{side}"], led, chrome, args.ring)
        say(f"TailLampFace_{side}: UV centre ({centre.x:.4f}, {centre.y:.4f}), radius {radius:.4f}; ring from "
            f"{args.ring:.2f} R: {counts[0]} faces TailLampLED, {counts[1]} faces TailLampChrome")

    # 9. Decimation (after the joins and the duplicate faces).
    decimated = {}
    for name, ratio in {**DECIMATE, **(DECIMATE_MARKS if args.badges else {})}.items():
        if name not in final:
            raise SystemExit(f"nothing named {name} to decimate")
        was = triangles(final[name])
        final[name] = decimate(final[name], ratio)
        decimated[name] = (was, triangles(final[name]))

    # 10. The objects, at identity, in one collection; the export.
    collection = bpy.data.collections.new("Car")
    bpy.context.scene.collection.children.link(collection)
    objects = []
    for name in sorted(final):
        mesh = final[name]
        mesh.name = name
        ob = bpy.data.objects.new(name, mesh)
        collection.objects.link(ob)
        if ob.name != name:
            raise SystemExit(f"{name} came out as {ob.name}")
        objects.append(ob)
    bpy.context.view_layer.update()
    for ob in bpy.context.view_layer.objects:
        ob.select_set(ob in objects)

    # Measures of the result, for the report and the entry; the orientation and the size checked.
    say("")
    total = sum(triangles(ob.data) for ob in objects)
    used = sorted(set().union(*(face_materials(ob.data) for ob in objects)))
    points = [v.co for ob in objects for v in ob.data.vertices]
    lo, hi = box(points)
    slo, shi = mesh_box(final["Shell"])
    body = [v.co for ob in objects if ob.name not in ("MirrorHousings", "WingMirrorGlass") for v in ob.data.vertices]
    blo, bhi = box(body)
    say(f"result: {len(objects)} objects, {total:,} triangles, {len(used)} materials, no images")
    say(f"  overall {hi.y - lo.y:.3f} m long, {shi.x - slo.x:.3f} m wide over the body shell ({bhi.x - blo.x:.3f} over the "
        f"rear rims' faces, {hi.x - lo.x:.3f} over the mirrors), {hi.z - lo.z:.3f} m high; nose at y {lo.y:+.3f} "
        f"(glTF z {-lo.y:+.3f}), tail at y {hi.y:+.3f}")
    tyres = {}
    for corner in ("FL", "FR", "RL", "RR"):
        mesh = final[f"Tyre_{corner}"]
        tlo, thi = mesh_box(mesh)
        hub = (tlo + thi) / 2
        radius = max(math.hypot(v.co.y - hub.y, v.co.z - hub.z) for v in mesh.vertices)
        tyres[corner] = (hub, radius, thi.x - tlo.x, tlo.z)
        wall_lo, wall_hi = mesh_box(final[f"TyreWall_{corner}"])
        say(f"  Tyre_{corner}: hub ({hub.x:+.4f} {hub.y:+.4f} {hub.z:+.4f}), radius by vertices {radius:.4f} m, tread "
            f"{thi.x - tlo.x:.4f} m wide, {wall_hi.x - wall_lo.x:.4f} m over the walls")
    wb = (tyres["RL"][0].y + tyres["RR"][0].y - tyres["FL"][0].y - tyres["FR"][0].y) / 2
    say(f"  wheelbase {wb:.4f} m; tracks {tyres['FL'][0].x - tyres['FR'][0].x:.4f} / {tyres['RL'][0].x - tyres['RR'][0].x:.4f} m (tread centres)")
    for corner in ("FL", "FR"):
        dlo, dhi = mesh_box(final[f"BrakeDisc_{corner}"])
        say(f"  BrakeDisc_{corner} {dhi.x - dlo.x:.4f} m thick across x: the front wheels point straight ahead")
    face_l, face_r = centre_of(final["TailLampFace_L"]), centre_of(final["TailLampFace_R"])
    flo, fhi = mesh_box(final["TailLampFace_L"])
    say(f"  tail lamp faces {fhi.z - flo.z:.3f} m across, centres {face_l.x - face_r.x:.3f} m apart, {face_l.z:.3f} m "
        f"over the ground, {hi.y - face_l.y:.3f} m ahead of the tail")
    # The orientation the entry's "forward": "+z" and the preparation's left (+x facing +z) rely on.
    wrong = []
    if not (tyres["FL"][0].y < 0 < tyres["RL"][0].y and tyres["FL"][0].x > 0 > tyres["FR"][0].x):
        wrong.append("the tyres' corners")
    if not centre_of(final["HeadlampCovers"]).y < 0 < face_l.y:
        wrong.append("the headlamps ahead of the tail lamps")
    if abs(wb - args.wheelbase) > 0.001:
        wrong.append(f"the wheelbase {wb:.4f}")
    if min(t[3] for t in tyres.values()) < -1e-4:
        wrong.append("the tyres under the ground")
    if wrong:
        raise SystemExit(f"the result is not set up as the entry needs it: {wrong}")
    say("")
    say("triangles by part (before: the file's viewport levels; after: this conversion):")
    for name in sorted(final):
        parts = origin[name]
        was = sum(before[p] for p in parts)
        now = triangles(final[name])
        mats = sorted(face_materials(final[name]))
        lo_p, hi_p = mesh_box(final[name])
        c = (lo_p + hi_p) / 2
        say(f"  {name:26s} {was:7,d} -> {now:7,d}  centre ({c.x:+.3f} {c.y:+.3f} {c.z:+.3f}) size ({hi_p.x - lo_p.x:.3f} "
            f"{hi_p.y - lo_p.y:.3f} {hi_p.z - lo_p.z:.3f})  {mats}  <- {', '.join(parts) if len(parts) <= 4 else f'{len(parts)} parts'}")
    say(f"  total {sum(before.values()):,} -> {total:,}")
    say(f"decimated: {', '.join(f'{k} {a:,} -> {b:,}' for k, (a, b) in decimated.items())}")
    say("")
    say("materials (linear base, metallic, roughness, transmission, alpha):")
    for name in used:
        spec = MATERIALS[name]
        say(f"  {name:30s} {tuple(spec['base'])} m {spec['m']} r {spec['r']} t {spec.get('t', 0.0)} a {spec.get('a', 1.0)}"
            + (f" coat {spec['coat']}" if 'coat' in spec else "") + (f" sheen {spec['sheen']}" if 'sheen' in spec else ""))

    # The exporter's Y-up conversion turns Blender's -Y nose into glTF +Z and keeps +X (the car's left) as +X. No Draco
    # or meshopt: tools/car_models.py reads plain accessors. Vertex colours only where a material reads them (none).
    out = os.path.abspath(args.out)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_cameras=False, export_lights=False, export_animations=False, export_extras=False,
                              export_skins=False, export_morph=False, export_materials="EXPORT", export_normals=True,
                              export_vertex_color="MATERIAL", export_draco_mesh_compression_enable=False,
                              export_image_format="AUTO")
    doc, blob = read_glb(out)
    say("")
    say(f"exported {os.path.basename(out)}: {os.path.getsize(out):,} bytes, SHA-256 {sha256(out)}")
    say(f"  {doc.get('asset', {}).get('generator')}; nodes {len(doc.get('nodes', []))}, meshes {len(doc.get('meshes', []))}, "
        f"materials {len(doc.get('materials', []))}, images {len(doc.get('images', []))}; extensionsUsed "
        f"{doc.get('extensionsUsed', [])}, extensionsRequired {doc.get('extensionsRequired', [])}")
    for material in doc.get("materials", []):
        if "alphaMode" in material or material.get("extensions"):
            say(f"  {material['name']}: alphaMode {material.get('alphaMode', 'OPAQUE')}, extensions "
                f"{sorted(material.get('extensions', {}))}")
    check_written(doc, blob, say, set(used))
    say.write(args.report)


main()
