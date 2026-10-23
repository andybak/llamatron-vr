using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.Management;
using WebXR;

namespace Llamatron.Web
{
    // WebXR Export 0.19 uses manual per-eye cameras with the Built-in renderer.
    // Native Windows/Android builds continue to use their OpenXR camera/input.
    public sealed class BrowserPlatform : MonoBehaviour
    {
        public bool Started { get; private set; }
        public bool Immersive { get; private set; }
        public bool SavePressed => Left != null && Left.enabled && Left.buttonB > 0.5f;
        public bool LoadPressed => Right != null && Right.enabled && Right.buttonB > 0.5f;
        public bool RestartPressed => Left != null && Left.enabled && Left.buttonA > 0.5f ||
                                      Right != null && Right.enabled && Right.buttonA > 0.5f;
        public WebXRControllerData Left { get; private set; }
        public WebXRControllerData Right { get; private set; }
        private Camera desktop, leftEye, rightEye;
        private XRManagerSettings loader;
        private WebXRManager manager;
        private bool supported, advertised;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void LLWebReady();
        [DllImport("__Internal")] private static extern void LLWebStatus(int supported, int immersive);
#endif

        public IEnumerator Initialize(Camera camera)
        {
            desktop = camera;
            var origin = new GameObject("WebXR origin");
            origin.transform.SetParent(transform, false);
            origin.transform.rotation = Quaternion.Euler(0, 180, 0);
            leftEye = Eye("WebXR left eye", origin.transform);
            rightEye = Eye("WebXR right eye", origin.transform);
            WebXRManager.OnXRChange += ChangeState;
            WebXRManager.OnHeadsetUpdate += Headset;
            WebXRManager.OnControllerUpdate += Controller;
            WebXRManager.OnVisibilityChange += Visibility;
            loader = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (loader != null)
            {
                yield return loader.InitializeLoader();
                if (loader.activeLoader != null)
                {
                    loader.StartSubsystems();
                    manager = gameObject.AddComponent<WebXRManager>();
                }
            }
            if (manager == null) Debug.LogWarning("[LLWEB] WebXR unavailable; desktop play remains available.");
#if UNITY_WEBGL && !UNITY_EDITOR
            LLWebReady();
#endif
            Debug.Log("[LLWEB] Browser ready; waiting for the Start gesture.");
        }

        private Camera Eye(string label, Transform origin)
        {
            var eye = new GameObject(label).AddComponent<Camera>();
            eye.CopyFrom(desktop); eye.enabled = false; eye.depth = desktop.depth + 1;
            eye.transform.SetParent(origin, false);
            return eye;
        }

        public void StartGame() => Started = true;

        public void EnterVR()
        {
            StartGame();
            if (manager != null && manager.isSupportedVR) manager.ToggleVR();
        }

        public void ExitVR()
        {
            if (manager != null && Immersive) manager.ToggleVR();
        }

        private void Update()
        {
            bool canEnter = manager != null && manager.isSupportedVR;
            if (advertised && supported == canEnter) return;
            advertised = true; supported = canEnter;
            Advertise();
        }

        private void Advertise()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LLWebStatus(supported ? 1 : 0, Immersive ? 1 : 0);
#endif
        }

        private void ChangeState(WebXRState state, int views, Rect left, Rect right)
        {
            Immersive = state == WebXRState.VR;
            desktop.enabled = !Immersive;
            leftEye.enabled = Immersive && views > 0; leftEye.rect = left;
            rightEye.enabled = Immersive && views > 1; rightEye.rect = right;
            if (!Immersive) { Left = null; Right = null; }
            Advertise();
            Debug.Log($"[LLWEB] XR state={state}; views={views}.");
        }

        private void Headset(Matrix4x4 leftProjection, Matrix4x4 rightProjection,
                             Quaternion leftRotation, Quaternion rightRotation, Vector3 leftPosition, Vector3 rightPosition)
        {
            if (!Immersive) return;
            leftEye.transform.SetLocalPositionAndRotation(leftPosition, leftRotation);
            rightEye.transform.SetLocalPositionAndRotation(rightPosition, rightRotation);
            leftEye.projectionMatrix = leftProjection; rightEye.projectionMatrix = rightProjection;
        }

        private void Controller(WebXRControllerData data)
        {
            if (data.hand == 1) Left = data;
            else if (data.hand == 2) Right = data;
        }

        private void Visibility(WebXRVisibilityState state)
        {
            // Follow the same suspension path as native application pause.
            gameObject.SendMessage("OnApplicationPause", state != WebXRVisibilityState.VISIBLE, SendMessageOptions.DontRequireReceiver);
        }

        public byte Primary(bool split)
        {
            byte input = Input(Left);
            if (!split && (input & 15) == 0) input |= (byte)(Input(Right) & 15);
            if (!split && Right != null && Right.enabled && Right.trigger > 0.5f) input |= 128;
            return input;
        }

        public byte Secondary() => Input(Right);

        private static byte Input(WebXRControllerData data)
        {
            if (data == null || !data.enabled) return 0;
            float x = data.thumbstickX, y = data.thumbstickY;
            if (x * x + y * y < 0.04f) { x = data.touchpadX; y = data.touchpadY; }
            byte bits = 0;
            if (y > 0.25f) bits |= 1; if (y < -0.25f) bits |= 2;
            if (x < -0.25f) bits |= 4; if (x > 0.25f) bits |= 8;
            if (data.trigger > 0.5f) bits |= 128;
            return bits;
        }

        private void OnDestroy()
        {
            WebXRManager.OnXRChange -= ChangeState;
            WebXRManager.OnHeadsetUpdate -= Headset;
            WebXRManager.OnControllerUpdate -= Controller;
            WebXRManager.OnVisibilityChange -= Visibility;
            if (loader != null && loader.activeLoader != null) { loader.StopSubsystems(); loader.DeinitializeLoader(); }
        }
    }
}
