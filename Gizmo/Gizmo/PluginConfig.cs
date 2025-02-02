﻿using BepInEx.Configuration;
using UnityEngine;

namespace Gizmo {
  public static class PluginConfig {
    public static ConfigEntry<int> SnapDivisions { get; private set; }
    public static ConfigEntry<string> CustomSnapStages { get; private set; }
    public static ConfigEntry<int> CurrentSnapStage { get; private set; } // TODO can you make config entries that are stored in the config file but not binded? like, just a value that it remembers on the user's machine?
    public static ConfigEntry<int> GizmoOpacity { get; private set; }

    public static ConfigEntry<KeyboardShortcut> XRotationKey;
    public static ConfigEntry<KeyboardShortcut> ZRotationKey;
    public static ConfigEntry<KeyboardShortcut> ResetRotationKey;
    public static ConfigEntry<KeyboardShortcut> ResetAllRotationKey;
    public static ConfigEntry<KeyboardShortcut> ChangeRotationModeKey;
    public static ConfigEntry<KeyboardShortcut> CopyPieceRotationKey;
    public static ConfigEntry<KeyboardShortcut> SnapDivisionIncrementKey;
    public static ConfigEntry<KeyboardShortcut> SnapDivisionDecrementKey;

    public static ConfigEntry<bool> UseCustomSnapStages;
    public static ConfigEntry<bool> ShowGizmoPrefab;
    public static ConfigEntry<bool> ResetRotationOnModeChange;
    public static ConfigEntry<bool> ResetRotationOnSnapDivisionChange;
    public static ConfigEntry<bool> NewGizmoRotation;

    public static int MaxSnapDivisions = 256;
    public static int MinSnapDivisions = 1;

    public static int MaxCustomStages = 360;
    public static int MaxCustomSnapDivisions = 1000000000;

    public static void BindConfig(ConfigFile config) {
      SnapDivisions =
          config.Bind(
              "Gizmo",
              "snapDivisions",
              16,
              new ConfigDescription(
              "Number of snap angles per 180 degrees. Vanilla uses 8.",
              new AcceptableValueRange<int>(MinSnapDivisions, MaxSnapDivisions)));

      UseCustomSnapStages =
          config.Bind(
              "Gizmo",
              "useCustomSnapStages",
              false,
              "Enable use of custom snap division stages to move through with the increment & decrement hotkeys.");

      CustomSnapStages =
          config.Bind(
              "Gizmo",
              "userCustomSnapStages",
              "6, 10, 36, 360, 100, 1",
              "Numbers are separated by commas and spaces are ignored. Click 'reset' for formatting and usage examples. Disabled if empty or 0.");
            
      GizmoOpacity =
          config.Bind(
              "UI",
              "gizmoOpacity",
              100,
              new ConfigDescription(
              "Set the opacity of the gizmo. 0 is transparent, 100 is fully opaque.",
              new AcceptableValueRange<int>(0, 100)));
              
      XRotationKey =
          config.Bind(
              "Keys",
              "xRotationKey",
              new KeyboardShortcut(KeyCode.LeftShift),
              "Hold this key to rotate on the x-axis/plane (red circle).");

      ZRotationKey =
          config.Bind(
              "Keys",
              "zRotationKey",
              new KeyboardShortcut(KeyCode.LeftAlt),
              "Hold this key to rotate on the z-axis/plane (blue circle).");

      ResetRotationKey =
          config.Bind(
              "Keys",
              "resetRotationKey",
              new KeyboardShortcut(KeyCode.V),
              "Press this key to reset the selected axis to zero rotation.");

      ResetAllRotationKey =
          config.Bind(
              "Keys",
              "resetAllRotationKey",
              KeyboardShortcut.Empty,
              "Press this key to reset _all axis_ rotations to zero rotation.");


      ChangeRotationModeKey =
          config.Bind(
              "Keys",
              "changeRotationMode",
              new KeyboardShortcut(KeyCode.BackQuote),
              "Press this key to toggle rotation modes.");

      CopyPieceRotationKey =
          config.Bind(
              "Keys",
              "copyPieceRotation",
              KeyboardShortcut.Empty,
              "Press this key to copy targeted piece's rotation.");

      SnapDivisionIncrementKey =
          config.Bind(
              "Keys",
              "snapDivisionIncrement",
              new KeyboardShortcut(KeyCode.PageUp),
              "Shift to the next snap division stage. By default this doubles snap divisions from current.");

      SnapDivisionDecrementKey =
          config.Bind(
              "Keys",
              "snapDivisionDecrement",
              new KeyboardShortcut(KeyCode.PageDown),
              "Shift to the previous snap division stage. By default this halves snap divisions from current.");

      ShowGizmoPrefab = config.Bind("UI", "showGizmoPrefab", true, "Show the Gizmo prefab in placement mode.");

      ResetRotationOnSnapDivisionChange = config.Bind("Reset", "resetOnSnapDivisionChange", true, "Resets the piece's rotation on snap division change.");
      ResetRotationOnModeChange = config.Bind("Reset", "resetOnModeChange", true, "Resets the piece's rotation on mode switch.");

      NewGizmoRotation = config.Bind("Rotation Mode", "newGizmoRotation", false, "Enables post Gizmo v1.2.0 rotation scheme. Restart required for changes to take effect.");
    }
  }
}
