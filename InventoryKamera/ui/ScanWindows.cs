using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace InventoryKamera
{
	// What every look's window does around a scan: flash the taskbar when it ends, open the export
	// folder, send the scan to Genshin Optimizer. Moved out of MainForm for 1.7, unchanged.
	internal static class ScanWindows
	{
		// The game usually has focus when a scan ends, and Windows won't let the window jump in front
		// of it. So flash the taskbar button until the window is brought forward, and show a summary
		// popup (OK only, no questions).
		internal static void ShowScanEnded(Form window, string title, string message, MessageBoxIcon icon)
		{
			window.BeginInvoke((MethodInvoker)delegate
			{
				FlashTaskbar(window.Handle);
				MessageBox.Show(window, message, title, MessageBoxButtons.OK, icon);
			});
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct FLASHWINFO
		{
			public uint cbSize;
			public IntPtr hwnd;
			public uint dwFlags;
			public uint uCount;
			public uint dwTimeout;
		}

		[DllImport("user32.dll")]
		private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

		private const uint FLASHW_ALL = 3;        // flash the caption and the taskbar button
		private const uint FLASHW_TIMERNOFG = 12; // keep flashing until the window comes to the foreground

		private static void FlashTaskbar(IntPtr handle)
		{
			var info = new FLASHWINFO
			{
				cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)),
				hwnd = handle,
				dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
				uCount = uint.MaxValue,
				dwTimeout = 0,
			};
			FlashWindowEx(ref info);
		}

		internal static void OpenFolder(string path)
		{
			if (!string.IsNullOrWhiteSpace(path) && (Directory.Exists(path) || Directory.CreateDirectory(path).Exists))
				Process.Start($@"{path}");
			else
				Process.Start("explorer.exe");
		}

		// Copies the scan to the clipboard and opens Genshin Optimizer. Only used by the button.
		internal static void OpenOptimizer(GOOD data)
		{
			var t = new Thread(() => Clipboard.SetText(data.ToString()));
			t.SetApartmentState(ApartmentState.STA);
			t.Start();
			t.Join();
			MessageBox.Show("Content copied to your clipboard! Paste the content into the textbox when prompted.", "Data Copied", MessageBoxButtons.OK);
			Process.Start(new ProcessStartInfo("https://frzyc.github.io/genshin-optimizer/#/setting") { UseShellExecute = true });
		}

		// The folder picker both looks use for the export folder. Null when cancelled.
		internal static string PickFolder(string current)
		{
			var d = new Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog
			{
				InitialDirectory = !Directory.Exists(current) ? Directory.GetCurrentDirectory() : current,
				IsFolderPicker = true
			};
			return d.ShowDialog() == Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok ? d.FileName : null;
		}
	}
}
