
# vehicle enum actions

all of these enums can be passed to the same function call the current VehicleEngine.MainIgnite/MainShutdown are passed.


```
public enum VehicleEngine{  MainIgnite,  MainShutdown,}

public enum FlightComputerAction{  [XmlEnum("None")] None,  [XmlEnum("DeleteNextBurn")] DeleteNextBurn,  [XmlEnum("WarpToNextBurn")] WarpToNextBurn,}

public enum FlightComputerAttitudeProfile{  Strict,  Balanced,  Relaxed,}

public enum FlightComputerAttitudeTrackTarget{  None,  Custom,  Forward,  Backward,  Up,  Down,  Ahead,  Behind,  RadialOut,  RadialIn,  Prograde,  Retrograde,  Normal,  AntiNormal,  Outward,  Inward,  PositiveDv,  NegativeDv,  Toward,  Away,  Antivel,  Align,}

public enum VehicleReferenceFrame{  [XmlEnum("EclBody")] EclBody,  [XmlEnum("EnuBody")] EnuBody,  [XmlEnum("Lvlh")] Lvlh,  [XmlEnum("VlfBody")] VlfBody,  [XmlEnum("BurnBody")] BurnBody,  [XmlEnum("Tgt")] Dock,}

public enum FlightComputerBurnMode{  Manual,  Auto,}

public enum FlightComputerRollMode{  Decoupled,  Up,  Down,}

public enum FlightComputerAttitudeMode{  Manual,  Auto,}

public enum FlightComputerManualThrustMode{  Direct,  Pulse,}
```


# global actions

// toggle FPS display
GameSettings.Current.Interface.FrameStatistics = !GameSettings.Current.Interface.FrameStatistics;

// toggle UI (show/hide UI stuff)
Program.DrawUI = !Program.DrawUI;

// self-explanatory actions
Universe.IncreaseSimulationSpeed();
Universe.DecreaseSimulationSpeed();
Universe.ResetSimulationSpeed();
  Universe.SeekNextVehicle(SeekDirection.Forward);
Universe.SeekNextVehicle(SeekDirection.Backward);
Universe.SeekNextCelestial(SeekDirection.Forward);
Universe.SeekNextCelestial(SeekDirection.Backward);
