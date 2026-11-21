using System;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InventoryKamera.Tests
{
    // Item 8: every status color and the output panel's link and error text keep WCAG AA
    // contrast (4.5:1; the 15 pt status text is not "large text").
    [TestClass]
    public sealed class StatusColorTests
    {
        private static readonly Color Panel = Color.FromArgb(76, 86, 104);
        private static readonly Color ErrorBox = SystemColors.Control;

        private static double Luminance(Color c)
        {
            double F(int v) { double s = v / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * F(c.R) + 0.7152 * F(c.G) + 0.0722 * F(c.B);
        }

        internal static double Contrast(Color a, Color b)
        {
            double x = Luminance(a), y = Luminance(b);
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }

        [TestMethod]
        public void ContrastMatchesKnownValues()
        {
            Assert.AreEqual(21.0, Contrast(Color.Black, Color.White), 0.01);
            Assert.AreEqual(4.15, Contrast(Color.Green, UserInterface.WindowColor), 0.01, "the old green");
            Assert.AreEqual(3.23, Contrast(Color.Red, UserInterface.WindowColor), 0.01, "the old red");
        }

        [TestMethod]
        public void StatusColorsReadOnTheWindow()
        {
            foreach (var (name, color) in new[] { ("ok", UserInterface.OkColor), ("error", UserInterface.ErrorColor), ("preparing", UserInterface.PreparingColor) })
                Assert.IsTrue(Contrast(color, UserInterface.WindowColor) >= 4.5, $"{name}: {Contrast(color, UserInterface.WindowColor):0.00}:1");
        }

        [TestMethod]
        public void DesignerColorsMatchAndRead()
        {
            var designer = File.ReadAllText(Path.Combine(RepoRoot(), "InventoryKamera", "ui", "main", "MainForm.Designer.cs"));
            Assert.AreEqual(UserInterface.WindowColor, ColorOf(designer, "this.BackColor"), "the window's background");
            Assert.AreEqual(Panel, ColorOf(designer, "this.ScannerOutput_Panel.BackColor"), "the output panel");
            Assert.AreEqual(UserInterface.OkColor, ColorOf(designer, "this.ProgramStatus_Label.ForeColor"), "the status label's starting color");

            var link = ColorOf(designer, "this.IssuesPage_Label.LinkColor");
            Assert.IsTrue(Contrast(link, Panel) >= 4.5, $"Issue Page link: {Contrast(link, Panel):0.00}:1");
            var errorText = ColorOf(designer, "this.ErrorLog_TextBox.ForeColor");
            Assert.IsTrue(Contrast(errorText, ErrorBox) >= 4.5, $"error box text: {Contrast(errorText, ErrorBox):0.00}:1");
        }

        private static Color ColorOf(string designer, string property)
        {
            var m = Regex.Match(designer, Regex.Escape(property) + @" = System\.Drawing\.Color\.FromArgb\(\(\(int\)\(\(\(byte\)\((\d+)\)\)\)\), \(\(int\)\(\(\(byte\)\((\d+)\)\)\)\), \(\(int\)\(\(\(byte\)\((\d+)\)\)\)\)\);");
            Assert.IsTrue(m.Success, property + " is set with FromArgb");
            return Color.FromArgb(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InventoryKamera.sln"))) dir = dir.Parent;
            Assert.IsNotNull(dir, "the repository root");
            return dir.FullName;
        }
    }
}
