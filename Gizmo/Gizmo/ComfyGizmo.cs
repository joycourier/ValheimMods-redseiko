using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

using UnityEngine;

using static Gizmo.PluginConfig;

namespace Gizmo {
  [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
  public class ComfyGizmo : BaseUnityPlugin {
    public const string PluginGUID = "com.rolopogo.gizmo.comfy";
    public const string PluginName = "ComfyGizmo";
    public const string PluginVersion = "1.5.1";

    static GameObject _gizmoPrefab = null;
    static Transform _gizmoRoot;

    static Transform _xGizmo;
    static Transform _yGizmo;
    static Transform _zGizmo;

    static Transform _xGizmoRoot;
    static Transform _yGizmoRoot;
    static Transform _zGizmoRoot;

    static MeshRenderer _xRenderer = null;
    static MeshRenderer _yRenderer = null;
    static MeshRenderer _zRenderer = null;

    static GameObject _comfyGizmo;
    static Transform _comfyGizmoRoot;

    static Vector3 _eulerAngles;
    static float _rotation;

    static bool _localFrame;

    static float _snapAngle;

    static List<int> _customSnapStageList = new List<int>();
    static int _customListIndex;
    static int _previousCustomSnap;
    static bool _bStagesEnabled;

    Harmony _harmony;

    public void Awake() {
      BindConfig(Config);

      SnapDivisions.SettingChanged += (sender, eventArgs) => UpdateSnapDivision();

      UseCustomSnapStages.SettingChanged += (sender, eventArgs) => ConvertCustomSnapStagesToList();
      UseCustomSnapStages.SettingChanged += (sender, eventArgs) => ResetSnapDivision();

      CustomSnapStages.SettingChanged += (sender, eventArgs) => ConvertCustomSnapStagesToList();
      CustomSnapStages.SettingChanged += (sender, eventArgs) => ResetSnapDivision();

      _gizmoPrefab = LoadGizmoPrefab();
      
     
      _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), harmonyInstanceId: PluginGUID);

      ConvertCustomSnapStagesToList();
      ResetSnapDivision();
    }

    public void OnDestroy() {
      _harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(Game))]
    static class GamePatch {
      [HarmonyPostfix]
      [HarmonyPatch(nameof(Game.Start))]
      static void StartPostfix() {
        Destroy(_gizmoRoot);
        _gizmoRoot = CreateGizmoRoot();

        Destroy(_comfyGizmo);
        _comfyGizmo = new("ComfyGizmo");
        _comfyGizmoRoot = _comfyGizmo.transform;
        _localFrame = false;
      }
    }

    [HarmonyPatch(typeof(Player))]
    static class PlayerPatch {
      [HarmonyTranspiler]
      [HarmonyPatch(nameof(Player.UpdatePlacementGhost))]
      static IEnumerable<CodeInstruction> UpdatePlacementGhostTranspiler(IEnumerable<CodeInstruction> instructions) {
        if(NewGizmoRotation.Value) {
          return new CodeMatcher(instructions)
            .MatchForward(
                useEnd: false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Player), nameof(Player.m_placeRotation))),
                new CodeMatch(OpCodes.Conv_R4),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Ldc_R4),
                new CodeMatch(OpCodes.Call),
                new CodeMatch(OpCodes.Stloc_S))
            .Advance(offset: 5)
            .InsertAndAdvance(Transpilers.EmitDelegate<Func<Quaternion, Quaternion>>(_ => _comfyGizmoRoot.rotation))
            .InstructionEnumeration();
        } else {
          return new CodeMatcher(instructions)
          .MatchForward(
              useEnd: false,
              new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Player), nameof(Player.m_placeRotation))),
              new CodeMatch(OpCodes.Conv_R4),
              new CodeMatch(OpCodes.Mul),
              new CodeMatch(OpCodes.Ldc_R4),
              new CodeMatch(OpCodes.Call),
              new CodeMatch(OpCodes.Stloc_S))
          .Advance(offset: 5)
          .InsertAndAdvance(Transpilers.EmitDelegate<Func<Quaternion, Quaternion>>(_ => _xGizmoRoot.rotation))
          .InstructionEnumeration();
        }
      }

      [HarmonyPostfix]
      [HarmonyPatch(nameof(Player.UpdatePlacement))]
      static void UpdatePlacementPostfix(ref Player __instance, ref bool takeInput) {
        if(__instance.m_placementMarkerInstance) {
          _gizmoRoot.gameObject.SetActive(ShowGizmoPrefab.Value && __instance.m_placementMarkerInstance.activeSelf);
          _gizmoRoot.position = __instance.m_placementMarkerInstance.transform.position + (Vector3.up * 0.5f);
          if(_xRenderer == null || _yRenderer == null || _zRenderer == null) {
            AssignGizmoRenderers();
            ChangeGizmoShaders();
          } else { 
            SetGizmoOpacity();
          }
        }

        if(!__instance.m_buildPieces || !takeInput) {
          return;
        }

        if(Input.GetKeyDown(SnapDivisionIncrementKey.Value.MainKey)) {
          if(CustomSnapEnabled()) {
            CustomIncrement();
          } else {
            DefaultIncrement();
          }
            ResetRotationIfEnabled();
            return;
        }

        if(Input.GetKeyDown(SnapDivisionDecrementKey.Value.MainKey)) {
          if(CustomSnapEnabled()) {
            CustomDecrement();
          } else {
            DefaultDecrement();
          }
            ResetRotationIfEnabled();
            return;
        }

        if(Input.GetKey(CopyPieceRotationKey.Value.MainKey) && __instance.m_hoveringPiece != null) {
          MatchPieceRotation(__instance.m_hoveringPiece);
        }

        if(Input.GetKeyDown(ChangeRotationModeKey.Value.MainKey)) {
          ChangeRotationFrames();
          return;
        }

        _xGizmo.localScale = Vector3.one;
        _yGizmo.localScale = Vector3.one;
        _zGizmo.localScale = Vector3.one;

        if(!_localFrame) {
          Rotate();
        } else {
          RotateLocalFrame();
        }
      }
    }

    static void ChangeRotationFrames() { 
      if(_localFrame) {
        MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "Default rotation mode enabled");
        if (ResetRotationOnModeChange.Value) {
          ResetRotationsLocalFrame();
        } else {
          _eulerAngles = _comfyGizmo.transform.eulerAngles;
          ResetGizmoRoot();
          RotateGizmoComponents(_eulerAngles);
        }
      } else {
        MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "Local frame rotation mode enabled");
        if (ResetRotationOnModeChange.Value) {
          ResetRotations();
        } else {
          Quaternion currentRotation = _comfyGizmoRoot.rotation;
          ResetGizmoComponents();
          _gizmoRoot.rotation = currentRotation;
        }
      }
      _localFrame = !_localFrame;
    }

    static void CustomIncrement() { 
      if(_customListIndex < _customSnapStageList.Count() - 1) {
        _customListIndex += 1;
      } else { 
        _customListIndex = _customSnapStageList.Count() - 1;
      }
      UpdateValueCustom("increased");
    }

    static void CustomDecrement() { 
      if(_customListIndex > 0) {
        _customListIndex -= 1;
      } else { 
        _customListIndex = 0;
      }
      UpdateValueCustom("decreased");
    }

    static void UpdateValueCustom(string verb) { 
      if(_customListIndex >= _customSnapStageList.Count) { //failsafe
        ResetSnapDivision();
        return;
      }
      //int newSnap = _customSnapStageList[_customListIndex];
      if(_snapAngle != FixFloatingPointDecimals( 180f / _customSnapStageList[_customListIndex])) { // only send the message if something actually changes
        SendFancyCustomMessage(verb);
        //SnapDivisions.Value = newSnap;
        _snapAngle = FixFloatingPointDecimals( 180f / _customSnapStageList[_customListIndex]);
      }
    }

    static void SendFancyCustomMessage(string verb) { 
      if(MessageHud.instance != null) { // mod crashes when MessageHud is used during 'awake' because it only exists when in-game
        string text = "";
        for(int i = 0; i < _customSnapStageList.Count; i++) { 
          int snap = _customSnapStageList[i];
          if(i == _customListIndex) { 
            text += "[" + snap + "]";
          } else { 
            text += snap;
          }
          if (i != _customSnapStageList.Count - 1) { 
            text += " - ";
          }
        }
        if(text.Contains("[69]")) { text += " (nice)"; }
        MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, $"Snap stage {verb} to: {text}");
      }
    }

    static void DefaultIncrement() {
      if(SnapDivisions.Value * 2 <= MaxSnapDivisions) {
        MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, $"Snap divisions increased to {SnapDivisions.Value * 2}");
        SnapDivisions.Value = SnapDivisions.Value * 2;
      }
    }

    static void DefaultDecrement() { 
      if(Math.Floor(SnapDivisions.Value/2f) == SnapDivisions.Value/2f && SnapDivisions.Value/2 >= MinSnapDivisions) {
        MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, $"Snap divisions decreased to {SnapDivisions.Value / 2}");
        SnapDivisions.Value = SnapDivisions.Value / 2;
      }
    }

    static void ResetRotationIfEnabled() { 
      if(ResetRotationOnSnapDivisionChange.Value) {
        if(_localFrame) {
            ResetRotationsLocalFrame();
        } else {
            ResetRotations();
        }
      }
    }
    
    // only used to handle changes to SnapDivisions directly, best if it has no effect when custom stages are enabled
    static void UpdateSnapDivision() { 
      if(CustomSnapEnabled() == false) {
        _snapAngle = FixFloatingPointDecimals(180f / SnapDivisions.Value);
      }
    }

    static void ResetSnapDivision() { 
      if(CustomSnapEnabled()) {
        if(UpdateCustomIndex() == true) { 
          SendFancyCustomMessage("reset"); 
        }
        _snapAngle = FixFloatingPointDecimals( 180f / _customSnapStageList[_customListIndex] );
        _bStagesEnabled= true;
      } else {
        ResetSnapDefault();
        SendDefaultResetMessage();
      }
    }

    static bool UpdateCustomIndex() {
      if(_customListIndex >= 0 && _customListIndex < _customSnapStageList.Count) { // if the current index is within bounds
        if(_customSnapStageList[_customListIndex] == _previousCustomSnap) { 
          if (_bStagesEnabled == true) { 
            return false; // the current value is already accurate to the list, nothing needs to change and no messages need to be sent
          } else { 
            _bStagesEnabled = true;
            return true; 
          }
        } else { 
        return CurrentIndexWithinBounds(); 
        }
      } else if(_customListIndex >= _customSnapStageList.Count) {
        return CurrentIndexOutOfBounds();
      } else { 
        _customListIndex = 0; 
        return true; // index was in the negatives somehow... but there is still a value present in the list so, let's just use 0
      }
    }

      static bool CurrentIndexWithinBounds() { 
        if(_customListIndex + 1 <= _customSnapStageList.Count - 1) { // make sure we don't go out of bounds in the if statement below
          if(_customSnapStageList[_customListIndex + 1] == _previousCustomSnap) { 
            _customListIndex++;
            return true; // target number simply moved to the right, easy fix
          }
        }
        if(_customListIndex - 1 >= 0) { // again, making sure we don't go out of bounds
          if (_customSnapStageList[_customListIndex - 1] == _previousCustomSnap) { 
            _customListIndex--;
            return true; // target number simply moved to the left, easy fix
          } 
        }
        if(_customSnapStageList.Contains(_previousCustomSnap) == false) {
          return true; // target number is no longer in the list but we're still within bounds, whatever number is in the current stage will be the new number
        } else { 
          _customListIndex = _customSnapStageList.IndexOf(_previousCustomSnap); 
          return true; // out of bounds but the number IS in the new list somewhere else... do we use IndexOf and go to the first occurence? I guess that would make sense since this is a niche case
        }
      }

      static bool CurrentIndexOutOfBounds() { 
        if(_customListIndex - 1 <= _customSnapStageList.Count - 1) { // make sure we don't go out of bounds in the if statement below
          if (_customSnapStageList[_customListIndex - 1] == _previousCustomSnap) { 
            _customListIndex--;
            return false; // target number simply moved to the left, no message required
           }
        } 
        if(_customSnapStageList.Contains(_previousCustomSnap) == false) { 
          _customListIndex = _customSnapStageList.Count - 1; 
          return true; // target number is no longer in the list, reset to closest stage (which would be equal to Count-1 since we're currently out of bounds)  
        } else { 
          _customListIndex = _customSnapStageList.IndexOf(_previousCustomSnap); 
          return true; // out of bounds but the number IS in the new list somewhere else... do we use IndexOf and go to the first occurence? I guess that would make sense since this is a niche case
        }
      }

    static void ResetSnapDefault() { 
      // are we at any of the 'default' numbers like 8, 16, 32 etc? if we're not then reset to the default value (typically 16), if we are then no need to change anything
      /*
      if(GenerateDefaultSnapDivisionList().Contains(SnapDivisions.Value) == false) { 
        SnapDivisions.Value = (int)SnapDivisions.DefaultValue;
      } */
      _snapAngle = FixFloatingPointDecimals( 180f / SnapDivisions.Value );
    }

    static void SendDefaultResetMessage() { 
      if(MessageHud.instance != null) { 
        if(UseCustomSnapStages.Value == false && _bStagesEnabled == true) { 
          MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, $"Custom snap stages disabled, snap divisions reset to {SnapDivisions.Value}");
          _bStagesEnabled = false;
        } else if (_customSnapStageList.Any() == false) { 
          MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, $"No custom stages found, snap divisions reset to {SnapDivisions.Value}");
          if(UseCustomSnapStages.Value == true) { 
            _bStagesEnabled = true;
          }
        }
      }
    }

    static void Rotate() {
      if (Input.GetKey(ResetAllRotationKey.Value.MainKey)) {
        ResetRotations();
      } else if (Input.GetKey(XRotationKey.Value.MainKey)) {
        HandleAxisInput(ref _eulerAngles.x, _xGizmo);
      } else if (Input.GetKey(ZRotationKey.Value.MainKey)) {
        HandleAxisInput(ref _eulerAngles.z, _zGizmo);
      } else {
        HandleAxisInput(ref _eulerAngles.y, _yGizmo);
      }
      _comfyGizmo.transform.localRotation = Quaternion.Euler(_eulerAngles);
      RotateGizmoComponents(_eulerAngles);
    }

    static void RotateLocalFrame() {
      if (Input.GetKey(ResetAllRotationKey.Value.MainKey)) {
        ResetRotationsLocalFrame();
        return;
      }
      _rotation = 0f;
      Vector3 rotVector;

      if (Input.GetKey(XRotationKey.Value.MainKey)) {
        _xGizmo.localScale = Vector3.one * 1.5f;
        rotVector = Vector3.right;
        HandleAxisInputLocalFrame(ref _rotation, rotVector, _xGizmo);
      } else if (Input.GetKey(ZRotationKey.Value.MainKey)) {
        _zGizmo.localScale = Vector3.one * 1.5f;
        rotVector = Vector3.forward;
        HandleAxisInputLocalFrame(ref _rotation, rotVector, _zGizmo);
      } else {
        _yGizmo.localScale = Vector3.one * 1.5f;
        rotVector = Vector3.up;
        HandleAxisInputLocalFrame(ref _rotation, rotVector, _yGizmo);
      }
      RotateAxes(_rotation, rotVector);
    }

    static void RotateAxes(float rotation, Vector3 rotVector) {
      _comfyGizmo.transform.rotation *= Quaternion.AngleAxis(rotation, rotVector);
      _gizmoRoot.rotation *= Quaternion.AngleAxis(rotation, rotVector);
    }

    static void HandleAxisInput(ref float rotation, Transform gizmo) {
      gizmo.localScale = Vector3.one * 1.5f;
      rotation += Math.Sign(Input.GetAxis("Mouse ScrollWheel")) * _snapAngle;

      if (Input.GetKey(ResetRotationKey.Value.MainKey)) {
        rotation = 0f;
      }
    }

    static void HandleAxisInputLocalFrame(ref float rotation, Vector3 rotVector, Transform gizmo) {
      gizmo.localScale = Vector3.one * 5.5f;
      rotation = Math.Sign(Input.GetAxis("Mouse ScrollWheel")) * _snapAngle;

      if (Input.GetKey(ResetRotationKey.Value.MainKey)) {
        rotation = 0f;
        ResetRotationLocalFrameAxis(rotVector);
      }
    }

    static void MatchPieceRotation(Piece target) {
      if (_localFrame) {
        _comfyGizmo.transform.rotation = target.GetComponent<Transform>().localRotation;
        _gizmoRoot.rotation = target.GetComponent<Transform>().localRotation;
      } else {
        _eulerAngles = target.GetComponent<Transform>().eulerAngles;
        Rotate();
      }
    }

    static void ResetRotations() {
      _eulerAngles = Vector3.zero;
      _comfyGizmo.transform.localRotation = Quaternion.Euler(Vector3.zero);
      RotateGizmoComponents(Vector3.zero);
    }

    static void ResetGizmoComponents() {
      _eulerAngles = Vector3.zero;
      RotateGizmoComponents(Vector3.zero);
    }

    static void ResetGizmoRoot() {
      _gizmoRoot.rotation = Quaternion.AngleAxis(0f, Vector3.up);
      _gizmoRoot.rotation = Quaternion.AngleAxis(0f, Vector3.right);
      _gizmoRoot.rotation = Quaternion.AngleAxis(0f, Vector3.forward);
    }

    static void RotateGizmoComponents(Vector3 eulerAngles) {
      _xGizmoRoot.localRotation = Quaternion.Euler(eulerAngles.x, 0f, 0f);
      _yGizmoRoot.localRotation = Quaternion.Euler(0f, eulerAngles.y, 0f);
      _zGizmoRoot.localRotation = Quaternion.Euler(0f, 0f, eulerAngles.z);
    }

    static void ResetRotationsLocalFrame() {
      ResetRotationLocalFrameAxis(Vector3.up);
      ResetRotationLocalFrameAxis(Vector3.right);
      ResetRotationLocalFrameAxis(Vector3.forward);
    }

    static void ResetRotationLocalFrameAxis(Vector3 axis) {
      _comfyGizmo.transform.rotation = Quaternion.AngleAxis(0f, axis);
      _gizmoRoot.rotation = Quaternion.AngleAxis(0f, axis);
    }

    static GameObject LoadGizmoPrefab() {
      AssetBundle bundle = AssetBundle.LoadFromMemory(
          GetResource(Assembly.GetExecutingAssembly(), "Gizmo.Resources.gizmos"));

      GameObject prefab = bundle.LoadAsset<GameObject>("GizmoRoot");
      bundle.Unload(unloadAllLoadedObjects: false);

      return prefab;
    }

    static byte[] GetResource(Assembly assembly, string resourceName) {
      Stream stream = assembly.GetManifestResourceStream(resourceName);

      byte[] data = new byte[stream.Length];
      stream.Read(data, offset: 0, count: (int) stream.Length);

      return data;
    }

    static Transform CreateGizmoRoot() {
      _gizmoRoot = Instantiate(_gizmoPrefab).transform;

      // assigning the red, green and blue rings of the gizmo to separate transforms for resizing
      _xGizmo = _gizmoRoot.Find("YRoot/ZRoot/XRoot/X");
      _yGizmo = _gizmoRoot.Find("YRoot/Y");
      _zGizmo = _gizmoRoot.Find("YRoot/ZRoot/Z");

      _xGizmoRoot = _gizmoRoot.Find("YRoot/ZRoot/XRoot");
      _yGizmoRoot = _gizmoRoot.Find("YRoot");
      _zGizmoRoot = _gizmoRoot.Find("YRoot/ZRoot");

      return _gizmoRoot.transform;
    }

    static void AssignGizmoRenderers() { 
      _xRenderer = _gizmoRoot.Find("YRoot/ZRoot/XRoot/X").gameObject.GetComponent<MeshRenderer>();
      _yRenderer = _gizmoRoot.Find("YRoot/Y").gameObject.GetComponent<MeshRenderer>();
      _zRenderer = _gizmoRoot.Find("YRoot/ZRoot/Z").gameObject.GetComponent<MeshRenderer>();
    }
    
    // by default, the gizmo is using unlit colour shaders which don't support alpha (transparency). I was having trouble changing the shaders in unity and repacking it myself so i'm just changing it in-game instead.
    static void ChangeGizmoShaders() { 
      if(_xRenderer != null) { 
        _xRenderer.material.shader = Shader.Find("GUI/Text Shader");
        _yRenderer.material.shader = Shader.Find("GUI/Text Shader");
        _zRenderer.material.shader = Shader.Find("GUI/Text Shader");
      }
    }

    static void SetGizmoOpacity() { 
      if(_xRenderer != null) { 
        if(_xRenderer.material.color.a != GizmoOpacity.Value / 100f) { 
          _xRenderer.material.color = new Color(_xRenderer.material.color.r, _xRenderer.material.color.g, _xRenderer.material.color.b, GizmoOpacity.Value / 100f);
          _yRenderer.material.color = new Color(_yRenderer.material.color.r, _yRenderer.material.color.g, _yRenderer.material.color.b, GizmoOpacity.Value / 100f);
          _zRenderer.material.color = new Color(_zRenderer.material.color.r, _zRenderer.material.color.g, _zRenderer.material.color.b, GizmoOpacity.Value / 100f);
        }
      }
    }

    static void ConvertCustomSnapStagesToList() {
      if (_customSnapStageList.Any()) { 
        _previousCustomSnap = _customSnapStageList[_customListIndex];
      } else { 
        _previousCustomSnap = 0;
      }
      _customSnapStageList.Clear();
      int num = 0;
      string[] segments = CustomSnapStages.Value.Split(',');
      foreach (string s in segments) {
        if (s.Length > 0 && s.Length < Int64.MaxValue.ToString().Length) {
          if (IsAllDigits(s)) {
            long num64 = Int64.Parse(s);
            if (num64 >= 1 && num64 <= Int32.MaxValue) {
              num = (Int32)num64;
            } else if (num64 > Int32.MaxValue) {
              num = MaxCustomSnapDivisions;
            } else { 
              num = MinSnapDivisions;
            }
            if (_customSnapStageList.Count < MaxCustomStages) { 
              _customSnapStageList.Add(num);
            }
          }
        }
      } 
    }

    static bool IsAllDigits(string text) {
      string s = text.Trim();
      if (s.Length == 0) {
        return false;
      }
      for (int i = 0; i < s.Length; i++) {
        if (Char.IsDigit(s[i]) == false) {
          return false;
        }
      }
      return true;
    }

    // floating point oddities are causing issues with comparisons, using this to truncate the last digit to make sure they're the same
    static float FixFloatingPointDecimals(float num) {
      string s = num.ToString();
      if (s.Length > 7) { 
        s = s.Substring(0, s.Length - 1);
        float n = float.Parse(s);
        return n;
      } else { return num; 
      }
    }

    static bool CustomSnapEnabled() {
      return UseCustomSnapStages.Value == true && _customSnapStageList.Any();
    }

    static List<int> GenerateDefaultSnapDivisionList() {
      List<int> defaults = new List<int>();
      for(int i = MinSnapDivisions; i <= MaxSnapDivisions; i *= 2)
        defaults.Add(i);
      return defaults;
    }




    // TODO i would like the snap division messages to be faster, is there a way to do this, or perhaps display custom text on the HUD like Euler's Ruler or ClockMod?
  }
}
