using System.Collections.Generic;
using CarSales.Sim;
using CarSales.Studio;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class LicenceTests
    {
        [TestCase(LicenceType.CC0, false)]
        [TestCase(LicenceType.OwnWork, false)]
        [TestCase(LicenceType.AssetStoreStandard, false)]
        [TestCase(LicenceType.CCBYNC, true)]
        [TestCase(LicenceType.CCBYNCSA, true)]
        [TestCase(LicenceType.Editorial, true)]
        [TestCase(LicenceType.Unknown, true)]
        [TestCase(LicenceType.CCBYSA, true)]
        public void ReleaseBlocking(LicenceType t, bool blocked) =>
            Assert.AreEqual(blocked, LicencePolicy.Check(new LicenceInfo { licence = t, author = "A", purchaseRef = "1" }).BlocksRelease, t.ToString());

        [Test]
        public void CcBy_NeedsAnAuthor_PurchasedNeedsProof()
        {
            Assert.IsTrue(LicencePolicy.Check(new LicenceInfo { licence = LicenceType.CCBY }).BlocksRelease);
            Assert.AreEqual(LicenceStatus.AllowedWithAttribution, LicencePolicy.Check(new LicenceInfo { licence = LicenceType.CCBY, author = "Jane" }).Status);
            Assert.IsTrue(LicencePolicy.Check(new LicenceInfo { licence = LicenceType.RoyaltyFreePurchased }).BlocksRelease);
            Assert.IsFalse(LicencePolicy.Check(new LicenceInfo { licence = LicenceType.RoyaltyFreePurchased, purchaseRef = "INV-1" }).BlocksRelease);
            Assert.IsTrue(LicencePolicy.Check(null).BlocksRelease);
        }

        [TestCase("by", LicenceType.CCBY)]
        [TestCase("BY-NC-SA", LicenceType.CCBYNCSA)]
        [TestCase("cc0", LicenceType.CC0)]
        [TestCase("st", LicenceType.RoyaltyFreePurchased)]
        [TestCase("ed", LicenceType.Editorial)]
        [TestCase("weird", LicenceType.Unknown)]
        public void SketchfabSlugs(string slug, LicenceType expected) => Assert.AreEqual(expected, LicencePolicy.FromSketchfabSlug(slug));

        [Test]
        public void Attribution_ContainsNameAuthorAndLicence()
        {
            var info = new LicenceInfo { licence = LicenceType.CCBY, author = "Jane Doe", sourceUrl = "https://example.com/m/1" };
            string line = LicencePolicy.Attribution("Red Coupe", info);
            StringAssert.Contains("Red Coupe", line);
            StringAssert.Contains("Jane Doe", line);
            StringAssert.Contains("CC BY 4.0", line);
            StringAssert.Contains("creativecommons.org/licenses/by/4.0", line);
            Assert.AreEqual("Custom text", LicencePolicy.Attribution("x", new LicenceInfo { attributionText = " Custom text " }));
        }

        [Test]
        public void Credits_ListRequiredAttributionsFirst_AndSkipOwnWork()
        {
            var text = CreditsBuilder.Build("Game", new[]
            {
                new KeyValuePair<string, LicenceInfo>("Zeta", new LicenceInfo { licence = LicenceType.CCBY, author = "Z" }),
                new KeyValuePair<string, LicenceInfo>("Alpha", new LicenceInfo { licence = LicenceType.CCBY, author = "A" }),
                new KeyValuePair<string, LicenceInfo>("Mine", new LicenceInfo { licence = LicenceType.OwnWork }),
                new KeyValuePair<string, LicenceInfo>("Store", new LicenceInfo { licence = LicenceType.AssetStoreStandard, author = "Pub" }),
            });
            Assert.Less(text.IndexOf("Alpha"), text.IndexOf("Zeta"), "sorted");
            Assert.Less(text.IndexOf("Zeta"), text.IndexOf("Store"), "attributions first");
            Assert.IsFalse(text.Contains("Mine"));
        }
    }

    public class ImportHeuristicTests
    {
        [Test]
        public void CarInCentimetres_IsScaledDown()
        {
            var s = ScaleHeuristics.Suggest(AssetCategory.Vehicle, 180f, 140f, 450f);
            Assert.AreEqual(0.01f, s.Factor, 1e-6f);
            Assert.IsFalse(s.RotateLengthToZ);
        }

        [Test]
        public void CarInMetres_IsLeftAlone()
        {
            Assert.AreEqual(1f, ScaleHeuristics.Suggest(AssetCategory.Vehicle, 1.8f, 1.4f, 4.4f).Factor);
        }

        [Test]
        public void CarLongAlongX_IsTurned_AndZUpIsDetected()
        {
            Assert.IsTrue(ScaleHeuristics.Suggest(AssetCategory.Vehicle, 4.6f, 1.4f, 1.9f).RotateLengthToZ);
            var zUp = ScaleHeuristics.Suggest(AssetCategory.Vehicle, 1.9f, 4.6f, 1.4f);   // length ended up in Y
            Assert.IsTrue(zUp.LooksZUp);
            Assert.AreEqual(1f, zUp.Factor);
        }

        [Test]
        public void OddSize_IsScaledToTypical()
        {
            var s = ScaleHeuristics.Suggest(AssetCategory.Vehicle, 0.3f, 0.2f, 0.9f);   // a toy car, no unit factor fits
            Assert.AreEqual(4.5f / 0.9f, s.Factor, 0.001f);
        }

        [Test]
        public void CharacterInMillimetres()
        {
            Assert.AreEqual(0.001f, ScaleHeuristics.Suggest(AssetCategory.Character, 500f, 1800f, 300f).Factor, 1e-7f);
        }

        [TestCase("Car_BaseColor.png", TextureSlot.BaseColor)]
        [TestCase("body_albedo.tga", TextureSlot.BaseColor)]
        [TestCase("Body_Normal.png", TextureSlot.Normal)]
        [TestCase("tyre_nrm.jpg", TextureSlot.Normal)]
        [TestCase("Paint_Roughness.png", TextureSlot.Roughness)]
        [TestCase("Paint_Metallic.png", TextureSlot.Metallic)]
        [TestCase("Paint_MetallicSmoothness.png", TextureSlot.MetallicSmoothness)]
        [TestCase("Body_AO.png", TextureSlot.Occlusion)]
        [TestCase("Lights_Emissive.png", TextureSlot.Emission)]
        [TestCase("random.png", TextureSlot.Unknown)]
        public void TextureSlot_FromFileName(string file, TextureSlot expected) => Assert.AreEqual(expected, TextureSlots.Classify(file));

        [Test]
        public void Validation_FlagsHugeMeshes_BadScale_AndMissingWheels()
        {
            var stats = new ModelStats { Renderers = 10, Triangles = 400000, Materials = 6 };
            var issues = ImportValidation.Validate(AssetCategory.Vehicle, stats, 0.4f, new LicenceInfo { licence = LicenceType.CC0 }, false, 0);
            Assert.IsTrue(ImportValidation.HasErrors(issues));
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("triangles")));
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("Size")));
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("Wheels")));

            var good = ImportValidation.Validate(AssetCategory.Vehicle, new ModelStats { Renderers = 8, Triangles = 60000, Materials = 5 }, 4.4f,
                                                 new LicenceInfo { licence = LicenceType.CC0 }, true, 2);
            Assert.IsFalse(ImportValidation.HasErrors(good));
            Assert.AreEqual(0, good.Count);
        }

        [TestCase("Red Coupe (2019) v2.fbx", "RedCoupe2019V2")]
        [TestCase("3d-car.obj", "A3dCar")]
        [TestCase("...", "Asset")]
        public void SafeName(string input, string expected) => Assert.AreEqual(expected, StudioNames.SafeName(input));

        [Test]
        public void ModelExtensions()
        {
            Assert.IsTrue(StudioNames.IsModelFile("x/car.FBX"));
            Assert.IsTrue(StudioNames.IsModelFile("car.glb"));
            Assert.IsFalse(StudioNames.IsModelFile("car.png"));
        }
    }

    public class WheelDetectionTests
    {
        [TestCase("Wheel_FL", WheelCorner.FrontLeft)]
        [TestCase("wheel_front_right", WheelCorner.FrontRight)]
        [TestCase("RR_Wheel", WheelCorner.RearRight)]
        [TestCase("tire.rl", WheelCorner.RearLeft)]
        [TestCase("Wheel_LF", WheelCorner.FrontLeft)]
        [TestCase("Tyre Back Left", WheelCorner.RearLeft)]
        public void CornerFromName(string name, WheelCorner expected) => Assert.AreEqual(expected, WheelDetection.CornerFromName(name));

        [TestCase("Wheel_0")]
        [TestCase("Body")]
        public void CornerFromName_Unknown(string name) => Assert.IsNull(WheelDetection.CornerFromName(name));

        [TestCase("Wheel_3", true)]
        [TestCase("Tyre_FL", true)]
        [TestCase("SteeringWheel", false)]
        [TestCase("SpareWheel", false)]
        [TestCase("WheelArch_Front", false)]
        [TestCase("BrakeDisc_FL", false)]
        public void LooksLikeWheel(string name, bool expected) => Assert.AreEqual(expected, WheelDetection.LooksLikeWheel(name));

        static WheelCandidate C(int id, float x, float y, float z, string name = "w", float r = 0.33f) =>
            new WheelCandidate { Id = id, Name = name, X = x, Y = y, Z = z, Radius = r };

        [Test]
        public void AssignCorners_ByPosition()
        {
            var list = new List<WheelCandidate> { C(0, 0.8f, 0.33f, -1.3f), C(1, -0.8f, 0.33f, 1.4f), C(2, 0.8f, 0.33f, 1.4f), C(3, -0.8f, 0.33f, -1.3f) };
            var a = WheelDetection.AssignCorners(list);
            Assert.AreEqual(new[] { 1, 2, 3, 0 }, a);   // FL, FR, RL, RR
        }

        [Test]
        public void AssignCorners_PrefersLowestMesh_AndFailsWithoutAllFour()
        {
            var list = new List<WheelCandidate>
            {
                C(0, -0.8f, 0.33f, 1.4f), C(1, -0.8f, 0.9f, 1.4f),   // FL tyre + something higher (rim decal)
                C(2, 0.8f, 0.33f, 1.4f), C(3, -0.8f, 0.33f, -1.3f), C(4, 0.8f, 0.33f, -1.3f)
            };
            Assert.AreEqual(0, WheelDetection.AssignCorners(list)[0]);
            Assert.IsNull(WheelDetection.AssignCorners(list.GetRange(0, 3)));
        }
    }
}
