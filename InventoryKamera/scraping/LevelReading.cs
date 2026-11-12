using System.Linq;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    /// <summary>Result of reading a "Level 80 / 90" (or "Lv. 80/90") text.</summary>
    internal enum LevelReadResult
    {
        /// <summary>Both numbers read, and the cap is valid for the level.</summary>
        Valid,
        /// <summary>The level was read, but no valid cap could be found.</summary>
        LevelOnly,
        /// <summary>No level could be read.</summary>
        Unreadable,
    }

    /// <summary>
    /// Reads level and level cap from OCR text, checking the cap against the game's real caps.
    /// The old parser kept only digits and "/", so "Level 80 / 9C" became 80/9, a cap of 10,
    /// and the character was exported one ascension too low.
    /// </summary>
    internal static class LevelReading
    {
        internal static readonly int[] CharacterCaps = { 20, 40, 50, 60, 70, 80, 90, 95, 100 };
        internal static readonly int[] WeaponCaps = { 20, 40, 50, 60, 70, 80, 90 };

        // Level number, then whatever OCR made of the slash, then the cap.
        // The slash is often read as "/", "J", "]", ":", "_", "|" or "\"; a lone "7", "1", "l" or "I"
        // between spaces ("80 7 90") is also a misread slash.
        private static readonly Regex LevelAndCap = new Regex(
            @"(?<level>\d{1,3})\s*(?:[/\\|J\]\[_:.\-]+|[71lI](?=\s+\d))?\s*[/\\|J\]\[_:.\-]*\s*(?<cap>\d[0-9CcOo()\[\]r]*)?",
            RegexOptions.Compiled);

        /// <summary>
        /// Reads level and cap. <paramref name="cap"/> is 0 unless the result is <see cref="LevelReadResult.Valid"/>.
        /// </summary>
        internal static LevelReadResult Read(string text, int[] caps, out int level, out int cap)
        {
            level = 0;
            cap = 0;
            if (string.IsNullOrWhiteSpace(text)) return LevelReadResult.Unreadable;

            var match = LevelAndCap.Match(text);
            // A number after the slash is the cap, never the level ("Level / 90" has no level).
            bool levelAfterSlash = match.Success && text.Substring(0, match.Index).Contains("/");
            if (!match.Success || levelAfterSlash || !int.TryParse(match.Groups["level"].Value, out level) || level < 1 || level > caps.Max())
            {
                level = 0;
                return LevelReadResult.Unreadable;
            }

            if (match.Groups["cap"].Success)
            {
                // Brackets, C and O are misread zeros ("9C", "9()", "9(r", "9)").
                string digits = match.Groups["cap"].Value.Replace("()", "0");
                digits = Regex.Replace(digits, "[CcOo()\\[\\]]", "0");
                digits = Regex.Replace(digits, "[^0-9]", string.Empty);

                // Trailing junk can add digits ("90)" -> "900"): try 3 digits, then 2.
                foreach (int length in new[] { 3, 2 })
                {
                    if (digits.Length < length) continue;
                    if (int.TryParse(digits.Substring(0, length), out int candidate) && Fits(level, candidate, caps))
                    {
                        cap = candidate;
                        return LevelReadResult.Valid;
                    }
                }
            }
            return LevelReadResult.LevelOnly;
        }

        /// <summary>A cap is valid if it is a real cap and the level lies between the previous cap and it.</summary>
        internal static bool Fits(int level, int cap, int[] caps)
        {
            int index = System.Array.IndexOf(caps, cap);
            if (index < 0) return false;
            int lowest = index == 0 ? 1 : caps[index - 1];
            return lowest <= level && level <= cap;
        }

        /// <summary>Same rule as before: ascended once past level 20 and below the cap (e.g. 80/90).</summary>
        internal static bool IsAscended(int level, int cap) => 20 <= level && level < cap;
    }
}
