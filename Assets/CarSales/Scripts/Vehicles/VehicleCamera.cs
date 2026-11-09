using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    /// <summary>
    /// Driving camera added to the main camera while driving. C (CameraView action) cycles chase / far chase / bonnet.
    /// The chase views follow the car smoothly and pull in when a wall is behind the car.
    /// </summary>
    public class VehicleCamera : MonoBehaviour
    {
        enum View { Chase, FarChase, Bonnet }

        Transform target;
        InputAction viewAction;
        View view;
        Vector3 velocity;
        float yaw, carLength = 4.5f, carHeight = 1.4f;
        int obstacleMask;

        public void Init(Transform car, InputAction cameraView, Bounds carBounds)
        {
            target = car;
            viewAction = cameraView;
            carLength = Mathf.Max(3f, carBounds.size.z);
            carHeight = Mathf.Max(1f, carBounds.max.y);
            yaw = car.eulerAngles.y;
            // Ignore the car itself (layer 2), interactables (20) and the minimap/studio layers.
            obstacleMask = Physics.DefaultRaycastLayers & ~(1 << 2) & ~(1 << 20) & ~(1 << 21) & ~(1 << 22);
            SnapBehind();
        }

        void SnapBehind()
        {
            if (!target) return;
            transform.position = Desired();
            transform.rotation = Quaternion.LookRotation(LookPoint() - transform.position);
            velocity = Vector3.zero;
        }

        Vector3 Offset()
        {
            float k = carLength / 4.5f;
            return view == View.FarChase ? new Vector3(0f, 3.4f * k, -9.5f * k) : new Vector3(0f, 2.4f * k, -6.4f * k);
        }

        Vector3 LookPoint() => target.position + Vector3.up * carHeight * 0.8f;
        Vector3 Desired() => target.position + Quaternion.Euler(0f, yaw, 0f) * Offset();

        void LateUpdate()
        {
            if (!target) return;
            if (viewAction != null && viewAction.WasPressedThisFrame() && !(CarSalesServices.UI && CarSalesServices.UI.IsMenuOpen))
            {
                view = (View)(((int)view + 1) % 3);
                if (view != View.Bonnet) SnapBehind();
            }

            if (view == View.Bonnet)
            {
                transform.SetPositionAndRotation(target.TransformPoint(new Vector3(0f, carHeight * 0.85f, carLength * 0.08f)), target.rotation);
                return;
            }

            yaw = Mathf.LerpAngle(yaw, target.eulerAngles.y, 1f - Mathf.Exp(-5f * Time.deltaTime));
            Vector3 desired = Desired();
            Vector3 look = LookPoint();
            // Don't go through walls behind the car.
            Vector3 dir = desired - look;
            if (Physics.SphereCast(look, 0.3f, dir.normalized, out var hit, dir.magnitude, obstacleMask, QueryTriggerInteraction.Ignore))
                desired = look + dir.normalized * Mathf.Max(1.2f, hit.distance - 0.2f);
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, 0.08f);
            transform.rotation = Quaternion.LookRotation(look - transform.position);
        }
    }
}
