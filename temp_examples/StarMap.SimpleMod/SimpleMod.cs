using Brutal.ImGuiApi;
using KSA;
using StarMap.API;
using Brutal.Numerics;
using System.Reflection;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Text;

namespace StarMap.SimpleExampleMod
{
    [StarMapMod]
    public class SimpleMod
    {
        private bool _isRotating = false;
        private bool _isTranslating = false;
        private bool _applyYPR = false;
        private bool _applyPositionOffset = false;
        private Camera? _camera;
        private static bool _firstLog = true;
        
        // Debug UI state
        private double3 _positionOffset = double3.Zero;
        private double3 _rotationAxis = new double3(0, 0, 1);
        private float _rotationSpeed = 1.0f;
        private float _translationSpeed = 10.0f; // Much smaller default
        private float _keypadMovementSpeed = 5.0f; // Speed for keypad movement
        private float _keypadRotationSpeed = 35.0f; // Speed for keypad rotation
        private float _circleAngle = 0.0f;
        private double _circleRadius = 1000.0; // Smaller default radius
        
        // Yaw/Pitch/Roll controls
        private float _yaw = 0.0f;
        private float _pitch = 0.0f;
        private float _roll = 0.0f;
        
        // Look At controls
        private bool _isLookingAt = false;
        private double3 _lookAtTarget = double3.Zero;
        private bool _isLookingAtVessel = false;
        private dynamic? _lastFollowedVessel = null;
        
        // UDP control state
        private UdpHandler _udpHandler = new UdpHandler();
        private SmoothingFilter _smoothingFilter = new SmoothingFilter(6);
        private bool _enableUdpInput = false;
        
        // UDP Settings
        private float _sensitivityTranslation = 0.1f;
        private float _sensitivityRotation = 1.0f;
        private float _smoothness = 0.9f; // 0.0 to 0.99
        private bool[] _mirrorAxes = new bool[6]; // 0=X, 1=Y, 2=Z, 3=Roll, 4=Pitch, 5=Yaw
        
        // Manual Follow controls
        private dynamic? _followedObject = null;
        private double3 _followOffset = double3.Zero;
        private bool _isManualFollowing = false;

        // QuickView and Animation Managers
        private QuickViewManager _quickViewManager = new QuickViewManager();
        private AnimationManager _animationManager = new AnimationManager();
        private bool _applyYPRBeforeAnimation = false;
        private bool _wasAnimationPlaying = false;
        
        // UI State for new features
        private byte[] _newQuickViewName = new byte[32];
        private float _newKeyframeTime = 0.0f;

        // Animated Actions state
        private float _actionDuration = 2.0f;           // 0-30 seconds
        private double _zoomOutDistance = 100.0;        // 0-1000 meters
        private EasingType _actionEasing = EasingType.EaseInOut;
        private bool _orbitCounterClockwise = false;

        static SimpleMod()
        {
            Console.WriteLine("SimpleMod - STATIC Constructor called");
        }

        public SimpleMod()
        {
            Console.WriteLine("SimpleMod - Instance Constructor called");
            Encoding.UTF8.GetBytes("View 1").CopyTo(_newQuickViewName, 0);
        }

        private void UpdateCamera(double dt)
        {
            // Track animation state transitions
            bool isCurrentlyPlaying = _animationManager.IsPlaying;
            
            // Save YPR state when animation starts
            if (isCurrentlyPlaying && !_wasAnimationPlaying)
            {
                _applyYPRBeforeAnimation = _applyYPR;
            }
            
            // Animation Update
            var animData = _animationManager.Update(dt);
            if (animData.HasValue)
            {
                var data = animData.Value;
                _positionOffset = data.Offset;
                _applyPositionOffset = true;
                
                _yaw = data.Yaw;
                _pitch = data.Pitch;
                _roll = data.Roll;
                _applyYPR = true;
                
                // Apply FOV from animation
                if (_camera != null)
                {
                    _camera.SetFieldOfView(data.Fov);
                }
            }
            
            // Restore YPR state when animation ends
            if (_wasAnimationPlaying && !_animationManager.IsPlaying)
            {
                _applyYPR = _applyYPRBeforeAnimation;
            }
            
            _wasAnimationPlaying = _animationManager.IsPlaying;

            // Always get camera if any control is active or we need to cache it
            if (_isRotating || _isTranslating || _applyYPR || _applyPositionOffset || _isLookingAt || _isLookingAtVessel || _isManualFollowing || _enableUdpInput || _camera == null)
            {
                try
                {
                    var ksaAssembly = typeof(KSA.Camera).Assembly;
                    var programType = ksaAssembly.GetType("KSA.Program");
                    
                    if (programType != null)
                    {
                        var getMainCameraMethod = programType.GetMethod("GetMainCamera", BindingFlags.Public | BindingFlags.Static);
                        var getCameraMethod = programType.GetMethod("GetCamera", BindingFlags.Public | BindingFlags.Static);
                        MethodInfo? methodToUse = getMainCameraMethod ?? getCameraMethod;
                        
                        if (methodToUse != null)
                        {
                            var camera = methodToUse.Invoke(null, null) as Camera;
                            
                            if (camera != null)
                            {
                                if (_camera == null)
                                {
                                    var methodName = getMainCameraMethod != null ? "GetMainCamera" : "GetCamera";
                                    Console.WriteLine($"SimpleMod - Found camera via Program.{methodName}()");
                                }
                                
                                // Track last followed vessel for look-at tracking
                                var currentFollowing = camera.Following;
                                if (currentFollowing != null && currentFollowing != _lastFollowedVessel)
                                {
                                    _lastFollowedVessel = currentFollowing;
                                }
                                
                                // Manual Follow Logic
                                if (_isManualFollowing && _followedObject != null)
                                {
                                    try 
                                    {
                                        var targetPos = _followedObject.GetPositionEcl();
                                        
                                        // If translating, update offset
                                        if (_isTranslating)
                                        {
                                            // Translation happens below, but we need to sync offset for next frame
                                            // We'll do it after translation
                                        }
                                        else
                                        {
                                            // Snap to offset
                                            camera.PositionEcl = targetPos + _followOffset;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"SimpleMod - Error following object: {ex.Message}");
                                        _isManualFollowing = false;
                                        _followedObject = null;
                                    }
                                }

                                // UDP Input Application
                                if (_enableUdpInput)
                                {
                                    // Get relative values (raw - center)
                                    var relative = _udpHandler.GetRelativeValues();
                                    
                                    // Apply smoothing
                                    var smoothed = _smoothingFilter.Apply(relative, _smoothness);
                                    
                                    // Indices: 0=X, 1=Y, 2=Z, 3=Roll(was Yaw), 4=Pitch, 5=Yaw(was Roll)
                                    // Because user said Yaw and Roll are swapped in data.
                                    
                                    // Apply Mirroring (Indices match the output concept: 0-2 Pos, 3=Roll, 4=Pitch, 5=Yaw)
                                    // Note: smoothed array is still raw index order [0..5] from UDP
                                    // UDP Order: X, Y, Z, Yaw(Raw), Pitch(Raw), Roll(Raw)
                                    // We interpret: 
                                    //   0 -> X (Mirror Index 0)
                                    //   1 -> Y (Mirror Index 1)
                                    //   2 -> Z (Mirror Index 2)
                                    //   3 -> Roll (Mirror Index 3) - Coming from UDP[3] ? Wait, user said 3 is Roll in Application but 3 is Yaw in Data
                                    //   Let's clarify mapping:
                                    //   Data[0] = X
                                    //   Data[1] = Y
                                    //   Data[2] = Z
                                    //   Data[3] = Yaw (Original Label) -> Mapped to Roll
                                    //   Data[4] = Pitch
                                    //   Data[5] = Roll (Original Label) -> Mapped to Yaw
                                    
                                    // To make "Mirror Index" intuitive, let's align them with the Application Logic:
                                    // 0: X
                                    // 1: Y
                                    // 2: Z
                                    // 3: Roll
                                    // 4: Pitch
                                    // 5: Yaw
                                    
                                    // Position Offset (Assuming meters)
                                    // Apply Translation Sensitivity
                                    double valX = smoothed[0];
                                    double valY = smoothed[1];
                                    double valZ = -smoothed[2]; // Z is internally mirrored
                                    
                                    if (_mirrorAxes[0]) valX *= -1;
                                    if (_mirrorAxes[1]) valY *= -1;
                                    if (_mirrorAxes[2]) valZ *= -1;
                                    
                                    double dx = valX * _sensitivityTranslation;
                                    double dy = valY * _sensitivityTranslation;
                                    double dz = valZ * _sensitivityTranslation;
                                    
                                    var udpPosOffset = new double3(dx, dy, dz);
                                    var finalPosOffset = _positionOffset + udpPosOffset;

                                    // Apply combined position offset
                                    if (_applyPositionOffset || _enableUdpInput) 
                                    {
                                         if (finalPosOffset.Length() > 0.001)
                                         {
                                             camera.PositionEcl = camera.PositionEcl + finalPosOffset;
                                         }
                                    }

                                    // Rotation (Assuming degrees)
                                    // Apply Rotation Sensitivity
                                    // Mapping: 3->Roll, 5->Yaw (Swapped)
                                    
                                    // UDP[3] maps to Roll (Index 3)
                                    double valRoll = -smoothed[3]; // Roll is internally mirrored
                                    // UDP[4] maps to Pitch (Index 4)
                                    double valPitch = smoothed[4];
                                    // UDP[5] maps to Yaw (Index 5)
                                    double valYaw = smoothed[5];
                                    
                                    if (_mirrorAxes[3]) valRoll *= -1; // Roll
                                    if (_mirrorAxes[4]) valPitch *= -1; // Pitch
                                    if (_mirrorAxes[5]) valYaw *= -1; // Yaw
                                    
                                    var udpRoll = (float)(valRoll * _sensitivityRotation);
                                    var udpPitch = (float)(valPitch * _sensitivityRotation);
                                    var udpYaw = (float)(valYaw * _sensitivityRotation);
                                    
                                    // Apply UDP rotation: build base quaternion, then apply UDP delta in local frame
                                    if (_enableUdpInput)
                                    {
                                        // Build base rotation quaternion from _yaw, _pitch, _roll
                                        var baseYawRad = _yaw * (Math.PI / 180.0);
                                        var basePitchRad = _pitch * (Math.PI / 180.0);
                                        var baseRollRad = _roll * (Math.PI / 180.0);
                                        
                                        var baseYawQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), baseYawRad);
                                        var basePitchQuat = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), basePitchRad);
                                        var baseRollQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), baseRollRad);
                                        var baseRot = baseYawQuat * basePitchQuat * baseRollQuat;
                                        
                                        // Build UDP delta rotation quaternion
                                        var udpYawRad = udpYaw * (Math.PI / 180.0);
                                        var udpPitchRad = udpPitch * (Math.PI / 180.0);
                                        var udpRollRad = udpRoll * (Math.PI / 180.0);
                                        
                                        var udpYawQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), udpYawRad);
                                        var udpPitchQuat = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), udpPitchRad);
                                        var udpRollQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), udpRollRad);
                                        var udpDeltaRot = udpYawQuat * udpPitchQuat * udpRollQuat;
                                        
                                        // Apply UDP delta in local frame of base rotation: base * delta
                                        var newRot = baseRot * udpDeltaRot;
                                        
                                        // Convert doubleQuat to floatQuat
                                        var fQuat = new floatQuat((float)newRot.X, (float)newRot.Y, (float)newRot.Z, (float)newRot.W);
                                        
                                        // Create rotation matrix
                                        var rotMatrix = float4x4.CreateFromQuaternion(fQuat);
                                        
                                        // Get current high-precision position
                                        var currentPos = camera.PositionEcl;
                                        var savedLocalPos = camera.LocalPosition;
                                        
                                        // Apply matrix (this sets LocalRotation and LocalPosition)
                                        camera.SetMatrix(rotMatrix);
                                        
                                        // Restore position
                                        camera.LocalPosition = savedLocalPos;
                                        camera.PositionEcl = currentPos;
                                        
                                        // Force WorldRotation to match
                                        camera.WorldRotation = newRot;
                                    }
                                    
                                    // Apply manual YPR using SetMatrix (absolute angles from sliders)
                                    if (_applyYPR)
                                    {
                                        var yawRad = _yaw * (Math.PI / 180.0);
                                        var pitchRad = _pitch * (Math.PI / 180.0);
                                        var rollRad = _roll * (Math.PI / 180.0);
                                        
                                        // Create rotation quaternions in ECL space
                                        // Yaw around Z (Up), Pitch around X (Right), Roll around Y (Forward) - based on ECL frame
                                        var yawQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), yawRad);
                                        var pitchQuat = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), pitchRad);
                                        var rollQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), rollRad);
                                        
                                        // Combine rotations
                                        var newRot = yawQuat * pitchQuat * rollQuat;
                                        
                                        // Use SetMatrix as requested, but be careful with position
                                        // Convert doubleQuat to floatQuat
                                        var fQuat = new floatQuat((float)newRot.X, (float)newRot.Y, (float)newRot.Z, (float)newRot.W);
                                        
                                        // Create rotation matrix
                                        var rotMatrix = float4x4.CreateFromQuaternion(fQuat);
                                        
                                        // We need to construct a full matrix with the CURRENT position
                                        // SetMatrix decomposes the matrix into Scale, Rotation, and Position
                                        // So we must provide the correct position in the matrix itself
                                        
                                        // Get current high-precision position
                                        var currentPos = camera.PositionEcl;
                                        
                                        // But SetMatrix sets LocalPosition, not PositionEcl directly (it seems)
                                        // Let's check if we can just set Rotation directly again, forcing it hard
                                        // If SetMatrix is required, we should probably just use it for rotation and restore position
                                        
                                        var savedLocalPos = camera.LocalPosition;
                                        
                                        // Apply matrix (this sets LocalRotation and LocalPosition)
                                        camera.SetMatrix(rotMatrix);
                                        
                                        // IMMEDIATELY restore position to fight the drift/reset
                                        camera.LocalPosition = savedLocalPos;
                                        camera.PositionEcl = currentPos; // Double insurance
                                        
                                        // Also force WorldRotation to match
                                        camera.WorldRotation = newRot;
                                    }
                                }
                                else // UDP Disabled, use only standard controls
                                {
                                     // Apply position offset
                                    if (_applyPositionOffset && _positionOffset.Length() > 0.001)
                                    {
                                        camera.PositionEcl = camera.PositionEcl + _positionOffset;
                                    }
                                }
                                
                                // Apply YPR using SetMatrix as requested
                                if (_applyYPR)
                                {
                                    var yawRad = _yaw * (Math.PI / 180.0);
                                    var pitchRad = _pitch * (Math.PI / 180.0);
                                    var rollRad = _roll * (Math.PI / 180.0);

                                    // Create rotation quaternions in ECL space
                                    // Yaw around Z (Up), Pitch around X (Right), Roll around Y (Forward) - based on ECL frame
                                    var yawQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), yawRad);
                                    var pitchQuat = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), pitchRad);
                                    var rollQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), rollRad);

                                    // Combine rotations
                                    var newRot = yawQuat * pitchQuat * rollQuat;
                                    
                                    // Use SetMatrix as requested, but be careful with position
                                    // Convert doubleQuat to floatQuat
                                    var fQuat = new floatQuat((float)newRot.X, (float)newRot.Y, (float)newRot.Z, (float)newRot.W);
                                    
                                    // Create rotation matrix
                                    var rotMatrix = float4x4.CreateFromQuaternion(fQuat);
                                    
                                    // We need to construct a full matrix with the CURRENT position
                                    // SetMatrix decomposes the matrix into Scale, Rotation, and Position
                                    // So we must provide the correct position in the matrix itself
                                    
                                    // Get current high-precision position
                                    var currentPos = camera.PositionEcl;
                                    
                                    // But SetMatrix sets LocalPosition, not PositionEcl directly (it seems)
                                    // Let's check if we can just set Rotation directly again, forcing it hard
                                    // If SetMatrix is required, we should probably just use it for rotation and restore position
                                    
                                    var savedLocalPos = camera.LocalPosition;
                                    
                                    // Apply matrix (this sets LocalRotation and LocalPosition)
                                    camera.SetMatrix(rotMatrix);
                                    
                                    // IMMEDIATELY restore position to fight the drift/reset
                                    camera.LocalPosition = savedLocalPos;
                                    camera.PositionEcl = currentPos; // Double insurance
                                    
                                    // Also force WorldRotation to match
                                    camera.WorldRotation = newRot;
                                }
                                
                                // Look At - Update target if tracking vessel
                                if (_isLookingAtVessel)
                                {
                                    dynamic? vesselToTrack = camera.Following ?? _lastFollowedVessel;
                                    if (vesselToTrack != null)
                                    {
                                        try
                                        {
                                            _lookAtTarget = vesselToTrack.GetPositionEcl();
                                            _isLookingAt = true;
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"SimpleMod - Error getting vessel position for look at: {ex.Message}");
                                            _isLookingAtVessel = false;
                                        }
                                    }
                                    else
                                    {
                                        // No vessel to track, disable vessel tracking
                                        _isLookingAtVessel = false;
                                    }
                                }
                                
                                // Look At
                                if (_isLookingAt)
                                {
                                    camera.LookAt(_lookAtTarget, new double3(0, 0, 1));
                                }

                                // Circle rotation
                                if (_isRotating)
                                {
                                    var following = camera.Following;
                                    if (following != null)
                                    {
                                        var targetPos = following.GetPositionEcl();
                                        _circleAngle += (float)(dt * _rotationSpeed);
                                        
                                        // Create horizontal circle in ECL XZ plane
                                        var circleX = Math.Cos(_circleAngle) * _circleRadius;
                                        var circleZ = Math.Sin(_circleAngle) * _circleRadius;
                                        
                                        // Get current altitude relative to target
                                        var currentPos = camera.PositionEcl;
                                        var currentAltitude = currentPos.Y - targetPos.Y;
                                        
                                        // Set camera position in horizontal circle
                                        var circlePos = new double3(
                                            targetPos.X + circleX,
                                            targetPos.Y + currentAltitude,
                                            targetPos.Z + circleZ
                                        );
                                        
                                        camera.PositionEcl = circlePos;
                                        
                                        // Look at target
                                        var upEcl = new double3(0, 0, 1);
                                        camera.LookAt(targetPos, upEcl);
                                    }
                                }
                                
                                // Translation
                                if (_isTranslating)
                                {
                                    var right = camera.GetRight();
                                    camera.Translate(right * dt * _translationSpeed);
                                    
                                    // If following, update the offset based on the new position
                                    if (_isManualFollowing && _followedObject != null)
                                    {
                                        _followOffset = camera.PositionEcl - _followedObject.GetPositionEcl();
                                    }
                                }
                                
                                _camera = camera;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SimpleMod - Exception finding camera: {ex.Message}");
                }
            }
        }

        [StarMapAfterGui]
        public void OnAfterUi(double dt)
        {
            // Check for input keys (within ImGui context)
            bool keypad2Pressed = false;
            bool keypad4Pressed = false;
            bool keypad5Pressed = false;
            bool keypad6Pressed = false;
            bool keypad7Pressed = false;
            bool keypad8Pressed = false;
            bool keypad9Pressed = false;
            bool ctrlPressed = false;
            bool wPressed = false;
            bool aPressed = false;
            bool sPressed = false;
            bool dPressed = false;
            bool rPressed = false;
            bool fPressed = false;
            try
            {
                // Check if keys are pressed using ImGui
                keypad2Pressed = ImGui.IsKeyDown(ImGuiKey.Keypad2);
                keypad4Pressed = ImGui.IsKeyDown(ImGuiKey.Keypad4);
                keypad5Pressed = ImGui.IsKeyPressed(ImGuiKey.Keypad5); // Use IsKeyPressed for one-time action
                keypad6Pressed = ImGui.IsKeyDown(ImGuiKey.Keypad6);
                keypad7Pressed = ImGui.IsKeyDown(ImGuiKey.Keypad7);
                keypad8Pressed = ImGui.IsKeyDown(ImGuiKey.Keypad8);
                keypad9Pressed = ImGui.IsKeyDown(ImGuiKey.Keypad9);
                ctrlPressed = ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl);
                wPressed = ImGui.IsKeyDown(ImGuiKey.W);
                aPressed = ImGui.IsKeyDown(ImGuiKey.A);
                sPressed = ImGui.IsKeyDown(ImGuiKey.S);
                dPressed = ImGui.IsKeyDown(ImGuiKey.D);
                rPressed = ImGui.IsKeyDown(ImGuiKey.R);
                fPressed = ImGui.IsKeyDown(ImGuiKey.F);
            }
            catch
            {
                // Fallback if keys don't exist, try alternative
                try
                {
                    keypad2Pressed = ImGui.IsKeyDown((ImGuiKey)322); // GLFW_KEY_KP_2
                    keypad4Pressed = ImGui.IsKeyDown((ImGuiKey)324); // GLFW_KEY_KP_4
                    keypad5Pressed = ImGui.IsKeyPressed((ImGuiKey)325); // GLFW_KEY_KP_5
                    keypad6Pressed = ImGui.IsKeyDown((ImGuiKey)326); // GLFW_KEY_KP_6
                    keypad7Pressed = ImGui.IsKeyDown((ImGuiKey)327); // GLFW_KEY_KP_7
                    keypad8Pressed = ImGui.IsKeyDown((ImGuiKey)328); // GLFW_KEY_KP_8
                    keypad9Pressed = ImGui.IsKeyDown((ImGuiKey)329); // GLFW_KEY_KP_9
                    ctrlPressed = ImGui.IsKeyDown((ImGuiKey)341) || ImGui.IsKeyDown((ImGuiKey)345); // GLFW_KEY_LEFT_CONTROL or GLFW_KEY_RIGHT_CONTROL
                    wPressed = ImGui.IsKeyDown((ImGuiKey)87); // GLFW_KEY_W
                    aPressed = ImGui.IsKeyDown((ImGuiKey)65); // GLFW_KEY_A
                    sPressed = ImGui.IsKeyDown((ImGuiKey)83); // GLFW_KEY_S
                    dPressed = ImGui.IsKeyDown((ImGuiKey)68); // GLFW_KEY_D
                    rPressed = ImGui.IsKeyDown((ImGuiKey)82); // GLFW_KEY_R
                    fPressed = ImGui.IsKeyDown((ImGuiKey)70); // GLFW_KEY_F
                }
                catch
                {
                    // If still fails, skip input
                }
            }
            
            // Handle keypad 5 for re-follow vessel (no modifier needed)
            if (keypad5Pressed && _camera != null)
            {
                if (_isManualFollowing && _followedObject != null)
                {
                    // Re-follow the original object
                    if (_followedObject is Astronomical astronomical)
                    {
                        _camera.SetFollow(astronomical, false, changeControl: false, alert: false);
                        _lastFollowedVessel = astronomical; // Save for look-at tracking
                    }
                    _isManualFollowing = false;
                    _applyYPR = false;
                    _applyPositionOffset = false;
                }
            }

            // Ctrl+WASD: camera-relative movement
            // Ctrl+RF: up/down movement
            // Ctrl+keypad: rotation controls
            if (ctrlPressed)
            {
                // Handle Ctrl+keypad rotation controls
                if (keypad7Pressed || keypad9Pressed)
                {
                    // Yaw: Ctrl+keypad 4/6 (left/right)
                    // Use fixed ECL up axis to maintain consistent yaw behavior regardless of roll
                    float yawDelta = 0.0f;
                    if (keypad7Pressed)
                    {
                        yawDelta = (float)(dt * _keypadRotationSpeed); // Yaw left
                    }
                    else if (keypad9Pressed)
                    {
                        yawDelta = -(float)(dt * _keypadRotationSpeed); // Yaw right
                    }

                    // Apply yaw rotation relative to camera's current orientation
                    if (_camera != null)
                    {
                        // Get current camera orientation as quaternion
                        var currentQuat = _camera.WorldRotation;

                        // Use fixed ECL Z axis (up) for consistent yaw behavior
                        var eclUpAxis = new double3(0, 0, 1); // ECL Z axis (up)

                        var yawRad = yawDelta * (Math.PI / 180.0);
                        var localYawQuat = doubleQuat.CreateFromAxisAngle(eclUpAxis, yawRad);

                        // Apply local yaw rotation to current orientation
                        var newQuat = currentQuat * localYawQuat;

                        // Convert back to YPR for storage
                        var (newYaw, newPitch, newRoll) = QuaternionToYPR(newQuat);

                        _yaw = newYaw;
                        _pitch = newPitch;
                        _roll = newRoll;
                        _applyYPR = true;
                    }
                    else
                    {
                        // Fallback to old behavior if camera not available
                        _yaw += yawDelta;
                        _applyYPR = true;
                    }
                }

                if (keypad8Pressed || keypad2Pressed)
                {
                    // Pitch: Ctrl+keypad 8/2 (up/down)
                    // Use fixed ECL right axis to maintain consistent pitch behavior regardless of roll
                    float pitchDelta = 0.0f;
                    if (keypad8Pressed)
                    {
                        pitchDelta = (float)(dt * _keypadRotationSpeed); // Pitch up
                    }
                    else if (keypad2Pressed)
                    {
                        pitchDelta = -(float)(dt * _keypadRotationSpeed); // Pitch down
                    }

                    // Apply pitch rotation relative to camera's current orientation
                    if (_camera != null)
                    {
                        // Get current camera orientation as quaternion
                        var currentQuat = _camera.WorldRotation;

                        // Use fixed ECL X axis (right) for consistent pitch behavior
                        var eclRightAxis = new double3(1, 0, 0); // ECL X axis (right)

                        var pitchRad = pitchDelta * (Math.PI / 180.0);
                        var localPitchQuat = doubleQuat.CreateFromAxisAngle(eclRightAxis, pitchRad);

                        // Apply local pitch rotation to current orientation
                        var newQuat = currentQuat * localPitchQuat;

                        // Convert back to YPR for storage
                        var (newYaw, newPitch, newRoll) = QuaternionToYPR(newQuat);

                        _yaw = newYaw;
                        _pitch = newPitch;
                        _roll = newRoll;
                        _applyYPR = true;
                    }
                    else
                    {
                        // Fallback to old behavior if camera not available
                        _pitch += pitchDelta;
                        _applyYPR = true;
                    }
                }

                if (keypad4Pressed || keypad6Pressed)
                {
                    // Roll: Ctrl+keypad 7/9 (left/right)
                    // Use fixed ECL forward axis to maintain consistent roll behavior regardless of other rotations
                    float rollDelta = 0.0f;
                    if (keypad6Pressed)
                    {
                        rollDelta = -(float)(dt * _keypadRotationSpeed); // Roll left
                    }
                    else if (keypad4Pressed)
                    {
                        rollDelta = (float)(dt * _keypadRotationSpeed); // Roll right
                    }

                    // Apply roll rotation relative to camera's current orientation
                    if (_camera != null)
                    {
                        // Get current camera orientation as quaternion
                        var currentQuat = _camera.WorldRotation;

                        // Use fixed ECL Y axis (forward) for consistent roll behavior
                        var eclForwardAxis = new double3(0, 1, 0); // ECL Y axis (forward)

                        var rollRad = rollDelta * (Math.PI / 180.0);
                        var localRollQuat = doubleQuat.CreateFromAxisAngle(eclForwardAxis, rollRad);

                        // Apply local roll rotation to current orientation
                        var newQuat = currentQuat * localRollQuat;

                        // Convert back to YPR for storage
                        var (newYaw, newPitch, newRoll) = QuaternionToYPR(newQuat);

                        _yaw = newYaw;
                        _pitch = newPitch;
                        _roll = newRoll;
                        _applyYPR = true;
                    }
                    else
                    {
                        // Fallback to old behavior if camera not available
                        _roll += rollDelta;
                        _applyYPR = true;
                    }
                }
            }

            // Ensure camera update happens after game logic (and potential overrides)
            UpdateCamera(dt);

            // Now handle Ctrl+WASD/RF camera-relative position movement (after UpdateCamera so we have access to camera)
            Camera? movementCamera = _camera;
            if (movementCamera != null && ctrlPressed)
            {
                // Handle Ctrl+WASD/RF movement
                if (wPressed || aPressed || sPressed || dPressed || rPressed || fPressed)
                {
                    // Get camera orientation vectors for relative movement
                    double3 forward = movementCamera.GetForward();
                    double3 right = movementCamera.GetRight();
                    double3 up = movementCamera.GetUp();

                    double3 movementVector = double3.Zero;

                    // Forward/backward: Ctrl+W/S
                    if (wPressed)
                    {
                        movementVector += forward * dt * _keypadMovementSpeed; // Move forward
                    }
                    else if (sPressed)
                    {
                        movementVector -= forward * dt * _keypadMovementSpeed; // Move backward
                    }

                    // Left/right strafe: Ctrl+A/D
                    if (aPressed)
                    {
                        movementVector -= right * dt * _keypadMovementSpeed; // Strafe left
                    }
                    else if (dPressed)
                    {
                        movementVector += right * dt * _keypadMovementSpeed; // Strafe right
                    }

                    // Up/down: Ctrl+R/F
                    if (rPressed)
                    {
                        movementVector += up * dt * _keypadMovementSpeed; // Move up
                    }
                    else if (fPressed)
                    {
                        movementVector -= up * dt * _keypadMovementSpeed; // Move down
                    }

                    _positionOffset += movementVector;
                    _applyPositionOffset = true;
                }
            }

            if (_firstLog)
            {
                Console.WriteLine("SimpleMod - OnAfterUi running (first frame log)");
                _firstLog = false;
            }

            // Get camera for debug UI
            Camera? debugCamera = _camera; // Use the cached camera from UpdateCamera

            // Create a nice debug window for Camera Controls
            if (ImGui.Begin("Camera Debug Controls"))
            {
                if (debugCamera == null)
                {
                    ImGui.TextColored(new float4(1, 0, 0, 1), "Camera not found!");
                }
                else
                {
                    // Camera Info Section
                    ImGui.SeparatorText("Camera Info");
                    var pos = debugCamera.PositionEcl;
                    ImGui.Text($"Position ECL: X={pos.X:F2}, Y={pos.Y:F2}, Z={pos.Z:F2}");
                    
                    var forward = debugCamera.GetForward();
                    ImGui.Text($"Forward: X={forward.X:F3}, Y={forward.Y:F3}, Z={forward.Z:F3}");
                    
                    var up = debugCamera.GetUp();
                    ImGui.Text($"Up: X={up.X:F3}, Y={up.Y:F3}, Z={up.Z:F3}");
                    
                    var right = debugCamera.GetRight();
                    ImGui.Text($"Right: X={right.X:F3}, Y={right.Y:F3}, Z={right.Z:F3}");
                    
                    var fov = debugCamera.GetFieldOfView();
                    ImGui.Text($"FOV (radians): {fov:F4}");
                    ImGui.Text($"FOV (degrees): {fov * 57.2958f:F2}°");
                    
                    var following = debugCamera.Following;
                    ImGui.Text($"Following: {(following != null ? following.Id ?? "Unknown" : "None")}");
                    ImGui.SameLine();
                    if (ImGui.Button(_isManualFollowing ? "Re-follow" : "Free Camera"))
                    {
                        if (_isManualFollowing)
                        {
                            // Re-follow the original object
                            if (_followedObject is Astronomical astronomical)
                            {
                                debugCamera.SetFollow(astronomical, false, changeControl: false, alert: false);
                                _lastFollowedVessel = astronomical; // Save for look-at tracking
                            }
                            _isManualFollowing = false;
                            _applyYPR = false;
                            _applyPositionOffset = false;
                        }
                        else
                        {
                            // Capture current target and free the camera
                            var currentFollowing = debugCamera.Following;
                            if (currentFollowing != null)
                            {
                                _followedObject = currentFollowing;
                                _lastFollowedVessel = currentFollowing; // Save for look-at tracking
                                _followOffset = debugCamera.PositionEcl - currentFollowing.GetPositionEcl();

                                // Capture current rotation to maintain it
                                var currentQuat = debugCamera.WorldRotation;
                                var (capturedYaw, capturedPitch, capturedRoll) = QuaternionToYPR(currentQuat);

                                _yaw = capturedYaw;
                                _pitch = capturedPitch;
                                _roll = capturedRoll;
                                _applyYPR = true;

                                _isManualFollowing = true;
                            }
                            debugCamera.Unfollow();
                        }
                    }
                    
                    
                    ImGui.SeparatorText("UDP Input");
                    if (ImGui.Button(_enableUdpInput ? "Disable UDP Input" : "Enable UDP Input"))
                    {
                        _enableUdpInput = !_enableUdpInput;
                        if (_enableUdpInput)
                        {
                            _applyYPR = false;
                            _applyPositionOffset = true;
                        }
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button("Reset Center (Next Packet)"))
                    {
                        _udpHandler.RequestCenterReset();
                    }

                    if (_enableUdpInput)
                    {
                        ImGui.TextColored(new float4(0, 1, 0, 1), $"UDP Active on port {_udpHandler.Port}");
                        
                        var raw = _udpHandler.GetRawValues();
                        ImGui.Text($"Raw: X={raw[0]:F2} Y={raw[1]:F2} Z={raw[2]:F2}");
                        // Note: We treat index 3 as Roll and 5 as Yaw in application, but here we show raw
                        ImGui.Text($"     [3](Roll)={raw[3]:F2} [4](Pitch)={raw[4]:F2} [5](Yaw)={raw[5]:F2}");
                        
                        ImGui.Spacing();
                        ImGui.SeparatorText("UDP Settings");
                        ImGui.SliderFloat("Translation Sens.", ref _sensitivityTranslation, 0.0f, 10.0f);
                        ImGui.SliderFloat("Rotation Sens.", ref _sensitivityRotation, 0.0f, 10.0f);
                        ImGui.SliderFloat("Smoothness", ref _smoothness, 0.0f, 0.99f);
                        
                        ImGui.Spacing();
                        ImGui.Text("Mirror Axes");
                        ImGui.Checkbox("Mirror X", ref _mirrorAxes[0]); ImGui.SameLine();
                        ImGui.Checkbox("Mirror Y", ref _mirrorAxes[1]); ImGui.SameLine();
                        ImGui.Checkbox("Mirror Z", ref _mirrorAxes[2]);
                        
                        ImGui.Checkbox("Mirror Roll", ref _mirrorAxes[3]); ImGui.SameLine();
                        ImGui.Checkbox("Mirror Pitch", ref _mirrorAxes[4]); ImGui.SameLine();
                        ImGui.Checkbox("Mirror Yaw", ref _mirrorAxes[5]);
                        
                        if (ImGui.Button("Reset Smoothing Filter"))
                        {
                            _smoothingFilter.Reset();
                        }
                    }

                    ImGui.Spacing();
                    
                    // Position Controls
                    ImGui.SeparatorText("Position Controls");
                    float posX = (float)_positionOffset.X;
                    float posY = (float)_positionOffset.Y;
                    float posZ = (float)_positionOffset.Z;
                    
                    ImGui.SliderFloat("Offset X", ref posX, -30f, 30f);
                    ImGui.SameLine(); ImGui.InputFloat("##X", ref posX, 0.0f, 0.0f, "%.3f");
                    
                    ImGui.SliderFloat("Offset Y", ref posY, -30f, 30f);
                    ImGui.SameLine(); ImGui.InputFloat("##Y", ref posY, 0.0f, 0.0f, "%.3f");
                    
                    ImGui.SliderFloat("Offset Z", ref posZ, -30f, 30f);
                    ImGui.SameLine(); ImGui.InputFloat("##Z", ref posZ, 0.0f, 0.0f, "%.3f");
                    
                    _positionOffset = new double3(posX, posY, posZ);
                    
                    if (ImGui.Button(_applyPositionOffset ? "Stop Applying Offset" : "Start Applying Offset"))
                    {
                        _applyPositionOffset = !_applyPositionOffset;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Reset Offset"))
                    {
                        _positionOffset = double3.Zero;
                        _applyPositionOffset = false;
                    }

                    ImGui.Spacing();
                    
                    // Keyboard Camera Control Speed
                    ImGui.SeparatorText("Keyboard Camera Control");
                    ImGui.Text("Movement: Ctrl+WASD (forward/back/strafe)");
                    ImGui.Text("Up/Down: Ctrl+R/F");
                    ImGui.Text("Rotation: Ctrl+Keypad 4/6/8/2/7/9");
                    ImGui.Text("Re-follow: Keypad 5");
                    ImGui.SliderFloat("Movement Speed", ref _keypadMovementSpeed, 0.5f, 50.0f);
                    ImGui.SliderFloat("Rotation Speed", ref _keypadRotationSpeed, 1f, 100.0f);

                    ImGui.Spacing();

                    // QuickViews Section
                    ImGui.SeparatorText("QuickViews");
                    ImGui.InputText("Name", _newQuickViewName, (Brutal.ImGuiApi.ImGuiInputTextFlags)0);
                    ImGui.SameLine();
                    if (ImGui.Button("Save Current"))
                    {
                        string name = Encoding.UTF8.GetString(_newQuickViewName).TrimEnd('\0');
                        float currentFov = debugCamera.GetFieldOfView() * 57.2958f; // Convert radians to degrees
                        _quickViewManager.SaveView(name, _positionOffset, _yaw, _pitch, _roll, currentFov);
                    }
                    
                    var views = _quickViewManager.GetViews();
                    for (int i = 0; i < views.Count; i++)
                    {
                        var view = views[i];
                        var rot = view.GetRotation();
                        ImGui.Text($"{view.Name}: Pos({view.Offset.X:F1}, {view.Offset.Y:F1}, {view.Offset.Z:F1}) Rot({rot.Yaw:F1}, {rot.Pitch:F1}, {rot.Roll:F1}) FOV({view.Fov:F1}°)");
                        ImGui.SameLine();
                        if (ImGui.Button($"Apply##{i}"))
                        {
                            _positionOffset = view.GetDouble3Offset();
                            _applyPositionOffset = true;
                            
                            var r = view.GetRotation();
                            _yaw = r.Yaw;
                            _pitch = r.Pitch;
                            _roll = r.Roll;
                            _applyYPR = true;
                            
                            debugCamera.SetFieldOfView(view.Fov);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"Del##{i}"))
                        {
                            _quickViewManager.DeleteView(i);
                        }
                    }
                    
                    ImGui.Spacing();

                    // Animation Section
                    ImGui.SeparatorText("Animation");
                    ImGui.InputFloat("Time (s)", ref _newKeyframeTime);

                    // Time adjustment buttons (beneath the time field)
                    if (ImGui.Button("-5s")) _newKeyframeTime -= 5.0f;
                    ImGui.SameLine();
                    if (ImGui.Button("-1s")) _newKeyframeTime -= 1.0f;
                    ImGui.SameLine();
                    if (ImGui.Button("-0.5s")) _newKeyframeTime -= 0.5f;
                    ImGui.SameLine();
                    if (ImGui.Button("-0.1s")) _newKeyframeTime -= 0.1f;
                    ImGui.SameLine();
                    if (ImGui.Button("+0.1s")) _newKeyframeTime += 0.1f;
                    ImGui.SameLine();
                    if (ImGui.Button("+0.5s")) _newKeyframeTime += 0.5f;
                    ImGui.SameLine();
                    if (ImGui.Button("+1s")) _newKeyframeTime += 1.0f;
                    ImGui.SameLine();
                    if (ImGui.Button("+5s")) _newKeyframeTime += 5.0f;

                    if (ImGui.Button("Add Keyframe"))
                    {
                        float currentFov = debugCamera.GetFieldOfView() * 57.2958f; // Convert radians to degrees
                        _animationManager.AddKeyframe(_newKeyframeTime, _positionOffset, _yaw, _pitch, _roll, currentFov);

                        // Auto-advance time field based on previous keyframe interval
                        var allKeyframes = _animationManager.GetKeyframes();
                        if (allKeyframes.Count >= 2)
                        {
                            // Find the two most recent keyframes
                            var sortedKeyframes = allKeyframes.OrderBy(kf => kf.Timestamp).ToList();
                            var lastKeyframe = sortedKeyframes[sortedKeyframes.Count - 1];
                            var secondLastKeyframe = sortedKeyframes[sortedKeyframes.Count - 2];

                            // Calculate the interval and add it to current time
                            float interval = lastKeyframe.Timestamp - secondLastKeyframe.Timestamp;
                            _newKeyframeTime = lastKeyframe.Timestamp + interval;
                        }
                        else
                        {
                            // If only one keyframe, just add a default interval
                            _newKeyframeTime += 1.0f;
                        }
                    }
                    
                    var keyframes = _animationManager.GetKeyframes();
                    for (int i = 0; i < keyframes.Count; i++)
                    {
                        var kf = keyframes[i];
                        ImGui.Text($"[{kf.Timestamp:F2}s] Pos({kf.Offset.X:F1}, {kf.Offset.Y:F1}, {kf.Offset.Z:F1}) Rot({kf.Yaw:F1}, {kf.Pitch:F1}, {kf.Roll:F1}) FOV({kf.Fov:F1}°)");
                        ImGui.SameLine();
                        if (ImGui.Button($"Load##kf{i}"))
                        {
                            _positionOffset = kf.Offset;
                            _yaw = kf.Yaw;
                            _pitch = kf.Pitch;
                            _roll = kf.Roll;
                            _applyPositionOffset = true;
                            _applyYPR = true;
                            debugCamera.SetFieldOfView( kf.Fov );
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"Upd##kf{i}"))
                        {
                            float currentFovForUpdate = debugCamera.GetFieldOfView() * 57.2958f;
                            _animationManager.UpdateKeyframe( i, _positionOffset, _yaw, _pitch, _roll, currentFovForUpdate );
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"Del##kf{i}"))
                        {
                            _animationManager.RemoveKeyframe(i);
                        }
                    }
                    
                    if (ImGui.Button(_animationManager.IsPlaying ? "Stop Animation" : "Play Animation"))
                    {
                        if (_animationManager.IsPlaying)
                            _animationManager.Stop();
                        else
                            _animationManager.Play();
                    }
                    if (_animationManager.IsPlaying)
                    {
                        ImGui.SameLine();
                        ImGui.Text($"Playing: {_animationManager.CurrentTime:F2}s");
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button("Generate Flyby"))
                    {
                        GenerateFlybyAnimation(debugCamera);
                    }

                    ImGui.Spacing();

                    // Animated Actions Section
                    ImGui.SeparatorText("Animated Actions");

                    // Shared controls
                    ImGui.SliderFloat("Duration (s)", ref _actionDuration, 0f, 30f);

                    ImGui.Text("Easing:");
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Linear", _actionEasing == EasingType.Linear))
                        _actionEasing = EasingType.Linear;
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Ease In", _actionEasing == EasingType.EaseIn))
                        _actionEasing = EasingType.EaseIn;
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Ease Out", _actionEasing == EasingType.EaseOut))
                        _actionEasing = EasingType.EaseOut;
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Ease In-Out", _actionEasing == EasingType.EaseInOut))
                        _actionEasing = EasingType.EaseInOut;

                    // Orbit action
                    if (ImGui.Button("Orbit"))
                    {
                        ExecuteOrbitAction(debugCamera);
                    }
                    ImGui.SameLine();
                    ImGui.Checkbox("Counter-clockwise", ref _orbitCounterClockwise);

                    // Zoom to face action
                    if (ImGui.Button("Zoom to Face"))
                    {
                        ExecuteZoomFaceAction(debugCamera);
                    }

                    // Zoom out action
                    float zoomOutFloat = (float)_zoomOutDistance;
                    ImGui.SliderFloat("Zoom Out Distance (m)", ref zoomOutFloat, 0f, 1000f);
                    _zoomOutDistance = zoomOutFloat;
                    if (ImGui.Button("Zoom Out"))
                    {
                        ExecuteZoomOutAction(debugCamera);
                    }

                    ImGui.Spacing();

                    // Yaw/Pitch/Roll Controls
                    ImGui.SeparatorText("Yaw/Pitch/Roll Controls");
                    // note these labels are swapped for intuitive use by user
                    ImGui.SliderFloat("Yaw (degrees)", ref _roll, -359f, 359f);
                    ImGui.SameLine(); ImGui.InputFloat("##Roll", ref _roll, 0.0f, 0.0f, "%.1f");

                    ImGui.SliderFloat("Roll (degrees)", ref _yaw, -359f, 359f);
                    ImGui.SameLine(); ImGui.InputFloat("##Yaw", ref _yaw, 0.0f, 0.0f, "%.1f");
                    
                    ImGui.SliderFloat("Pitch (degrees)", ref _pitch, -359f, 359f);
                    ImGui.SameLine(); ImGui.InputFloat("##Pitch", ref _pitch, 0.0f, 0.0f, "%.1f");
                    
                    if (ImGui.Button(_applyYPR ? "Stop Applying YPR" : "Start Applying YPR"))
                    {
                        _applyYPR = !_applyYPR;
                        if (_applyYPR)
                        {
                            _isLookingAt = false;
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Reset YPR"))
                    {
                        _yaw = 0.0f;
                        _pitch = 0.0f;
                        _roll = 0.0f;
                        _applyYPR = false;
                    }
                    
                    ImGui.Spacing();

                    // Look At Controls
                    ImGui.SeparatorText("Look At Controls");
                    float targetX = (float)_lookAtTarget.X;
                    float targetY = (float)_lookAtTarget.Y;
                    float targetZ = (float)_lookAtTarget.Z;

                    ImGui.InputFloat("Target X", ref targetX);
                    ImGui.InputFloat("Target Y", ref targetY);
                    ImGui.InputFloat("Target Z", ref targetZ);

                    _lookAtTarget = new double3(targetX, targetY, targetZ);

                    if (ImGui.Button(_isLookingAt ? "Stop Looking At" : "Start Looking At"))
                    {
                        _isLookingAt = !_isLookingAt;
                        if (_isLookingAt)
                        {
                            _applyYPR = false;
                            _isRotating = false;
                            _isLookingAtVessel = false; // Disable vessel tracking when manually setting target
                        }
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button(_isLookingAtVessel ? "Stop Tracking Vessel" : "Track Vessel"))
                    {
                        _isLookingAtVessel = !_isLookingAtVessel;
                        if (_isLookingAtVessel)
                        {
                            var vesselFollowing = debugCamera.Following ?? _lastFollowedVessel;
                            if (vesselFollowing != null)
                            {
                                _isLookingAt = true;
                                _applyYPR = false;
                                _isRotating = false;
                            }
                            else
                            {
                                Console.WriteLine("SimpleMod - No vessel is being followed and no last vessel available");
                                _isLookingAtVessel = false;
                            }
                        }
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button("Set Target Front"))
                    {
                        var fwd = debugCamera.GetForward();
                        _lookAtTarget = debugCamera.PositionEcl + fwd * 100.0;
                        _isLookingAtVessel = false; // Disable vessel tracking when manually setting target
                    }
                    
                    ImGui.Spacing();
                    
                    // FOV Controls
                    ImGui.SeparatorText("Field of View");
                    float currentFovDegrees = debugCamera.GetFieldOfView() * 57.2958f;
                    if (ImGui.SliderFloat("FOV (degrees)", ref currentFovDegrees, 15f, 120f))
                    {
                        // Apply immediately when slider changes
                        debugCamera.SetFieldOfView(currentFovDegrees);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Reset FOV"))
                    {
                        debugCamera.SetFieldOfView(60.0f); // Default FOV
                    }
                    
                    ImGui.Spacing();
                    
                    // Quick Actions
                    ImGui.SeparatorText("Quick Actions");
                    if (ImGui.Button("Reset All"))
                    {
                        _isRotating = false;
                        _isTranslating = false;
                        _applyYPR = false;
                        _applyPositionOffset = false;
                        _isLookingAt = false;
                        _isLookingAtVessel = false;
                        _isManualFollowing = false;
                        _followedObject = null;
                        _followOffset = double3.Zero;
                        _lookAtTarget = double3.Zero;
                        _positionOffset = double3.Zero;
                        _rotationAxis = new double3(0, 0, 1);
                        _rotationSpeed = 1.0f;
                        _translationSpeed = 1000000.0f;
                    }
                }
            }
            ImGui.End();
        }

        [StarMapImmediateLoad]
        public void OnImmediateLoad(Mod mod)
        {
            Console.WriteLine($"SimpleMod - OnImmediateLoad called! Mod: {mod?.GetType().Name ?? "null"}");
            Console.WriteLine($"SimpleMod - Assembly: {System.Reflection.Assembly.GetExecutingAssembly().FullName}");
            
            StartUdpServer();
        }

        [StarMapUnload]
        public void Unload()
        {
            Console.WriteLine("SimpleMod - Unload");
            StopUdpServer();
        }

        private void StartUdpServer()
        {
            _udpHandler.Start();
        }

        private void StopUdpServer()
        {
            _udpHandler.Stop();
        }

        /// <summary>
        /// Executes the orbit action: circular orbit around the followed target.
        /// </summary>
        private void ExecuteOrbitAction(Camera? camera)
        {
            if (camera == null) return;

            var target = camera.Following ?? _followedObject;
            if (target == null)
            {
                Console.WriteLine("SimpleMod - Orbit action requires a follow target");
                return;
            }

            try
            {
                // Stop current animation and clear keyframes
                _animationManager.Stop();
                _animationManager.ClearKeyframes();

                // Set up manual following with zero offset so orbit offsets work correctly
                // This ensures the camera follows the target in its local space
                _followedObject = target;
                _followOffset = double3.Zero;
                _isManualFollowing = true;

                // Enable look-at vessel tracking to keep target centered even if it moves
                // This overrides the YPR from animation keyframes to always look at target
                _isLookingAtVessel = true;
                _isLookingAt = true;
                _lastFollowedVessel = target;

                // Disable manual YPR control since look-at takes over
                _applyYPR = false;

                // Unfollow the camera so manual following takes over
                camera.Unfollow();

                // Generate orbit keyframes
                var keyframes = CameraActionGenerator.GenerateOrbit(
                    camera, target, _actionDuration, _orbitCounterClockwise, _actionEasing);

                // Add to animation manager and play
                _animationManager.AddKeyframes(keyframes);
                _animationManager.Play();

                Console.WriteLine($"SimpleMod - Started orbit animation ({_actionDuration}s, {_actionEasing})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SimpleMod - Orbit action failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes the zoom face action: zoom into character face area.
        /// </summary>
        private void ExecuteZoomFaceAction(Camera? camera)
        {
            if (camera == null) return;

            var target = camera.Following ?? _followedObject;
            if (target == null)
            {
                Console.WriteLine("SimpleMod - Zoom Face action requires a follow target");
                return;
            }

            try
            {
                _animationManager.Stop();
                _animationManager.ClearKeyframes();

                var keyframes = CameraActionGenerator.GenerateZoomFace(
                    camera, target, _actionDuration, _actionEasing);

                _animationManager.AddKeyframes(keyframes);
                _animationManager.Play();

                Console.WriteLine($"SimpleMod - Started zoom face animation ({_actionDuration}s, {_actionEasing})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SimpleMod - Zoom Face action failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes the zoom out action: move camera backward by specified distance.
        /// </summary>
        private void ExecuteZoomOutAction(Camera? camera)
        {
            if (camera == null) return;

            var target = camera.Following ?? _followedObject;
            // Zoom out can work without a target (uses absolute positions)

            try
            {
                _animationManager.Stop();
                _animationManager.ClearKeyframes();

                var keyframes = CameraActionGenerator.GenerateZoomOut(
                    camera, target, _zoomOutDistance, _actionDuration, _actionEasing);

                _animationManager.AddKeyframes(keyframes);
                _animationManager.Play();

                Console.WriteLine($"SimpleMod - Started zoom out animation ({_zoomOutDistance}m, {_actionDuration}s, {_actionEasing})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SimpleMod - Zoom Out action failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates a flyby animation by sampling the followed object's trajectory
        /// and creating keyframes that position the camera to create a cinematic flyby effect.
        /// </summary>
        private void GenerateFlybyAnimation(Camera? camera)
        {
            if (camera == null)
            {
                Console.WriteLine("SimpleMod - Cannot generate flyby: Camera is null");
                return;
            }

            // Get the followed object
            Astronomical? target = null;
            if (_followedObject is Astronomical astro)
            {
                target = astro;
            }
            else if (camera.Following is Astronomical following)
            {
                target = following;
            }

            if (target == null)
            {
                Console.WriteLine("SimpleMod - Cannot generate flyby: No target object");
                return;
            }

            // Check if object has an orbit (for trajectory prediction)
            if (!target.HasOrbit())
            {
                Console.WriteLine("SimpleMod - Cannot generate flyby: Target object has no orbit");
                return;
            }

            try
            {
                // Ensure we're following the object for the animation
                if (!_isManualFollowing)
                {
                    // Set up manual following
                    _followedObject = target;
                    _followOffset = camera.PositionEcl - target.GetPositionEcl();
                    _isManualFollowing = true;
                }
                
                // Get current simulation time
                var currentTime = Universe.GetElapsedSimTime();
                
                // Flyby parameters (configurable)
                double flybyDuration = 10.0; // seconds of animation (default 10 seconds)
                int keyframeCount = 10; // number of keyframes
                double cameraDistance = 200.0; // meters - distance from object (closer for visibility)
                double lookAheadTime = 1.0; // seconds - how far ahead to look
                
                // Get object's position at time 0 (current time) for reference
                double3 objectPosAtTime0 = target.GetPositionEcl(currentTime);
                double3 currentCameraPos = camera.PositionEcl;
                
                // Calculate initial offset relative to object
                // This is what the camera offset should be relative to the object's position
                double3 initialOffset = currentCameraPos - objectPosAtTime0;
                
                // Sample trajectory and create keyframes
                for (int i = 0; i < keyframeCount; i++)
                {
                    float timeOffset = (float)(i * flybyDuration / (keyframeCount - 1));
                    SimTime futureTime = currentTime + timeOffset;
                    
                    // Get object's future position in ECL frame
                    double3 objectPosEcl = target.GetPositionEcl(futureTime);
                    
                    // Get object's velocity to determine direction of travel
                    double3 objectVelEcl = target.GetVelocityEcl(futureTime);
                    double velMag = objectVelEcl.Length();
                    
                    // Only proceed if velocity is meaningful
                    if (velMag < 0.001)
                    {
                        Console.WriteLine($"SimpleMod - Warning: Object has very low velocity at time {timeOffset:F2}s");
                        continue;
                    }
                    
                    double3 velocityDir = objectVelEcl / velMag; // Normalized
                    
                    // Calculate camera position relative to object
                    // Position camera ahead and to the side for cinematic effect
                    double3 upEcl = new double3(0, 0, 1); // ECL up vector
                    double3 right = double3.Cross(velocityDir, upEcl);
                    double rightMag = right.Length();
                    
                    // Handle edge case where velocity is parallel to up vector
                    if (rightMag < 0.001)
                    {
                        right = new double3(1, 0, 0); // Default right vector
                    }
                    else
                    {
                        right = right / rightMag; // Normalized
                    }
                    
                    double3 up = double3.Cross(right, velocityDir).Normalized();
                    
                    // Create offset: ahead, to the side, and slightly above
                    // Vary the position for cinematic effect
                    double progress = (double)i / (keyframeCount - 1); // 0 to 1
                    double aheadOffset = cameraDistance * (0.3 + 0.4 * Math.Sin(progress * Math.PI)); // Vary ahead distance
                    double sideOffset = cameraDistance * Math.Sin(progress * Math.PI * 2.0); // S-curve side movement
                    double verticalOffset = cameraDistance * 0.2 * (1.0 - Math.Abs(progress - 0.5) * 2.0); // Arc above
                    
                    double3 relativeCameraOffset = 
                        velocityDir * aheadOffset + 
                        right * sideOffset + 
                        up * verticalOffset;
                    
                    // Calculate position offset relative to initial offset
                    // The animation system adds _positionOffset to camera.PositionEcl
                    // When following, camera.PositionEcl = targetPos + _followOffset
                    // So _positionOffset should be the change from initialOffset
                    double3 positionOffset = relativeCameraOffset - initialOffset;
                    
                    // Calculate camera position for look direction calculation
                    double3 cameraPosEcl = objectPosEcl + relativeCameraOffset;
                    
                    // Calculate camera orientation to look at object
                    // Look at a point slightly ahead of the object for smoother motion
                    SimTime lookAheadTimeSim = futureTime + lookAheadTime;
                    double3 lookAtPosEcl = target.GetPositionEcl(lookAheadTimeSim);
                    
                    double3 lookDirection = (lookAtPosEcl - cameraPosEcl);
                    double lookMag = lookDirection.Length();
                    if (lookMag > 0.001)
                    {
                        lookDirection = lookDirection / lookMag; // Normalized
                    }
                    else
                    {
                        lookDirection = -velocityDir; // Fallback
                    }
                    
                    // Create rotation quaternion to look at target using Camera's LookAtRotation
                    doubleQuat lookAtQuat = Camera.LookAtRotation(lookDirection, upEcl);
                    
                    // Convert quaternion to YPR
                    var (yaw, pitch, roll) = QuaternionToYPR(lookAtQuat);
                    
                    // Vary FOV during flyby for cinematic effect
                    // Start wide, zoom in during middle, widen at end
                    float baseFov = camera.GetFieldOfView() * 57.2958f; // Convert to degrees
                    float fovVariation = (float)(15.0 * Math.Sin(progress * Math.PI)); // ±15 degrees variation
                    float fov = baseFov + fovVariation;
                    fov = Math.Clamp(fov, 15.0f, 120.0f); // Clamp to reasonable range
                    
                    // Add keyframe with relative offset
                    _animationManager.AddKeyframe(timeOffset, positionOffset, yaw, pitch, roll, fov);
                }
                
                Console.WriteLine($"SimpleMod - Generated {keyframeCount} flyby keyframes for {target.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SimpleMod - Error generating flyby animation: {ex.Message}");
                Console.WriteLine($"SimpleMod - Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Converts a quaternion to Yaw/Pitch/Roll angles in ECL coordinate system.
        /// Matches the forward conversion: yawQuat(Z) * pitchQuat(X) * rollQuat(Y)
        /// This is extrinsic ZXY (or intrinsic YXZ).
        /// </summary>
        private (float yaw, float pitch, float roll) QuaternionToYPR(doubleQuat q)
        {
            // Convert quaternion to rotation matrix elements
            var qw = q.W;
            var qx = q.X;
            var qy = q.Y;
            var qz = q.Z;

            // Rotation matrix from quaternion:
            // R = | 1-2(qy²+qz²)   2(qxqy-qwqz)   2(qxqz+qwqy) |
            //     | 2(qxqy+qwqz)   1-2(qx²+qz²)   2(qyqz-qwqx) |
            //     | 2(qxqz-qwqy)   2(qyqz+qwqx)   1-2(qx²+qy²) |

            double r00 = 1.0 - 2.0 * (qy * qy + qz * qz);
            double r01 = 2.0 * (qx * qy - qw * qz);
            double r02 = 2.0 * (qx * qz + qw * qy);
            double r10 = 2.0 * (qx * qy + qw * qz);
            double r11 = 1.0 - 2.0 * (qx * qx + qz * qz);
            double r12 = 2.0 * (qy * qz - qw * qx);
            double r20 = 2.0 * (qx * qz - qw * qy);
            double r21 = 2.0 * (qy * qz + qw * qx);
            double r22 = 1.0 - 2.0 * (qx * qx + qy * qy);

            // For extrinsic ZXY (yaw around Z, pitch around X, roll around Y):
            // R = Rz(yaw) * Rx(pitch) * Ry(roll)
            // pitch = asin(r21)
            // yaw = atan2(-r01, r11)
            // roll = atan2(-r20, r22)

            var pitch = Math.Asin(Math.Clamp( r21, -1.0, 1.0 ));
            var yaw = Math.Atan2(-r01, r11);
            var roll = Math.Atan2(-r20, r22);

            // Convert to degrees
            return (
                (float)(yaw * 180.0 / Math.PI),
                (float)(pitch * 180.0 / Math.PI),
                (float)(roll * 180.0 / Math.PI)
            );
        }
    }
}
