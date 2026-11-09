using CarSales.Studio;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class SketchfabParsingTests
    {
        const string Search = @"{
  ""next"": ""https://api.sketchfab.com/v3/search?cursor=abc"",
  ""results"": [
    { ""uid"": ""a1"", ""name"": ""Red Coupe"", ""viewerUrl"": ""https://sketchfab.com/3d-models/red-coupe-a1"", ""faceCount"": 52000, ""isDownloadable"": true,
      ""license"": { ""label"": ""CC Attribution"", ""uid"": ""x"" },
      ""user"": { ""username"": ""jane"", ""displayName"": ""Jane Doe"", ""profileUrl"": ""https://sketchfab.com/jane"" },
      ""thumbnails"": { ""images"": [ { ""url"": ""big.jpg"", ""width"": 1024 }, { ""url"": ""small.jpg"", ""width"": 256 }, { ""url"": ""tiny.jpg"", ""width"": 100 } ] } },
    { ""uid"": ""b2"", ""name"": ""Van"", ""license"": { ""slug"": ""by-nc"", ""label"": ""CC Attribution-NonCommercial"" }, ""user"": { ""username"": ""bob"" } }
  ]
}";

        [Test]
        public void ParsesSearchResults()
        {
            var page = SketchfabApi.ParseSearch(Search);
            Assert.AreEqual(2, page.Models.Count);
            Assert.AreEqual("https://api.sketchfab.com/v3/search?cursor=abc", page.Next);
            var m = page.Models[0];
            Assert.AreEqual("a1", m.Uid);
            Assert.AreEqual("Jane Doe", m.Author);
            Assert.AreEqual(LicenceType.CCBY, m.Licence);
            Assert.AreEqual(52000, m.Faces);
            Assert.AreEqual("small.jpg", m.ThumbnailUrl, "smallest image at least 200 px wide");
            Assert.AreEqual(LicenceType.CCBYNC, page.Models[1].Licence, "slug wins");
            Assert.AreEqual("bob", page.Models[1].Author, "username when there's no display name");
        }

        [TestCase("CC Attribution", LicenceType.CCBY)]
        [TestCase("CC Attribution-ShareAlike", LicenceType.CCBYSA)]
        [TestCase("CC Attribution-NoDerivs", LicenceType.CCBYND)]
        [TestCase("CC Attribution-NonCommercial", LicenceType.CCBYNC)]
        [TestCase("CC Attribution-NonCommercial-ShareAlike", LicenceType.CCBYNCSA)]
        [TestCase("CC Attribution-NonCommercial-NoDerivs", LicenceType.CCBYNCND)]
        [TestCase("CC0 Public Domain", LicenceType.CC0)]
        [TestCase("Free Standard", LicenceType.RoyaltyFreePurchased)]
        [TestCase("Editorial", LicenceType.Editorial)]
        [TestCase("https://creativecommons.org/licenses/by/4.0/", LicenceType.CCBY)]
        [TestCase("https://creativecommons.org/licenses/by-nc-sa/4.0/", LicenceType.CCBYNCSA)]
        [TestCase("", LicenceType.Unknown)]
        [TestCase("Something else", LicenceType.Unknown)]
        public void LicenceFromText(string text, LicenceType expected) => Assert.AreEqual(expected, SketchfabApi.LicenceFromText(text));

        [Test]
        public void Download_PrefersGlb()
        {
            string url = SketchfabApi.ParseDownload(@"{ ""gltf"": { ""url"": ""z.zip"" }, ""glb"": { ""url"": ""m.glb"", ""size"": 10 } }", out bool zip);
            Assert.AreEqual("m.glb", url);
            Assert.IsFalse(zip);
            url = SketchfabApi.ParseDownload(@"{ ""gltf"": { ""url"": ""z.zip"" } }", out zip);
            Assert.AreEqual("z.zip", url);
            Assert.IsTrue(zip);
            Assert.IsNull(SketchfabApi.ParseDownload("{}", out _));
        }

        [Test]
        public void Urls()
        {
            StringAssert.Contains("q=red%20car", SketchfabApi.SearchUrl("red car", true));
            StringAssert.Contains("categories=cars-vehicles", SketchfabApi.SearchUrl("x", true));
            StringAssert.DoesNotContain("categories", SketchfabApi.SearchUrl("x", false));
            Assert.AreEqual("Token abc", SketchfabApi.AuthHeader(" abc "));
        }
    }

    public class PolyHavenParsingTests
    {
        [Test]
        public void ParsesListSorted()
        {
            var list = PolyHavenApi.ParseList(@"{ ""zebra_sky"": { ""name"": ""Zebra Sky"", ""categories"": [""outdoor""], ""authors"": { ""Greg"": ""All"" },
                                                                ""thumbnail_url"": ""https://cdn.polyhaven.com/asset_img/thumbs/zebra_sky.png?width=256&format=webp&v=1"" },
                                                  ""alpha_road"": { ""name"": ""Alpha Road"", ""categories"": [""road"", ""asphalt""] } }");
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("alpha_road", list[0].Id);
            Assert.AreEqual(2, list[0].Categories.Count);
            Assert.AreEqual("Greg", list[1].Authors);
            StringAssert.Contains("format=png", list[1].Thumbnail, "Unity can't show WebP");
            StringAssert.Contains("alpha_road.png", list[0].Thumbnail, "fallback when there's no thumbnail_url");
        }

        [Test]
        public void HdriUrl_FallsBackToAvailableResolution()
        {
            const string files = @"{ ""hdri"": { ""1k"": { ""hdr"": { ""url"": ""1k.hdr"" } }, ""4k"": { ""hdr"": { ""url"": ""4k.hdr"" } } } }";
            Assert.AreEqual("4k.hdr", PolyHavenApi.HdriUrl(files, "4k"));
            Assert.AreEqual("1k.hdr", PolyHavenApi.HdriUrl(files, "2k"));
            Assert.IsNull(PolyHavenApi.HdriUrl("{}", "2k"));
        }

        [Test]
        public void TextureUrls_MapSlots()
        {
            const string files = @"{ ""Diffuse"": { ""2k"": { ""jpg"": { ""url"": ""d.jpg"" } } }, ""nor_gl"": { ""2k"": { ""png"": { ""url"": ""n.png"" } } },
                                     ""nor_dx"": { ""2k"": { ""jpg"": { ""url"": ""dx.jpg"" } } }, ""Rough"": { ""1k"": { ""jpg"": { ""url"": ""r1k.jpg"" } } }, ""blend"": {} }";
            var map = PolyHavenApi.TextureUrls(files, "2k");
            Assert.AreEqual("d.jpg", map[TextureSlot.BaseColor]);
            Assert.AreEqual("n.png", map[TextureSlot.Normal], "OpenGL normals (Unity), not DirectX");
            Assert.AreEqual("r1k.jpg", map[TextureSlot.Roughness]);
            Assert.AreEqual(3, map.Count);
            var other = PolyHavenApi.TextureUrls(@"{ ""roughness"": { ""2k"": { ""jpg"": { ""url"": ""r.jpg"" } } }, ""arm"": { ""2k"": { ""jpg"": { ""url"": ""arm.jpg"" } } } }", "2k");
            Assert.AreEqual("r.jpg", other[TextureSlot.Roughness], "undocumented names fall back to the usual naming");
            Assert.AreEqual(1, other.Count);
        }
    }

    public class ModelNamingTests
    {
        [TestCase("Body_LOD0", 0)]
        [TestCase("Body_LOD2", 2)]
        [TestCase("wheel lod1", 1)]
        [TestCase("Body", -1)]
        [TestCase("LODGE_Building", -1)]
        public void LodIndex(string name, int expected) => Assert.AreEqual(expected, ModelNaming.LodIndex(name));

        [TestCase("mixamo.com|Idle", ClipRole.Idle)]
        [TestCase("Walking", ClipRole.Walk)]
        [TestCase("Slow Run", ClipRole.Run)]
        [TestCase("Wave", ClipRole.None)]
        [TestCase("__preview__Take 001", ClipRole.None)]
        public void ClipRoles(string name, ClipRole expected) => Assert.AreEqual(expected, ModelNaming.Role(name));

        [Test]
        public void LodHeights_AreDescending()
        {
            for (int n = 1; n <= 4; n++)
            {
                var h = ModelNaming.LodHeights(n);
                Assert.AreEqual(n, h.Length);
                for (int i = 1; i < h.Length; i++) Assert.Less(h[i], h[i - 1]);
            }
        }
    }
}
