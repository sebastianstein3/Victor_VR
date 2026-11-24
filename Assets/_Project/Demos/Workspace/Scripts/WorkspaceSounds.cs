using CardboardXR.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CardboardXR.Demos.Workspace
{
    // A soft "tap" for the Workspace's buttons (windows, phone, dock), so every press has a sound, like
    // the desk keyboard's and trackpad's own clicks. One shared AudioSource plays it.
    public static class WorkspaceSounds
    {
        const float k_Volume = 0.35f;

        static AudioSource s_Source;
        static AudioClip s_Clip;

        // Set while something that already clicked (the desk trackpad) presses a button, so the two
        // sounds don't stack.
        public static bool Muted { get; set; }

        // Any button that has the tap was pressed. WorkspaceDemo uses it to make the window the button is
        // in the active one: a pinch on a button goes to the button, not to the demo (PinchRouter.Order).
        public static event System.Action<Button> Pressed;

        // Gives every M3Button under root (that has none yet) the tap. Call it after building UI;
        // the desk keyboard doesn't use it, its keys click by themselves.
        public static void Attach(Component root)
        {
            if (root == null)
                return;
            foreach (var button in root.GetComponentsInChildren<M3Button>(true))
            {
                if (button.GetComponent<ClickSound>() == null)
                    button.gameObject.AddComponent<ClickSound>();
            }
        }

        public static void PlayTap()
        {
            if (Muted)
                return;
            if (s_Source == null)
            {
                var go = new GameObject("WorkspaceClickSounds");
                s_Source = go.AddComponent<AudioSource>();
                s_Source.playOnAwake = false;
                s_Source.spatialBlend = 0f;
            }
            if (s_Clip == null)
                s_Clip = CreateTapClip();
            s_Source.PlayOneShot(s_Clip, k_Volume);
        }

        // Removes the shared source when the demo ends.
        public static void Release()
        {
            if (s_Source != null)
                Object.Destroy(s_Source.gameObject);
            s_Source = null;
        }

        static AudioClip CreateTapClip()
        {
            const int rate = 44100;
            const float seconds = 0.03f;
            int count = (int)(rate * seconds);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                // A short, soft tone dropping from 900 to 600 Hz, lower and rounder than the key click
                samples[i] = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(900f, 600f, t) * (i / (float)rate)) * Mathf.Exp(-t * 9f);
            }
            var clip = AudioClip.Create("UiTap", count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // On a button: plays the tap when it is pressed.
        class ClickSound : MonoBehaviour
        {
            void Awake()
            {
                var button = GetComponent<Button>();
                if (button != null)
                    button.onClick.AddListener(() =>
                    {
                        PlayTap();
                        Pressed?.Invoke(button);
                    });
            }
        }
    }
}
