// ============================================================================
//  Pisces — front control panel   (parametric, vertical layout)
//
//  OpenSCAD 2023+ with the Manifold backend (default in current builds, or
//  Preferences -> Features -> manifold). Renders in ~1 s; tweak everything from
//  the Customizer panel (Window -> Customizer).
//
//      +-- TFT --+   (O) SELECT
//      +- OLED0 -+   (O) 1
//      +- OLED1 -+   (O) 2
//      +- OLED2 -+   (O) 3
//      +- OLED3 -+   (O) 4
//      [BYP] [   ]   [P-] [P+]
//
//  The FRONT face (labels, chamfers, countersinks) is +Z / up — the default
//  view shows it reading correctly. For printing, flip it face-down in the
//  slicer for the smoothest cosmetic surface, or leave it face-up for the
//  crispest engraving. Bushings fit from the front with their nuts; the
//  displays mount behind the panel (double-sided tape, or M2 screws into the
//  optional back bosses). Measure your modules and set the *_win / *_body /
//  *_hole_pitch values — they vary a lot between vendors.
// ============================================================================

/* [Output] */
// what to render
part = "panel";                     // [panel, coupon, backplate]
// engrave / emboss the control labels
show_labels = true;
// split into two printable halves (only if it won't fit your bed in one piece)
auto_split = false;
// tint parts in the F5 preview
preview_colour = true;

/* [Fabrication] */
// printer bed size (for the split check + build report)
bed = [220, 220];                   // [120:10:400]
panel_t = 3;                        // [2:0.5:6]
// outer corner radius
corner_r = 5;                       // [0:0.5:15]
// M3 frame-mounting holes at the corners
corner_screws = true;
corner_hole_d = 3.4;                // [2.6:0.1:5]
// flat-head countersink depth (0 = plain through hole)
corner_csk = 1.6;                   // [0:0.2:4]

/* [Layout] */
panel_w = 92;                       // [60:1:160]
margin_top = 10;                    // [4:1:30]
margin_bottom = 9;                  // [4:1:30]
margin_side = 6;                    // [4:1:20]
// gap below the TFT before the OLED stack
tft_gap = 8;                        // [0:1:30]
// clear space between stacked OLED rows
oled_row_gap = 6;                   // [0:1:20]
// physical knob diameter (sets vertical spacing so knobs don't touch)
knob_dia = 20;                      // [10:1:40]

/* [Labels] */
label_size = 3.4;                   // [2:0.1:8]
label_style = "recessed";           // [recessed, raised]
label_depth = 0.7;                  // [0.3:0.1:1.5]
label_font = "Liberation Sans:style=Bold";

/* [Rotary encoder] */
enc_hole_d = 7.2;                   // [5:0.1:12]
// anti-rotation locating pin
enc_lug = true;
enc_lug_d = 2.9;                    // [1.5:0.1:5]
enc_lug_r = 6.9;                    // [3:0.1:12]
// 0 = 3 o'clock (points away from the OLED)
enc_lug_angle = 0;                  // [0:15:345]

/* [Toggle switch] */
tog_hole_d = 6.2;                   // [4:0.1:12]
tog_keyway = true;
tog_key_w = 1.8;                    // [1:0.1:3]
tog_key_depth = 1.6;               // [0.5:0.1:4]
tog_key_angle = 90;                // [0:15:345]

/* [Momentary button] */
btn_hole_d = 12.2;                  // [6:0.1:20]

/* [OLED - SSD1306 0.96in : MEASURE YOURS] */
// visible window = glass active area + a hair
oled_win = [25.5, 14.5];
// window centre offset UP from the module centre (ribbon at the bottom)
oled_win_yoff = 1.5;               // [-6:0.1:6]
// module PCB outline (for row spacing only)
oled_body = [27.8, 27.8];
// 45-deg lead-in chamfer on the front edge of the window
oled_chamfer = 1.0;                // [0:0.1:3]
// cut 4 corner mounting holes
oled_mount_holes = true;
// centre-to-centre spacing of the mounting holes [x, y]
oled_hole_pitch = [23.5, 23.5];
// mounting hole diameter (2.4 = M2 clearance, 1.7 = M2 self-tap)
oled_hole_d = 2.4;                 // [1.5:0.1:4]
// add screw bosses off the back so the module sits behind the panel
oled_screw_mount = false;
// boss height behind the panel
oled_standoff = 2.5;               // [1:0.5:10]

/* [TFT - 2.0in ST7789 : MEASURE YOURS] */
tft_win = [31, 41];
// window centre offset UP from the module centre
tft_win_yoff = 0;                  // [-8:0.1:8]
tft_body = [45, 53];
tft_chamfer = 1.0;                 // [0:0.1:3]
tft_mount_holes = true;
tft_hole_pitch = [40, 48];
tft_hole_d = 2.6;                  // [1.5:0.1:4]
tft_screw_mount = false;
tft_standoff = 2.5;                // [1:0.5:10]

/* [Split] */
split_pin_d = 3.2;                 // [2:0.1:5]
split_pin_count = 3;               // [2:1:6]

/* [Hidden] */
$fa = 1;
$fs = 0.35;
eps = 0.02;
n_oled = 4;

// ============================================================================
//  DERIVED LAYOUT   (origin bottom-left, Y up)
// ============================================================================
row_pitch  = max(oled_body[1], knob_dia) + oled_row_gap;
bot_row_h  = max(btn_hole_d, tog_hole_d) + 2 * label_size + 10;

panel_h = margin_top + tft_body[1] + tft_gap
        + n_oled * row_pitch + bot_row_h + margin_bottom;

oled_cx = margin_side + max(oled_win[0], oled_body[0]) / 2;
enc_cx  = panel_w - margin_side - knob_dia / 2;
tft_cx  = panel_w / 2;

tft_cy     = panel_h - margin_top - tft_body[1] / 2;
oled_top_y = tft_cy - tft_body[1] / 2 - tft_gap - row_pitch / 2;
oled_c     = [ for (i = [0 : n_oled - 1]) [ oled_cx, oled_top_y - i * row_pitch ] ];
enc_c      = [ for (i = [0 : n_oled - 1]) [ enc_cx,  oled_c[i][1] ] ];
sel_c      = [ enc_cx, tft_cy ];

bot_y  = margin_bottom + bot_row_h / 2;
tog_c  = [ [ panel_w * 0.20, bot_y ], [ panel_w * 0.40, bot_y ] ];
btn_c  = [ [ panel_w * 0.64, bot_y ], [ panel_w * 0.84, bot_y ] ];

// split seam: the clear gap between two OLED rows nearest the panel centre,
// so the cut never runs through a window or a hole
gap_ys = [ for (i = [0 : n_oled - 2]) (oled_c[i][1] + oled_c[i + 1][1]) / 2 ];
seam_y = gap_ys[ search(min([ for (g = gap_ys) abs(g - panel_h / 2) ]),
                        [ for (g = gap_ys) abs(g - panel_h / 2) ])[0] ];

// [ x, y, text, size-multiplier, side ]   side: +1 label above, -1 below
labels = [
    [ tft_cx,      panel_h - margin_top / 2 - 1,           "PISCES", 1.5, -1 ],
    [ sel_c[0],    sel_c[1] + knob_dia/2 + label_size + 1, "SELECT", 1.0, +1 ],
    [ enc_c[0][0], enc_c[0][1] + knob_dia/2 + label_size,  "1", 1.0, +1 ],
    [ enc_c[1][0], enc_c[1][1] + knob_dia/2 + label_size,  "2", 1.0, +1 ],
    [ enc_c[2][0], enc_c[2][1] + knob_dia/2 + label_size,  "3", 1.0, +1 ],
    [ enc_c[3][0], enc_c[3][1] + knob_dia/2 + label_size,  "4", 1.0, +1 ],
    [ tog_c[0][0], bot_y + tog_hole_d/2 + label_size,      "BYPASS", 0.9, +1 ],
    [ btn_c[0][0], bot_y + btn_hole_d/2 + label_size,      "PATCH-", 0.85, +1 ],
    [ btn_c[1][0], bot_y + btn_hole_d/2 + label_size,      "PATCH+", 0.85, +1 ],
];

// true if any of the 4 corner mount holes would overlap the display window rect
function holes_hit_window(pitch, hole_d, win, yoff) =
    (pitch[0]/2 < win[0]/2 + hole_d/2) && (abs(pitch[1]/2 - yoff) < win[1]/2 + hole_d/2);

// ============================================================================
//  VALIDATION
// ============================================================================
assert(oled_win[0] < oled_body[0] && oled_win[1] < oled_body[1],
       "OLED window must be smaller than the module body");
assert(tft_win[0] < tft_body[0] && tft_win[1] < tft_body[1],
       "TFT window must be smaller than the module body");
assert(panel_w - 2*margin_side > oled_body[0] + knob_dia,
       "panel_w too narrow for an OLED + a knob on one row");
assert(max(oled_chamfer, tft_chamfer) < panel_t,
       "window chamfer cannot exceed panel thickness");
assert(!enc_lug || enc_lug_r > (enc_hole_d + enc_lug_d) / 2 - 0.3
       || enc_lug_r < (enc_hole_d - enc_lug_d) / 2,
       "enc_lug_r lands on the bushing edge — move it in or out");
assert(!oled_mount_holes || !holes_hit_window(oled_hole_pitch, oled_hole_d, oled_win, oled_win_yoff),
       "OLED mounting holes clip the window — widen oled_hole_pitch or shrink oled_win");
assert(!tft_mount_holes || !holes_hit_window(tft_hole_pitch, tft_hole_d, tft_win, tft_win_yoff),
       "TFT mounting holes clip the window — widen tft_hole_pitch or shrink tft_win");

too_tall  = panel_h > bed[1] || panel_w > bed[0];
will_split = auto_split && too_tall;
fit_note  = will_split ? str("SPLIT at y=", seam_y, " -> halves ", panel_w, " x ",
                             max(seam_y, panel_h - seam_y), " mm")
          : too_tall   ? "DOES NOT FIT the bed - enable auto_split or shrink the layout"
          :              "fits the bed";
if (part == "panel")
    echo(str("Pisces panel: ", panel_w, " x ", panel_h, " x ", panel_t,
             " mm  (", fit_note, ")"));

// ============================================================================
//  2D PRIMITIVES
// ============================================================================
module rrect(w, h, r) offset(r = min(r, w/2, h/2)) offset(delta = -min(r, w/2, h/2))
    square([w, h], center = true);

module encoder_2d() {
    circle(d = enc_hole_d);
    if (enc_lug)
        translate(enc_lug_r * [cos(enc_lug_angle), sin(enc_lug_angle)])
            circle(d = enc_lug_d);
}

module toggle_2d() {
    circle(d = tog_hole_d);
    if (tog_keyway)
        rotate(tog_key_angle - 90)
            translate([-tog_key_w/2, -tog_hole_d/4])
                square([tog_key_w, tog_hole_d * 0.75 + tog_key_depth]);
}

module controls_2d() {
    translate(sel_c) encoder_2d();
    for (c = enc_c) translate(c) encoder_2d();
    for (c = tog_c) translate(c) toggle_2d();
    for (c = btn_c) translate(c) circle(d = btn_hole_d);
}

module plate_2d() {
    difference() {
        translate([panel_w/2, panel_h/2]) rrect(panel_w, panel_h, corner_r);
        if (corner_screws)
            for (x = [corner_r + 3, panel_w - corner_r - 3],
                 y = [corner_r + 3, panel_h - corner_r - 3])
                translate([x, y]) circle(d = corner_hole_d);
    }
}

// ============================================================================
//  3D FEATURES
//  Convention: the FRONT face (the one you look at, with the labels) is the
//  +Z face at z = panel_t. Features are cut DOWNWARD from it. This makes the
//  default OpenSCAD view show the front, reading correctly. Print face-down
//  (flip in the slicer) for the smoothest cosmetic surface, or face-up for the
//  crispest engraving.
// ============================================================================

// display window through the panel + 4 corner mounting holes, with a 45-deg
// lead-in chamfer on the FRONT (+Z) face. SUBTRACTED from the plate.
//   c       module centre [x, y]        yoff    window offset up from centre
//   win     [w, h] visible window       chamfer front lead-in (0 = none)
//   holes   true to cut mount holes     pitch   [x, y] hole spacing
//   hole_d  mount hole diameter
module display_cut(c, win, yoff, chamfer, holes, pitch, hole_d) {
    translate([c[0], c[1], 0]) {
        // through window
        translate([0, yoff, -eps]) linear_extrude(panel_t + 2*eps)
            square(win, center = true);
        // front lead-in chamfer, widening toward +Z
        if (chamfer > 0)
            translate([0, yoff, panel_t - chamfer]) hull() {
                linear_extrude(eps) square(win, center = true);
                translate([0, 0, chamfer + eps])
                    linear_extrude(eps) square(win + 2*[chamfer, chamfer], center = true);
            }
        // corner mounting holes
        if (holes)
            for (sx = [-1, 1], sy = [-1, 1])
                translate([sx*pitch[0]/2, sy*pitch[1]/2, -eps])
                    cylinder(h = panel_t + 2*eps, d = hole_d);
    }
}

// M2 screw bosses standing off the BACK (-Z) face for a screw-mounted module.
module display_bosses(c, pitch, hole_d, standoff) {
    for (sx = [-1, 1], sy = [-1, 1])
        translate([c[0] + sx*pitch[0]/2, c[1] + sy*pitch[1]/2, 0])
            difference() {
                union() {
                    translate([0, 0, -standoff]) cylinder(h = standoff + eps, d = hole_d + 3.5);
                    cylinder(h = 1.2, d1 = hole_d + 6, d2 = hole_d + 3.5);   // skirt
                }
                translate([0, 0, -standoff - eps]) cylinder(h = standoff + 3, d = hole_d);
            }
}

// flat-head countersinks on the FRONT (+Z) face, widening toward +Z
module corner_countersinks() {
    if (corner_screws && corner_csk > 0)
        for (x = [corner_r + 3, panel_w - corner_r - 3],
             y = [corner_r + 3, panel_h - corner_r - 3])
            translate([x, y, panel_t - corner_csk])
                cylinder(h = corner_csk + eps,
                         d1 = corner_hole_d, d2 = corner_hole_d + 2*corner_csk);
}

module split_pins() {
    if (part == "panel" && will_split)
        for (i = [1 : split_pin_count])
            translate([panel_w * i / (split_pin_count + 1), seam_y, panel_t/2])
                rotate([90, 0, 0]) cylinder(d = split_pin_d, h = 30, center = true);
}

// Control labels on the FRONT (+Z) face. "recessed" is engraved (subtracted);
// "raised" stands proud of the front. Read correctly from +Z, no mirror needed.
module engrave() {
    if (show_labels)
        for (l = labels) {
            sz  = label_size * l[3];
            // shrink so a label can never overrun the panel width
            fit = min(1, (panel_w - 2) / (len(l[2]) * sz * 0.62 + 0.01));
            z0  = label_style == "raised" ? panel_t - eps : panel_t - label_depth;
            hh  = label_style == "raised" ? label_depth + eps : label_depth + 0.5;
            translate([l[0], l[1], z0])
                linear_extrude(hh)
                    text(l[2], size = sz * fit, halign = "center", valign = "center",
                         font = label_font);
        }
}

// ============================================================================
//  ASSEMBLY
// ============================================================================
module panel_solid() {
    difference() {
        union() {
            linear_extrude(panel_t) plate_2d();
            if (oled_screw_mount)
                for (c = oled_c)
                    display_bosses(c, oled_hole_pitch, oled_hole_d, oled_standoff);
            if (tft_screw_mount)
                display_bosses([tft_cx, tft_cy], tft_hole_pitch, tft_hole_d, tft_standoff);
        }
        translate([0, 0, -eps]) linear_extrude(panel_t + 2*eps) controls_2d();
        for (c = oled_c)
            display_cut(c, oled_win, oled_win_yoff, oled_chamfer,
                        oled_mount_holes, oled_hole_pitch, oled_hole_d);
        display_cut([tft_cx, tft_cy], tft_win, tft_win_yoff, tft_chamfer,
                    tft_mount_holes, tft_hole_pitch, tft_hole_d);
        corner_countersinks();
        split_pins();
        if (label_style == "recessed") engrave();
    }
    if (show_labels && label_style == "raised") engrave();
}

module panel() {
    if (preview_colour && $preview) color("#3a6ea5") panel_solid();
    else panel_solid();
}

module half(bottom) intersection() {
    panel();
    h = panel_t + max(oled_standoff, tft_standoff) + 10;
    if (bottom) translate([-1, -1, -h/2])     cube([panel_w + 2, seam_y + 1, h]);
    else        translate([-1, seam_y, -h/2]) cube([panel_w + 2, panel_h - seam_y + 1, h]);
}

// thin back plate that clamps all the displays in from behind
module backplate() {
    bp_t = 2;
    difference() {
        linear_extrude(bp_t) offset(-1) plate_2d();
        // clear the encoder bodies + wiring
        translate([0, 0, -eps]) linear_extrude(bp_t + 2*eps) {
            translate(sel_c) circle(d = 14);
            for (c = enc_c) translate(c) circle(d = 14);
            for (c = tog_c) translate(c) circle(d = 12);
            for (c = btn_c) translate(c) circle(d = 16);
        }
        // windows onto the display PCBs so connectors/ribbons pass
        for (c = oled_c) translate([c[0], c[1], -eps])
            linear_extrude(bp_t + 2*eps) square(oled_body - [4, 4], center = true);
        translate([tft_cx, tft_cy, -eps])
            linear_extrude(bp_t + 2*eps) square(tft_body - [4, 4], center = true);
    }
}

// ============================================================================
module coupon() {
    difference() {
        linear_extrude(panel_t) square([80, 46]);
        translate([16, 30, -eps]) linear_extrude(panel_t + 2*eps) encoder_2d();
        translate([40, 30, -eps]) linear_extrude(panel_t + 2*eps) toggle_2d();
        translate([64, 30, -eps]) linear_extrude(panel_t + 2*eps) circle(d = btn_hole_d);
        display_cut([40, 14], oled_win, 0, oled_chamfer,
                    oled_mount_holes, oled_hole_pitch, oled_hole_d);
    }
}

if (part == "coupon") coupon();
else if (part == "backplate") backplate();
else if (will_split) {
    half(true);
    translate([panel_w + 12, -seam_y, 0]) half(false);
}
else panel();
