using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Main-menu camera: slow dolly shots around the showcase car (low front three-quarter, side tracking, rear,
    /// high orbit...), blended smoothly, with a little handheld drift. The subject is framed right of centre
    /// because the menu sits on the left.
    /// </summary>
    public class MenuCinematic : MonoBehaviour
    {
        [System.Serializable]
        public struct Shot
        {
            public float startAngle, endAngle;     // degrees around the target
            public float distance, height, lookHeight, fov, seconds;
        }

        public Transform target;
        [Tooltip("Degrees the view is turned left so the car sits right of the menu.")]
        public float frameOffset = 13f;
        public float blendSeconds = 2.2f;
        public Shot[] shots =
        {
            new Shot { startAngle = 200f, endAngle = 225f, distance = 7.5f, height = 0.7f, lookHeight = 0.6f, fov = 38f, seconds = 9f },
            new Shot { startAngle = 270f, endAngle = 300f, distance = 6.5f, height = 1.3f, lookHeight = 0.7f, fov = 45f, seconds = 8f },
            new Shot { startAngle = 20f, endAngle = -10f, distance = 7f, height = 0.9f, lookHeight = 0.7f, fov = 40f, seconds = 8f },
            new Shot { startAngle = 120f, endAngle = 170f, distance = 10f, height = 4.2f, lookHeight = 0.3f, fov = 42f, seconds = 10f },
        };

        int index;
        float time;
        Camera cam;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (!target)
            {
                var showcase = FindFirstObjectByType<MenuShowcase>();
                if (showcase) target = showcase.transform;
            }
        }

        static void Pose(Shot s, float t, Vector3 centre, out Vector3 pos, out Vector3 look, out float fov)
        {
            float e = t * t * (3f - 2f * t);   // ease in-out
            float a = Mathf.Lerp(s.startAngle, s.endAngle, e) * Mathf.Deg2Rad;
            pos = centre + new Vector3(Mathf.Sin(a) * s.distance, s.height, Mathf.Cos(a) * s.distance);
            look = centre + Vector3.up * s.lookHeight;
            fov = s.fov;
        }

        void LateUpdate()
        {
            if (!target || shots == null || shots.Length == 0) return;
            time += Time.unscaledDeltaTime;
            var shot = shots[index];
            if (time >= shot.seconds) { time -= shot.seconds; index = (index + 1) % shots.Length; shot = shots[index]; }

            Vector3 c = target.position;
            Pose(shot, Mathf.Clamp01(time / shot.seconds), c, out var pos, out var look, out var fov);
            // Blend out of the previous shot's last frame at the start of each shot.
            if (time < blendSeconds && shots.Length > 1)
            {
                var prev = shots[(index - 1 + shots.Length) % shots.Length];
                Pose(prev, 1f, c, out var pPos, out var pLook, out var pFov);
                float b = time / blendSeconds;
                b = b * b * (3f - 2f * b);
                pos = Vector3.Lerp(pPos, pos, b);
                look = Vector3.Lerp(pLook, look, b);
                fov = Mathf.Lerp(pFov, fov, b);
            }
            float tt = Time.unscaledTime;
            pos += new Vector3(Mathf.PerlinNoise(tt * 0.15f, 0f) - 0.5f, Mathf.PerlinNoise(0f, tt * 0.12f) - 0.5f, 0f) * 0.12f;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(look - pos) * Quaternion.Euler(0f, -frameOffset, 0f);
            if (cam) cam.fieldOfView = fov;
        }
    }
}
