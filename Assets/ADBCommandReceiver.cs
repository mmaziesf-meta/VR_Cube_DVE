using UnityEngine;
using System;
using System.Globalization;
using System.IO;
using System.Collections;
using UnityEngine.XR;
using UnityEngine.XR.Management;

public class ADBCommandReceiver : MonoBehaviour
{
    public GameObject cube;
    private string lastCommand = "";
    private string adbFilePath = "/data/local/tmp/adb_command.txt";
    private Renderer cubeRenderer;

    private Vector3 lastHeadsetPosition;
    private Quaternion lastHeadsetRotation;

    private float movementDetectedTime = 0f;
    private bool waitingForRender = false;

    private float movementThreshold = 0.005f;
    private float rotationThreshold = 0.01f;

    void Start()
    {
        cubeRenderer = cube.GetComponent<Renderer>();
        if (cubeRenderer == null)
        {
            UnityEngine.Debug.LogError("❌ Cube does not have a Renderer component!");
        }

        UnityEngine.Debug.Log($"✅ ADBCommandReceiver started. Listening for ADB commands at: {adbFilePath}");

        if (Camera.main != null)
        {
            lastHeadsetPosition = Camera.main.transform.position;
            lastHeadsetRotation = Camera.main.transform.rotation;
        }
    }

    void Update()
    {
        if (File.Exists(adbFilePath))
        {
            string adbCommand = File.ReadAllText(adbFilePath).Trim();
            if (!string.IsNullOrEmpty(adbCommand) && adbCommand != lastCommand)
            {
                UnityEngine.Debug.Log($"📩 Received ADB Command: {adbCommand}");
                lastCommand = adbCommand;
                movementDetectedTime = Time.realtimeSinceStartup;
                waitingForRender = true;
                ApplyTransform(adbCommand);
                StartCoroutine(MeasureLatencyAfterRender());
            }
        }

        if (HasSignificantHeadsetMovement())
        {
            movementDetectedTime = Time.realtimeSinceStartup;
            waitingForRender = true;
            LogIMUData();
            LogCubeCorners();
            StartCoroutine(MeasureLatencyAfterRender());
        }
    }

    void ApplyTransform(string command)
    {
        try
        {
            UnityEngine.Debug.Log($"🔍 Parsing ADB command: {command}");

            string[] parts = command.Split(';');
            if (parts.Length < 3)
            {
                UnityEngine.Debug.LogError("❌ Invalid command format. Expected: 'x,y,z;x,y,z;x,y,z;R,G,B'");
                return;
            }

            Vector3 position = ParseVector(parts[0]);
            Vector3 rotation = ParseVector(parts[1]);
            Vector3 scale = ParseVector(parts[2]);

            cube.transform.position = position;
            cube.transform.eulerAngles = rotation;
            cube.transform.localScale = scale;

            UnityEngine.Debug.Log($"✅ Cube updated: Position={position}, Rotation={rotation}, Scale={scale}");

            if (parts.Length >= 4)
            {
                ChangeColor(parts[3]);
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"❌ Error parsing ADB command: {ex.Message}");
        }
    }

    IEnumerator MeasureLatencyAfterRender()
    {
        yield return new WaitForEndOfFrame(); // ✅ Wait until Unity finishes rendering

        float renderTime = Time.realtimeSinceStartup;
        float latency = (renderTime - movementDetectedTime) * 1000f;
        UnityEngine.Debug.Log($"⏳ Latency (Movement → Frame Rendered): {latency} ms");

        LogVRFrameTime();
        LogFullLatency();
    }

    void LogVRFrameTime()
    {
        XRDisplaySubsystem displaySubsystem = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRDisplaySubsystem>();
        if (displaySubsystem != null)
        {
            float frameTime;
            if (displaySubsystem.TryGetAppGPUTimeLastFrame(out frameTime))
            {
                UnityEngine.Debug.Log($"🔍 VR GPU Frame Time: {frameTime} ms");
            }
        }
    }

    void LogFullLatency()
    {
        #if UNITY_XR_OCULUS
        float motionToPhotonLatency;
        if (Stats.TryGetStat(XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRDisplaySubsystem>(), "motion_to_photon", out motionToPhotonLatency))
        {
            UnityEngine.Debug.Log($"⏳ **Full Motion-to-Photon Latency (Oculus): {motionToPhotonLatency} ms**");
            return;
        }
        #endif

        #if UNITY_OPENVR
        float totalLatency = OpenVR.Compositor.GetFrameTimings().m_flTotalRenderTimeMs;
        UnityEngine.Debug.Log($"⏳ **Full Motion-to-Photon Latency (SteamVR): {totalLatency} ms**");
        return;
        #endif

        UnityEngine.Debug.Log($"⚠ No built-in latency support, using manual timing.");
        StartCoroutine(MeasureManualLatency());
    }

    IEnumerator MeasureManualLatency()
    {
        yield return new WaitForEndOfFrame();
        float endTime = Time.realtimeSinceStartup;
        float fullLatency = (endTime - movementDetectedTime) * 1000f;
        UnityEngine.Debug.Log($"⏳ **Estimated Full Motion-to-Photon Latency (Fallback): {fullLatency} ms**");
    }

    void ChangeColor(string colorString)
    {
        try
        {
            string[] values = colorString.Split(',');
            if (values.Length != 3)
            {
                UnityEngine.Debug.LogError("❌ Invalid color format. Expected 'R,G,B'.");
                return;
            }

            float r = int.Parse(values[0], CultureInfo.InvariantCulture) / 255f;
            float g = int.Parse(values[1], CultureInfo.InvariantCulture) / 255f;
            float b = int.Parse(values[2], CultureInfo.InvariantCulture) / 255f;

            Color newColor = new Color(r, g, b);
            if (cubeRenderer != null)
            {
                cubeRenderer.material.color = newColor;
                UnityEngine.Debug.Log($"🎨 Cube color updated to: {newColor}");
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"❌ Error parsing color: {colorString}. Exception: {ex.Message}");
        }
    }

    void LogIMUData()
    {
        Vector3 angularVelocity;
        if (InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out angularVelocity))
        {
            UnityEngine.Debug.Log($"🔄 IMU - Gyroscope Angular Velocity: {angularVelocity}");
        }

        Vector3 deviceAcceleration;
        if (InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.deviceAcceleration, out deviceAcceleration))
        {
            UnityEngine.Debug.Log($"🚀 IMU - Device Acceleration: {deviceAcceleration}");
        }
    }

    void LogCubeCorners()
    {
        if (cubeRenderer == null)
        {
            UnityEngine.Debug.LogError("❌ Cube Renderer not found! Cannot log corners.");
            return;
        }

        Transform headsetTransform = Camera.main?.transform;
        if (headsetTransform == null)
        {
            UnityEngine.Debug.LogError("❌ No Main Camera found! Ensure your VR headset is set as the Main Camera.");
            return;
        }

        Bounds bounds = cubeRenderer.bounds;
        Vector3[] worldCorners = new Vector3[8];
        worldCorners[0] = bounds.min;
        worldCorners[1] = new Vector3(bounds.max.x, bounds.min.y, bounds.min.z);
        worldCorners[2] = new Vector3(bounds.min.x, bounds.min.y, bounds.max.z);
        worldCorners[3] = new Vector3(bounds.max.x, bounds.min.y, bounds.max.z);
        worldCorners[4] = new Vector3(bounds.min.x, bounds.max.y, bounds.min.z);
        worldCorners[5] = new Vector3(bounds.max.x, bounds.max.y, bounds.min.z);
        worldCorners[6] = new Vector3(bounds.min.x, bounds.max.y, bounds.max.z);
        worldCorners[7] = bounds.max;

        UnityEngine.Debug.Log("🎯 Cube Corners (Relative to Headset):");
        for (int i = 0; i < worldCorners.Length; i++)
        {
            Vector3 localCorner = headsetTransform.InverseTransformPoint(worldCorners[i]);
            UnityEngine.Debug.Log($"🔹 Corner {i + 1}: {localCorner}");
        }
    }

    Vector3 ParseVector(string vectorString)
    {
        try
        {
            string[] values = vectorString.Split(',');
            if (values.Length != 3)
            {
                UnityEngine.Debug.LogError("❌ Invalid vector format. Expected 'x,y,z'.");
                return Vector3.zero;
            }

            return new Vector3(
                float.Parse(values[0], CultureInfo.InvariantCulture),
                float.Parse(values[1], CultureInfo.InvariantCulture),
                float.Parse(values[2], CultureInfo.InvariantCulture)
            );
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"❌ Error parsing vector: {vectorString}. Exception: {ex.Message}");
            return Vector3.zero;
        }
    }

    bool HasSignificantHeadsetMovement()
    {
        if (Camera.main == null) return false;

        Transform headsetTransform = Camera.main.transform;
        float posDiff = Vector3.Distance(headsetTransform.position, lastHeadsetPosition);
        float rotDiff = Quaternion.Angle(headsetTransform.rotation, lastHeadsetRotation);

        bool hasMoved = posDiff > movementThreshold || rotDiff > rotationThreshold;

        if (hasMoved)
        {
            lastHeadsetPosition = headsetTransform.position;
            lastHeadsetRotation = headsetTransform.rotation;
        }

        return hasMoved;
    }

    void LogMotionToPhotonLatency()
    {
        #if UNITY_XR_OCULUS
        float motionToPhotonLatency;
        bool success = Unity.XR.Oculus.Stats.TryGetStat(
            XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRDisplaySubsystem>(),
            "motion_to_photon",
            out motionToPhotonLatency
        );

        if (success)
        {
            Debug.Log($"⚡ Motion-to-Photon Latency API Supported ✅ Value: {motionToPhotonLatency} ms");
        }
        else
        {
            Debug.Log($"❌ Motion-to-Photon Latency API Not Supported on this headset.");
        }
        #else
        Debug.Log($"⚠ `motion_to_photon` API is only available on Oculus devices.");
        #endif
    }

    void LogMotionSmoothingStatus()
    {
        #if UNITY_XR_OCULUS
        float motionSmoothing;
        bool success = Unity.XR.Oculus.Stats.TryGetStat(
            XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRDisplaySubsystem>(),
            "motion_smoothing",
            out motionSmoothing
        );

        if (success)
        {
            Debug.Log($"🔄 Motion Smoothing Active? {motionSmoothing}");
        }
        else
        {
            Debug.Log($"⚠ Could not retrieve Motion Smoothing status.");
        }
        #else
        Debug.Log($"⚠ `motion_smoothing` API is only available on Oculus devices.");
        #endif
    }
}
