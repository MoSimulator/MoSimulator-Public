# Robot script templates

In the Project window, choose the folder for your new robot script, then use
**Create > MoSimulator > RebuiltTemplate**, **RebuiltTurretTemplate**, or
**ReefscapeTemplate**. Enter the robot's class/file name when Unity prompts you.
The same commands are available under **Assets > Create > MoSimulator**.

Unity fills in the class name and the destination assembly's root namespace.
The `.cs.txt` files are source templates; edit them to change future scripts.
The menu registration is in `Assets/Editor/RobotScriptTemplates.cs`.

- **RebuiltTemplate** inherits `RebuiltRobotBase` and follows CompetitiveConcept's
  field sections, scheduler registration, and mechanism method signatures.
- **RebuiltTurretTemplate** inherits `RebuiltTurretRobot`. Its `base.Init()` call
  registers the inherited SWM, zone, and shooter dispatch. Assign the inherited
  `swmRef` (with a parent) and `swmSettings` fields, then implement the provided
  overrides. `Aligned()` returns false until you supply an alignment check.
- **ReefscapeTemplate** inherits `ReefscapeCommandRobotBase` and follows
  CommandLynk's initialization, setpoints, game-piece methods, and state callbacks.
  Creating `MyRobot.cs` also creates `MyRobotSetpoint.cs` in the same folder and
  namespace. The companion model inherits `ScriptableObject`, following
  `LynkSetpoint`; nested structs do not serialize correctly through the mod build
  pipeline. Extend that separate model with your mechanism targets, then use
  **Assets > Create > Games > Reefscape > &lt;ModpackName&gt; > MyRobotSetpoint** (the modpack name is the folder under `Mods/`; "Robot" if the script is elsewhere) to create the setpoint assets and
  assign them to the robot's serialized fields. Keep both scripts with the mod.

These are scaffolds. Fill in the TODOs, add your robot's serialized hardware and
tuning fields, and assign them on its prefab before expecting mechanism behavior.
The scheduler owns updates; add work through its callbacks rather than adding
Unity `Start`, `Update`, or `FixedUpdate` methods to these scripts.
