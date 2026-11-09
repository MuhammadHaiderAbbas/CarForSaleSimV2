using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CarSales.Tests
{
    /// <summary>
    /// Source checks for the UI design system (spec Phase 4): screens take every colour from the theme.
    /// Runs on the files in Assets/CarSales/Scripts/UI (found from the project folder Unity runs tests in,
    /// or from the CARSALES_ROOT environment variable).
    /// </summary>
    public class UiLintTests
    {
        static readonly string[] Allowed = { "UITheme.cs", "UIFactory.cs" };   // the theme defines colours; UIFactory has two constants

        static string UiFolder()
        {
            var starts = new List<string>();
            string env = Environment.GetEnvironmentVariable("CARSALES_ROOT");
            if (!string.IsNullOrEmpty(env)) starts.Add(env);
            starts.Add(Directory.GetCurrentDirectory());
            starts.Add(TestContext.CurrentContext.TestDirectory);
            foreach (var s in starts)
            {
                var dir = new DirectoryInfo(s);
                while (dir != null)
                {
                    string ui = Path.Combine(dir.FullName, "Assets", "CarSales", "Scripts", "UI");
                    if (Directory.Exists(ui)) return ui;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        [Test]
        public void Screens_UseThemeColours_NotNewColor()
        {
            string ui = UiFolder();
            if (ui == null) Assert.Ignore("UI source folder not found (set CARSALES_ROOT).");
            var offenders = new List<string>();
            // Literal colours: new Color(...), new Color32(...), and hex colours in rich text (<color=#...>).
            var re = new Regex(@"new\s+Color(32)?\s*\(|<color=#[0-9a-fA-F]{3,8}>");
            foreach (var file in Directory.GetFiles(ui, "*.cs", SearchOption.AllDirectories))
            {
                if (Allowed.Contains(Path.GetFileName(file))) continue;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (re.IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("//"))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
            Assert.IsEmpty(offenders, "Use UIFactory colour tokens (UITheme) instead of new Color(...):\n" + string.Join("\n", offenders));
        }

        [Test]
        public void UIFactory_OnlyDefinesTheAllowedConstants()
        {
            string ui = UiFolder();
            if (ui == null) Assert.Ignore("UI source folder not found (set CARSALES_ROOT).");
            string text = File.ReadAllText(Path.Combine(ui, "Framework", "UIFactory.cs"));
            int count = Regex.Matches(text, @"new\s+Color\s*\(").Count;
            Assert.LessOrEqual(count, 2, "UIFactory: only Clear and the dark contrast text colour may be literal; add new colours to UITheme.");
        }
    }
}
