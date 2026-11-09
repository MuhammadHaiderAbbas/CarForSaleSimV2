using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>Red / amber / green lamps on a pole, following TrafficRules.Light.</summary>
    public class TrafficLight : MonoBehaviour
    {
        public bool phaseA;
        Renderer red, amber, green;
        Material on, off, redM, amberM, greenM;

        public void Build()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material M(Color c) { var m = new Material(shader); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c); return m; }
            redM = M(new Color(1f, 0.1f, 0.1f)); amberM = M(new Color(1f, 0.65f, 0.05f)); greenM = M(new Color(0.1f, 1f, 0.3f));
            off = M(new Color(0.08f, 0.08f, 0.08f));
            Renderer Lamp(float y)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(s.GetComponent<Collider>());
                s.transform.SetParent(transform, false);
                s.transform.localPosition = new Vector3(0f, y, 0.12f);
                s.transform.localScale = Vector3.one * 0.22f;
                return s.GetComponent<Renderer>();
            }
            red = Lamp(3.4f); amber = Lamp(3.15f); green = Lamp(2.9f);
        }

        void Update()
        {
            if (!red) return;
            var s = TrafficRules.Light(Time.time, phaseA);
            red.sharedMaterial = s == LightState.Red ? redM : off;
            amber.sharedMaterial = s == LightState.Amber ? amberM : off;
            green.sharedMaterial = s == LightState.Green ? greenM : off;
        }

        void OnDestroy() { foreach (var m in new[] { redM, amberM, greenM, off }) if (m) Destroy(m); }
    }
}
