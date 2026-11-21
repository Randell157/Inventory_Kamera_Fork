using System.Globalization;

namespace InventoryKamera
{
	// The "Delay per item" drop-down: five fixed scan speeds. Each multiplies every wait in
	// Navigation.BaseWait, so the window shows the same numbers the scan uses.
	internal static class ScanSpeeds
	{
		internal static readonly string[] Names = { "Fastest", "Fast", "Normal", "Slow", "Slowest" };
		private static readonly double[] Factors = { 0.5, 0.75, 1, 1.25, 1.5 };

		internal const int Normal = 2;
		internal static int Count => Names.Length;

		// An out-of-range step (a hand-edited settings file) falls back to Normal, as the old slider did.
		internal static int Valid(int step) => step >= 0 && step < Count ? step : Normal;

		internal static double Factor(int step) => Factors[Valid(step)];

		// Fastest keeps the third OCR worker that the old "Fast" setting had.
		internal static int Workers(int step) => Valid(step) == 0 ? 3 : 2;

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
			scanSpeed >= 0 && scanSpeed < Count ? scanSpeed : FromScannerDelay(scannerDelay);

		internal static double WaitMs(int step, Navigation.Speed speed) => Navigation.BaseWait(speed) * Factor(step);

		internal static string FormatWait(double ms) =>
			ms >= 1000 ? (ms / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " s" : System.Math.Round(ms) + " ms";

		private static string Wait(int step, Navigation.Speed speed) => FormatWait(WaitMs(step, speed));

		// "Fastest – 100 ms": the text of each choice in the drop-down.
		internal static string ItemText(int step) =>
			Names[Valid(step)] + " – " + Wait(step, Navigation.Speed.SelectNextInventoryItem);

		internal static string HintText(int step) => "= " + Workers(step) + " OCR workers";

		// Waiting added by the wait after each item alone, for 1,000 items. OCR and the other waits come on top.
		internal static string Per1000Items(int step) =>
			(WaitMs(step, Navigation.Speed.SelectNextInventoryItem) * 1000 / 60000).ToString("0.#", CultureInfo.InvariantCulture) + " min";

		// The hover text. For the current setting the heading is "Waits at this setting:"; for a choice
		// pointed at in the open list it names that choice.
		internal static string TooltipText(int step, bool forChoice = false) =>
			(forChoice ? "Waits with " + Names[Valid(step)] + ":" : "Waits at this setting:") + "\n" +
			"after each item: " + Wait(step, Navigation.Speed.SelectNextInventoryItem) + "\n" +
			"after switching character: " + Wait(step, Navigation.Speed.SelectNextCharacter) + "\n" +
			"after opening a menu: " + Wait(step, Navigation.Speed.UI) + "\n" +
			"OCR workers: " + Workers(step) + "\n" +
			"Waiting for 1,000 items: " + Per1000Items(step) + "\n" +
			"\n" +
			"Faster: the scan finishes sooner, but on a slow PC or\n" +
			"with lag a card can be read before it has loaded\n" +
			"(wrong lock, or the same item twice). Fastest also uses\n" +
			"3 OCR workers, so it needs more CPU.\n" +
			"Slower: fewer misread cards on a slow or busy PC,\n" +
			"but the scan takes longer.";

		// For the scan log: "Fastest (0.5x, 100 ms per item)".
		internal static string LogText(int step) =>
			Names[Valid(step)] + " (" + Factor(step).ToString("0.##", CultureInfo.InvariantCulture) + "x, " +
			Wait(step, Navigation.Speed.SelectNextInventoryItem) + " per item)";
	}
}
