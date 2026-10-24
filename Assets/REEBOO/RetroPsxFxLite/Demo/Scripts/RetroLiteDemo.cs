// Retro PSX & CRT FX LITE - REEBOO Labs. Small demo: orbit camera, preset cycling, before/after (IMGUI, no extra packages).
using UnityEngine;
#if RFXL_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace REEBOO.RetroFx.Lite.Demo
{
    [AddComponentMenu("REEBOO/Retro FX Lite/Demo/Retro Lite Demo")]
    public sealed class RetroLiteDemo : MonoBehaviour
    {
        public RetroFxLiteController controller;
        public Transform cameraTransform;
        public Vector3 orbitCenter = new Vector3(0, 2, 0);
        public float orbitRadius = 16f, orbitHeight = 5f, orbitSpeed = 8f;
        public Transform spinner;
        private float m_Angle = 200f;

        private static bool Pressed(int key)
        {
#if RFXL_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return false;
            switch (key) { case 0: return k.eKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame; case 1: return k.qKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame; default: return k.spaceKey.wasPressedThisFrame; }
#elif ENABLE_LEGACY_INPUT_MANAGER
            switch (key) { case 0: return Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.RightArrow); case 1: return Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftArrow); default: return Input.GetKeyDown(KeyCode.Space); }
#else
            return false;
#endif
        }

        private void Update()
        {
            if (controller != null)
            {
                if (Pressed(0)) controller.Next();
                if (Pressed(1)) controller.Previous();
                if (Pressed(2)) controller.EffectsEnabled = !controller.EffectsEnabled;
            }
            if (spinner != null) spinner.Rotate(new Vector3(9f, 24f, 0f) * Time.deltaTime, Space.Self);
            if (cameraTransform != null)
            {
                m_Angle += orbitSpeed * Time.deltaTime;
                float a = m_Angle * Mathf.Deg2Rad;
                Vector3 pos = orbitCenter + new Vector3(Mathf.Cos(a) * orbitRadius, orbitHeight, Mathf.Sin(a) * orbitRadius);
                cameraTransform.SetPositionAndRotation(pos, Quaternion.LookRotation(orbitCenter - pos));
            }
        }

        private void OnGUI()
        {
            if (controller == null) return;
            GUILayout.BeginArea(new Rect(10, 10, 360, 150), GUI.skin.box);
            GUILayout.Label("RETRO PSX & CRT FX - LITE (free)");
            GUILayout.Label("Preset: " + (controller.Current != null ? controller.Current.displayName : "-"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Prev (Q)")) controller.Previous();
            if (GUILayout.Button("Next (E) >")) controller.Next();
            if (GUILayout.Button(controller.EffectsEnabled ? "FX ON (Space)" : "FX OFF (Space)")) controller.EffectsEnabled = !controller.EffectsEnabled;
            GUILayout.EndHorizontal();
            GUILayout.Label("Full version: 40 presets, VHS, CRT mask, palettes, glitch...");
            GUILayout.Label("reebo0.itch.io/retro-psx-crt-fx-unity");
            GUILayout.EndArea();
        }
    }
}
