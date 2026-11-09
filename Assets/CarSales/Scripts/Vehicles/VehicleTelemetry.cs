using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    /// <summary>
    /// Developer overlay for tuning the driving (F3 while driving): speed, rpm, gear, inputs, wheel slip,
    /// which assists are working, and how the car's condition affects it.
    /// </summary>
    public class VehicleTelemetry : MonoBehaviour
    {
        public static bool Visible;

        IVehicleDriver driver;
        WheelVehicleController wheels;
        CarDisplay car;
        readonly StringBuilder sb = new StringBuilder(512);
        GUIStyle style;

        public void Init(IVehicleDriver vehicle, CarDisplay display)
        {
            driver = vehicle;
            wheels = vehicle as WheelVehicleController;
            car = display;
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k != null && k.f3Key.wasPressedThisFrame) Visible = !Visible;
        }

        void OnGUI()
        {
            if (!Visible || driver == null) return;
            if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };
            sb.Clear();
            sb.Append("<b>TELEMETRY</b> (F3)   ").Append(wheels ? wheels.Profile.name : "Arcade").Append('\n');
            sb.AppendFormat("{0,6:0} km/h   {1,5:0} rpm   gear {2}   load {3:0%}\n", Mathf.Abs(driver.Speed) * 3.6f, driver.Rpm, driver.GearLabel, driver.Load);
            if (wheels)
            {
                sb.AppendFormat("throttle {0:0%}  brake {1:0%}  steer {2,5:0.0}°  {3}\n", wheels.ThrottleInput, wheels.BrakeInput, wheels.SteerAngle,
                                wheels.Handbrake ? "HANDBRAKE" : "");
                sb.Append(wheels.AbsActive ? "<color=yellow>ABS</color> " : "ABS ")
                  .Append(wheels.TcsActive ? "<color=yellow>TCS</color> " : "TCS ")
                  .Append(wheels.EscActive ? "<color=yellow>ESC</color> " : "ESC ")
                  .Append(wheels.Limited ? "<color=orange>LIMITER</color>" : "").Append('\n');
                foreach (var w in wheels.Wheels)
                    sb.AppendFormat("{0}{1}  slip fwd {2,5:0.00}  side {3,5:0.00}  {4}\n", w.front ? "F" : "R", w.left ? "L" : "R",
                                    w.forwardSlip, w.sidewaysSlip, w.grounded ? "" : "(air)");
                var e = wheels.Effects;
                sb.AppendFormat("condition: power {0:0%}  brakes {1:0%}  grip F {2:0%} R {3:0%}  shocks {4:0%}\n",
                                e.Power, e.Brakes, e.FrontGrip, e.RearGrip, e.Damping);
            }
            if (car) sb.AppendFormat("fuel {0:0.0}%   odometer {1:N0} km   condition {2:0}%", car.Data.fuel, car.Data.mileage, car.Data.condition);
            GUI.Box(new Rect(12f, Screen.height * 0.3f, 470f, wheels ? 250f : 90f), sb.ToString(), style);
        }
    }
}
