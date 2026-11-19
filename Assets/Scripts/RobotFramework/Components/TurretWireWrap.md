# Turret wire wrap

For mechanisms other than turrets, use **Robot Framework → Energy Chain Run**.
It shares the `EnergyChainMesh` link renderer, but follows an ordered list of
Transform markers instead of solving turret wrapping. Assign a material and use
**Create straight run** or **Create U-loop run**, then position the markers.
Parent the moving endpoint to the mechanism it follows. Moving route markers
update the generated links in edit mode and play mode.

The four-marker U-loop preset enables **Follow Moving End**: its two return-bend
markers receive half the moving end's displacement along **Slide Axis**. Keep
these bend markers under the stationary run. **Moving End At Rest** stores the
setup position in reference-frame coordinates. This is intended for linear
sliders and elevators; disable it for manually routed arms or other layouts.
Corner radius, chain width/height, link length, gap and material are configurable.
The route rounds marker corners and does not solve collisions, tension or total
chain length. A U-turn needs two bend markers, rather than a single 180° corner.

`TurretWireWrap` works with any robot using `GenericTurret`. It reads the absolute,
multi-turn `GenericTurret.CurrentAngle()` in `LateUpdate`, generates rectangular
energy-chain links along a planar wrap and rounded return, and moves a configured slider.
Reversing turret rotation reverses the animation without accumulating drift.

To install on a robot:

1. Add **Robot Framework → Turret Energy Chain** to a stationary child object.
2. Assign the robot's `GenericTurret`, a stationary reference frame, the chain
   attachment transform on the rotating turret, and the slider transform.
3. Assign `Chain Material`, then click **Create chain renderer**. An existing
   renderer can also be assigned to `Wire` instead of creating one.
4. At the turret's rest position, click **Capture mount and slider positions** to
   initialize the wrap center, radius and slider rest position from those references.
5. Set the rotation/slide axes, fixed guide center and radius, stationary feed
   position, travel limits and chain dimensions to match that robot. These are
   Inspector settings; there are no model names or asset paths in the setup.
6. Disable any existing static chain renderers that this animated chain replaces.

Select the component and use **Preview angle (degrees) → Preview wrap**.
Use **Restore zero angle** before saving the prefab. A preview changes the wire
and slider and rotates the physical turret together with its mount. Restore zero
before saving or changing selection. Undo supports preview edits.

All coordinates and distances use `Stationary Frame` local units. This frame
must stay fixed to the robot, outside the rotating turret hierarchy. Slider
position is absolute from `Slider Rest Position`, so robot movement and repeated
enable/disable cycles do not change its rest position.

- `Center`, `Rotation Axis`, `Radius`: the turret wrap circle.
- `Route Along Guide`: routes from the feed side of the turret through a rounded
  base corner, up the inner guide lane, around the guide and back down the outer
  lane. Enabled by default. The bend faces away from the fixed feed, independently
  of the sign of `Slide Axis`.
- `Departure Direction`: the radial exit direction when `Route Along Guide` is off.
- `Initial Wrap Degrees`: requested starting wrap. With guide routing enabled,
  the starting angle is aligned to the actual attachment and the equivalent
  winding is chosen to leave toward the guide.
- `Angle Multiplier`: normally +1; use -1 for opposite winding.
- `Layer Spacing`: radial separation per full turn; zero makes a circular wrap.
- `Reverse Bend Radius`: the return bend around the rotating mount when the
  chain crosses its fully unwound position. Reverse winding uses an outer track
  offset by twice this bend radius, rather than simply reversing the original
  spiral. `Reverse Transition Degrees` controls how gradually that bend opens
  across zero wrap. The extra bend and outer-track length also drive take-up.
  Reverse wrapping computes its own departure and approach angle from the common
  tangent between the outer coil and the lower fixed guide. Before the mount
  passes that contact point, the mount return bend connects directly to the guide;
  it does not visit the positive-wrap departure point first.
- `Guide Offset`, `Bend Radius`, `Lead Length`: the U-shaped return at the slider.
- `Use Fixed Guide`: replace the automatic lower corner with a circular guide.
  Requires `Route Along Guide`. Set `Fixed Guide Center` in stationary frame local
  coordinates and `Fixed Guide Radius` to half the desired cable path diameter.
  This radius is independent of the upper return's `Bend Radius`. The cable follows
  tangent runs around the selected circle; if a run endpoint is inside the circle,
  the Inspector warns and the automatic corner is used instead. Include cable
  clearance in the radius. These settings change the cable path, not the CAD mesh.
- `Fixed Feed Position`: the stationary cable end.
- `Travel Per Cable Length`: 0.5 for the two runs around a take-up guide.
- `Travel Limits`: signed slider displacement from rest. The Inspector reports
  when travel is saturated. `Maximum Visual Turns` bounds geometry generation.
- `Chain Width`, `Chain Height`: outer dimensions of the rectangular energy-chain
  links in stationary-frame units. Width lies across the path; height follows the
  turret axis. `Chain Link Length` sets link spacing and `Chain Link Gap` controls
  the visible seams. `Chain Material` overrides the original wire material.
  The rectangular links are generated during preview and runtime. The original
  LineRenderer stores the path but its rendering is disabled.

This is a visual mechanism, not a cable physics solver. Slider travel derives
from wrapped spiral length; the curved free leads are not constrained to a fixed
total cable length. Travel limits and opposite-direction wrapping can therefore
require tuning for the robot's permitted turret range.

Runtime code, mesh generation and Inspector setup use only assigned references
and geometry settings. Robot-specific placement belongs in each robot's prefab.
