using System.Globalization;

namespace InventoryKamera
{
	// The "Delay per item" drop-down: Auto plus five fixed scan speeds. A fixed speed multiplies every
	// wait in Navigation.BaseWait, so the window shows the same numbers the scan uses. Auto skips the
	// fixed wait after each weapon or artifact and lets the card check (CardSettle) wait as long as the
	// card needs. The character screens wait until they settle (ScreenSettle), at most the Normal
	// waits; the other menus use the Normal waits.
	internal static class ScanSpeeds
	{
		internal static readonly string[] Names = { "Fastest", "Fast", "Normal", "Slow", "Slowest" };
		private static readonly double[] Factors = { 0.5, 0.75, 1, 1.25, 1.5 };

		internal const int Normal = 2;
		internal const int Auto = 5;   // saved value; shown first in the drop-down
		internal static int Count => Names.Length;   // fixed speeds
		internal static int ChoiceCount => Count + 1;

		internal static bool IsAuto(int step) => step == Auto;

		// An out-of-range step (a hand-edited settings file) falls back to Normal, as the old slider did.
		internal static int Valid(int step) => step >= 0 && step <= Auto ? step : Normal;

		internal static double Factor(int step) => IsAuto(step) ? 1 : Factors[Valid(step)];

		// Fastest keeps the third OCR worker that the old "Fast" setting had, and so does Auto.
		internal static int Workers(int step) => Valid(step) == 0 || IsAuto(step) ? 3 : 2;

		// Drop-down position <-> saved step: Auto first, then Fastest to Slowest.
		internal static int StepAt(int choice) => choice == 0 ? Auto : choice - 1;
		internal static int ChoiceOf(int step) => IsAuto(Valid(step)) ? 0 : Valid(step) + 1;

		// The old 3-position slider (setting "ScannerDelay": 0/1/2 = 0.5x/1x/1.5x) as a step with the same speed.
		internal static int FromScannerDelay(int scannerDelay)
		{
			switch (scannerDelay)
			{
				case 0: return 0;
				case 2: return 4;
				default: return Normal;
			}
		}

		// The step to use: the "ScanSpeed" setting once it's set (default -1), otherwise the old slider's
		// setting. That one is left untouched, because the 1.4.5 install reads the same settings file.
		internal static int Stored(int scanSpeed, int scannerDelay) =>
			scanSpeed >= 0 && scanSpeed <= Auto ? scanSpeed : FromScannerDelay(scannerDelay);

		internal static double WaitMs(int step, Navigation.Speed speed) => Navigation.BaseWait(speed) * Factor(step);

		internal static string FormatWait(double ms) =>
			ms >= 1000 ? (ms / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " s" : System.Math.Round(ms) + " ms";

		private static string Wait(int step, Navigation.Speed speed) => FormatWait(WaitMs(step, speed));

		internal static string Name(int step) => IsAuto(step) ? "Auto" : Names[Valid(step)];

		// "Fastest – 100 ms": the text of each choice in the drop-down.
		internal static string ItemText(int step) =>
			IsAuto(step) ? "Auto (recommended)" : Names[Valid(step)] + " – " + Wait(step, Navigation.Speed.SelectNextInventoryItem);

		// The scan speed control chosen under Options → Scan Speed Control (setting "ScanSpeedControl").
		internal const int DropDown = 0, Slider = 1;
		internal static bool IsSlider(int control) => control == Slider;   // anything else shows the drop-down

		// The slider has one step per drop-down choice, in the same order (Auto, Fastest … Slowest),
		// so a slider position is a drop-down position.
		internal static int SliderMax => ChoiceCount - 1;

		// "Speed: Auto (recommended)" or "Speed: Fast – 150 ms": the slider's heading, naming the step.
		internal static string SliderHeading(int step) =>
			"Speed: " + (IsAuto(Valid(step)) ? "Auto (recommended)" : ItemText(step));

		internal static string SliderStartLabel => Name(StepAt(0));
		internal static string SliderEndLabel => Name(StepAt(SliderMax));

		internal static string HintText(int step) =>
			IsAuto(step) ? "= waits for each card" : "= " + Workers(step) + " OCR workers";

		// Waiting added by the wait after each item alone, for 1,000 items. OCR and the other waits come on top.
		internal static string Per1000Items(int step) =>
			IsAuto(step) ? "depends on your PC" :
			(WaitMs(step, Navigation.Speed.SelectNextInventoryItem) * 1000 / 60000).ToString("0.#", CultureInfo.InvariantCulture) + " min";

		private static string AfterEachItem(int step) =>
			IsAuto(step)
				? "after each item: until its card stops changing\n(checked every " + CardSettle.PollMs + " ms, at most " + FormatWait(CardSettle.AutoGiveUpMs) + ")"
				: "after each item: " + Wait(step, Navigation.Speed.SelectNextInventoryItem);

		private const string FixedTradeoffs =
			"Faster: the scan finishes sooner, but on a slow PC or\n" +
			"with lag a card can be read before it has loaded\n" +
			"(wrong lock, or the same item twice). Fastest also uses\n" +
			"3 OCR workers, so it needs more CPU.\n" +
			"Slower: fewer misread cards on a slow or busy PC,\n" +
			"but the scan takes longer.";

		private const string AutoTradeoffs =
			"Auto: waits only as long as each card and character\n" +
			"screen needs, so it is fast and reliable, but it is new and\n" +
			"needs a live check.";

		// The hover text. For the current setting the heading is "Waits at this setting:"; for a choice
		// pointed at in the open list it names that choice.
		internal static string TooltipText(int step, bool forChoice = false) =>
			(forChoice ? "Waits with " + Name(step) + ":" : "Waits at this setting:") + "\n" +
			AfterEachItem(step) + "\n" +
			(IsAuto(step)
				? "on the character screens: until each screen stops\nchanging (at most the Normal waits)\n"
				: "after switching character: " + Wait(step, Navigation.Speed.SelectNextCharacter) + "\n") +
			"after opening a menu: " + Wait(step, Navigation.Speed.UI) + "\n" +
			"OCR workers: " + Workers(step) + "\n" +
			"Waiting for 1,000 items: " + Per1000Items(step) + "\n" +
			"\n" +
			(IsAuto(step) ? AutoTradeoffs : FixedTradeoffs);

		// For the scan log: "Fastest (0.5x, 100 ms per item)" or "Auto (waits for each card, up to 800 ms)".
		internal static string LogText(int step) =>
			IsAuto(step)
				? "Auto (waits for each card, up to " + FormatWait(CardSettle.AutoGiveUpMs) + ")"
				: Names[Valid(step)] + " (" + Factor(step).ToString("0.##", CultureInfo.InvariantCulture) + "x, " +
				  Wait(step, Navigation.Speed.SelectNextInventoryItem) + " per item)";
	}
}
