using UnityEngine;

namespace CarSales.EditorTools
{
    /// <summary>
    /// Phase 5b world sites: the AUTO WORLD service centre (mechanical bay with lift, tyre bay, MOT bay, workshop
    /// board) next to the main dealership, and the Parts Direct counter in town.
    /// </summary>
    public static partial class CarSalesSetupWizard
    {
        static void BuildServiceCentre(Transform g, Mats m)
        {
            // Driveway from the road, floor, walls, open front with three bay doors.
            Solid(g, "Driveway", new Vector3(0f, 0.012f, -9.5f), new Vector3(8f, 0.024f, 19f), m.concrete);   // stops at the road
            Solid(g, "Apron", new Vector3(0f, 0.014f, -1.5f), new Vector3(24f, 0.028f, 3f), m.concrete);
            Solid(g, "Floor", new Vector3(0f, 0.02f, 8f), new Vector3(24f, 0.04f, 16f), m.concrete);
            Solid(g, "Wall_Back", new Vector3(0f, 2.75f, 16f), new Vector3(24.3f, 5.5f, 0.3f), m.wall);
            Solid(g, "Wall_W", new Vector3(-12f, 2.75f, 8f), new Vector3(0.3f, 5.5f, 16f), m.wall);
            Solid(g, "Wall_E", new Vector3(12f, 2.75f, 8f), new Vector3(0.3f, 5.5f, 16f), m.wall);
            foreach (float x in new[] { -11.7f, -4f, 4f, 11.7f })
                Solid(g, "Pillar", new Vector3(x, 2.75f, 0f), new Vector3(0.6f, 5.5f, 0.3f), m.wall);
            Solid(g, "Header", new Vector3(0f, 5f, 0f), new Vector3(24.3f, 1f, 0.3f), m.wall);
            Solid(g, "Roof", new Vector3(0f, 5.65f, 8f), new Vector3(24.8f, 0.3f, 16.8f), m.roof);
            FrontSign(g, 24.8f, 0f, 6.5f, "AUTO WORLD SERVICE CENTRE", Color.white, m.accentPanel);

            Bay(g, m, StationType.Mechanical, "Mechanical bay (lift)", -8f);
            Bay(g, m, StationType.Tyres, "Tyre bay", 0f);
            Bay(g, m, StationType.MotBay, "MOT bay", 8f);

            // Lift posts and arms (mechanical bay)
            foreach (float x in new[] { -10.5f, -5.5f })
            {
                Solid(g, "LiftPost", new Vector3(x, 1.7f, 6f), new Vector3(0.35f, 3.4f, 0.35f), m.liftYellow);
                Deco(g, "LiftArm", PrimitiveType.Cube, new Vector3(x + (x < -8f ? 0.7f : -0.7f), 0.25f, 6f), new Vector3(1.4f, 0.1f, 0.25f), m.liftYellow);
            }
            // Tyre machine and balancer (tyre bay)
            Solid(g, "TyreChanger", new Vector3(2.9f, 0.55f, 10.5f), new Vector3(0.9f, 1.1f, 0.9f), m.toolRed);
            Deco(g, "TyreChangerTop", PrimitiveType.Cylinder, new Vector3(2.9f, 1.15f, 10.5f), new Vector3(0.8f, 0.05f, 0.8f), m.metal);
            Solid(g, "WheelBalancer", new Vector3(2.9f, 0.5f, 12.4f), new Vector3(0.8f, 1f, 0.7f), m.metal);
            for (int i = 0; i < 5; i++)
                Deco(g, "Tyre", PrimitiveType.Cylinder, new Vector3(-2.9f, 0.13f + i * 0.26f, 13.6f), new Vector3(0.75f, 0.12f, 0.75f), m.tire).AddComponent<BoxCollider>();
            // MOT brake rollers and headlamp tester
            foreach (float z in new[] { 3.6f, 8.4f })
                Deco(g, "BrakeRollers", PrimitiveType.Cube, new Vector3(8f, 0.03f, z), new Vector3(2.6f, 0.02f, 0.7f), m.metal);
            Solid(g, "HeadlampTester", new Vector3(10.6f, 0.7f, 1.6f), new Vector3(0.4f, 1.4f, 0.4f), m.screen);
            SignText(g, new Vector3(8f, 4.2f, 15.8f), "MOT TESTING STATION", 4f, new Color(0.4f, 0.75f, 1f), new Vector2(6f, 0.7f));

            // Workshop board, workbench, parts shelves, tool chest
            var board = InteractBox(g, "WorkshopBoard", new Vector3(-4f, 2.1f, 15.78f), new Vector3(3.4f, 1.8f, 0.08f), m.signWhite);
            board.AddComponent<WorkshopBoardSign>();
            SignText(g, new Vector3(-4f, 3.2f, 15.7f), "WORKSHOP BOARD", 4f, new Color(0.1f, 0.15f, 0.3f), new Vector2(3.2f, 0.4f));
            foreach (float x in new[] { -5.2f, -4.1f, -3f })
                Deco(g, "BoardColumn", PrimitiveType.Cube, new Vector3(x, 1.95f, 15.72f), new Vector3(0.9f, 1.2f, 0.02f), m.desk);
            Solid(g, "Workbench", new Vector3(0f, 0.45f, 15.2f), new Vector3(3f, 0.9f, 0.8f), m.metal);
            for (int s = 0; s < 3; s++)
            {
                Solid(g, "Shelf", new Vector3(10.9f, 0.5f + s * 0.8f, 13.5f), new Vector3(1.6f, 0.06f, 3.6f), m.metal);
                for (int b = 0; b < 3; b++)
                    Deco(g, "PartBox", PrimitiveType.Cube, new Vector3(10.9f, 0.68f + s * 0.8f, 12.3f + b * 1.2f), new Vector3(0.9f, 0.3f, 0.7f), b % 2 == 0 ? m.toolRed : m.accentPanel);
            }
            Solid(g, "ToolChest", new Vector3(-10.9f, 0.55f, 13.5f), new Vector3(0.7f, 1.1f, 1.6f), m.toolRed);
            PointLight(g, new Vector3(-8f, 4.8f, 7f), 14f, 1.6f, new Color(0.95f, 0.97f, 1f));
            PointLight(g, new Vector3(0f, 4.8f, 7f), 14f, 1.6f, new Color(0.95f, 0.97f, 1f));
            PointLight(g, new Vector3(8f, 4.8f, 7f), 14f, 1.6f, new Color(0.95f, 0.97f, 1f));
            AddReflectionProbe(g, "ServiceReflections", new Vector3(0f, 2.75f, 8f), new Vector3(24f, 5.6f, 16f), 0.8f);
            AddMapPlace(g.gameObject, "AUTO WORLD Service Centre", "S", UIFactory.Warn, new Vector3(0f, 0.1f, -5f));
        }

        static void Bay(Transform g, Mats m, StationType type, string name, float x)
        {
            Deco(g, "BayLine_L", PrimitiveType.Cube, new Vector3(x - 2.6f, 0.045f, 6f), new Vector3(0.12f, 0.01f, 8f), m.yellowLine);
            Deco(g, "BayLine_R", PrimitiveType.Cube, new Vector3(x + 2.6f, 0.045f, 6f), new Vector3(0.12f, 0.01f, 8f), m.yellowLine);
            SignText(g, new Vector3(x, 4.75f, -0.2f), name.ToUpper(), 3f, new Color(1f, 0.85f, 0.3f), new Vector2(4.6f, 0.5f));
            var bay = Child("Bay_" + type, g, new Vector3(x, 0f, 6f));
            var console = InteractBox(g, "Console_" + type, new Vector3(x + 1.9f, 0.6f, 11.6f), new Vector3(0.7f, 1.2f, 0.5f), m.desk);
            Deco(g, "ConsoleScreen", PrimitiveType.Cube, new Vector3(x + 1.9f, 1.35f, 11.6f), new Vector3(0.6f, 0.4f, 0.05f), m.screen);
            var st = console.AddComponent<WorkStation>();
            st.stationType = type;
            st.displayName = name;
            st.bay = bay;
            st.baySize = new Vector3(5f, 3f, 9f);
        }

        static void BuildPartsShop(Transform g, Mats m)
        {
            Solid(g, "Floor", new Vector3(0f, 0.02f, 4.5f), new Vector3(12f, 0.04f, 9f), m.floor);
            Solid(g, "Wall_Back", new Vector3(0f, 2f, 9f), new Vector3(12.3f, 4f, 0.3f), m.wall);
            Solid(g, "Wall_W", new Vector3(-6f, 2f, 4.5f), new Vector3(0.3f, 4f, 9f), m.wall);
            Solid(g, "Wall_E", new Vector3(6f, 2f, 4.5f), new Vector3(0.3f, 4f, 9f), m.wall);
            Solid(g, "Front_W", new Vector3(-3.75f, 2f, 0f), new Vector3(4.5f, 4f, 0.3f), m.windowGlass);
            Solid(g, "Front_E", new Vector3(3.75f, 2f, 0f), new Vector3(4.5f, 4f, 0.3f), m.windowGlass);
            Solid(g, "DoorHeader", new Vector3(0f, 3.5f, 0f), new Vector3(3f, 1f, 0.3f), m.wall);
            Solid(g, "Roof", new Vector3(0f, 4.15f, 4.5f), new Vector3(12.8f, 0.3f, 9.8f), m.roof);
            FrontSign(g, 12.8f, 0f, 4.9f, "PARTS DIRECT", Color.white, m.toolRed);
            var counter = InteractBox(g, "PartsCounter", new Vector3(0f, 0.55f, 5.5f), new Vector3(4f, 1.1f, 0.8f), m.desk);
            counter.AddComponent<PartsCounter>();
            Deco(g, "Till", PrimitiveType.Cube, new Vector3(1.2f, 1.25f, 5.5f), new Vector3(0.5f, 0.3f, 0.4f), m.screen);
            for (int s = 0; s < 3; s++)
                foreach (float x in new[] { -4.5f, 4.5f })
                {
                    Solid(g, "Shelf", new Vector3(x, 0.5f + s * 0.9f, 7.6f), new Vector3(2.4f, 0.06f, 1.2f), m.metal);
                    Deco(g, "Stock", PrimitiveType.Cube, new Vector3(x, 0.72f + s * 0.9f, 7.6f), new Vector3(2f, 0.36f, 0.8f), s % 2 == 0 ? m.accentPanel : m.toolRed);
                }
            PointLight(g, new Vector3(0f, 3.6f, 4.5f), 10f, 1.4f, new Color(1f, 0.97f, 0.9f));
            AddMapPlace(g.gameObject, "Parts Direct", "P", UIFactory.Warn, new Vector3(0f, 0.1f, -3f));
        }
    
        /// <summary>Phase 7b: a cul-de-sac of six houses where private sellers show their cars on the driveway.</summary>
        static void BuildMapleClose(Transform g, Mats m)
        {
            Solid(g, "Road", new Vector3(0f, 0.012f, 9f), new Vector3(7f, 0.024f, 80f), m.asphalt);   // joins the main road without overlapping it
            Solid(g, "TurningCircle", new Vector3(0f, 0.013f, 50f), new Vector3(16f, 0.026f, 12f), m.asphalt);
            SignText(g, new Vector3(5f, 2.4f, -34f), "MAPLE CLOSE", 5f, Color.white, new Vector2(4f, 0.6f));
            Solid(g, "SignPost", new Vector3(5f, 1.1f, -33.9f), new Vector3(0.12f, 2.2f, 0.12f), m.metal);
            int n = 0;
            foreach (float side in new[] { -1f, 1f })
                foreach (float z in new[] { 2f, 20f, 38f })
                {
                    n++;
                    var mat = m.buildings[n % m.buildings.Length];
                    Solid(g, "Lawn", new Vector3(side * 14f, 0.01f, z), new Vector3(16f, 0.02f, 14f), m.ground);
                    Solid(g, "House", new Vector3(side * 18f, 3f, z), new Vector3(8f, 6f, 9f), mat);
                    var roof = Deco(g, "Roof", PrimitiveType.Cube, new Vector3(side * 18f, 6.2f, z), new Vector3(8.6f, 3.2f, 3.2f), m.flagRed);
                    roof.transform.localRotation = Quaternion.Euler(45f, 90f, 0f);
                    Deco(g, "Door", PrimitiveType.Cube, new Vector3(side * 13.97f, 1.1f, z + 2f), new Vector3(0.06f, 2.2f, 1.1f), m.desk);
                    Deco(g, "Window", PrimitiveType.Cube, new Vector3(side * 13.97f, 2.2f, z - 2f), new Vector3(0.06f, 1.2f, 1.8f), m.windowGlass);
                    Solid(g, "Driveway", new Vector3(side * 7.5f, 0.015f, z), new Vector3(8f, 0.03f, 4.5f), m.concrete);
                    var spot = Child($"SellerSpot_{n}", g, new Vector3(side * 7.5f, 0.05f, z));
                    spot.localRotation = Quaternion.Euler(0f, side > 0 ? -90f : 90f, 0f);
                    var sellerPoint = Child("SellerPoint", spot, new Vector3(-1.8f, 0f, 0.5f));
                    var ss = spot.gameObject.AddComponent<SellerSpot>();
                    ss.address = "Maple Close";
                    ss.sellerPoint = sellerPoint;
                    SignText(g, new Vector3(side * 13.9f, 2.6f, z + 3.2f), n.ToString(), 3f, Color.white, new Vector2(0.6f, 0.6f), side > 0 ? -90f : 90f);
                }
            AddMapPlace(g.gameObject, "Maple Close", "M", UIFactory.Home, new Vector3(0f, 0.1f, -30f));
        }
    }
}
