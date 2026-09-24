using HideVolumeOSD.Properties;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HideVolumeOSD
{
	public class HideVolumeOSDLib
	{
		[DllImport("user32.dll")]
		private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		private static extern bool IsWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern bool IsIconic(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool SystemParametersInfo(uint action, IntPtr param, [Out] out RECT rect, IntPtr init);

		[DllImport("user32.dll")]
		static extern int GetSystemMetrics(int sm);


		const int SM_CXSCREEN = 0;
		const int SM_CYSCREEN = 1;

		const int SW_MINIMIZE = 6;
		const int SW_RESTORE = 9;

		const uint KEYEVENTF_KEYUP = 0x0002;

		const int SPI_GETWORKAREA = 0x0030;


		private struct NOTIFYICONIDENTIFIER
		{
			public uint cbSize;
			public IntPtr hWnd;
			public uint uID;
			public Guid guidItem;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int left;
			public int top;
			public int right;
			public int bottom;
		}

		[DllImport("Shell32.dll", SetLastError = true)]
		private static extern Int32 Shell_NotifyIconGetRect([In] ref NOTIFYICONIDENTIFIER identifier, [Out] out RECT iconLocation);

		// ---- startup / re-attach behaviour ---------------------------------------------------------

		// The OSD window only exists once Explorer has fully started (and on Windows 10 often only after
		// the first volume change). Directly after logon it is therefore usually NOT there yet. Instead of
		// giving up (older versions quit silently), we keep looking in the background.

		// Seconds after the start of an attach cycle at which we may "poke" Windows (mute toggle twice) to make
		// it create its OSD window. Poking is limited so nobody gets repeated audio clicks.
		static readonly int[] TriggerScheduleSeconds = { 0, 15, 45, 90, 180 };

		const int AttachRetryIntervalMs = 3000;   // how often we look while we are still trying hard
		const int AttachActiveSeconds = 300;      // ... for this long
		const int AttachIdleIntervalMs = 30000;   // after that we only look passively (no poking), slowly
		const int WatchdogIntervalMs = 5000;      // while hidden: is the window still there / still minimized?

		NotifyIcon notifyIcon;
		NOTIFYICONIDENTIFIER notifyIconIdentifier;
		string defaultTrayText = "";

		IntPtr hWndInject = IntPtr.Zero;

		VolumePoup volumePopup = new VolumePoup();

		System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();
		System.Windows.Forms.Timer attachTimer = new System.Windows.Forms.Timer();
		System.Windows.Forms.Timer watchdogTimer = new System.Windows.Forms.Timer();

		bool initialized = false;
		bool keyHookActive = false;
		bool gaveUpMessageShown = false;
		int triggerCount = 0;
		int reminimizeLogCount = 0;
		int watchdogReattachCount = 0;
		DateTime attachStart = DateTime.UtcNow;

		static int cachedBuildNumber = -1;

		public HideVolumeOSDLib(NotifyIcon ni)
		{
			if (ni != null)
			{
				this.notifyIcon = ni;
			}
		}

		/// <summary>
		/// Tray mode: called once. Never blocks for long and never terminates the application;
		/// if the OSD window cannot be found yet, it keeps searching in the background.
		/// </summary>
		public void Init()
		{
			if (initialized)
			{
				return;
			}

			initialized = true;

			Log.Write("Init (Windows build " + GetWindowsBuildNumber() + ", exe " + Application.ExecutablePath + ")");

			Application.ApplicationExit += Application_ApplicationExit;

			if (notifyIcon != null)
			{
				defaultTrayText = notifyIcon.Text;
				CaptureNotifyIconIdentifier();
			}

			hideTimer.Tick += HideTimer_Tick;

			attachTimer.Interval = AttachRetryIntervalMs;
			attachTimer.Tick += AttachTimer_Tick;

			watchdogTimer.Interval = WatchdogIntervalMs;
			watchdogTimer.Tick += WatchdogTimer_Tick;

			ApplyKeyHookSetting();

			StartAttaching(true);
		}

		/// <summary>
		/// Command line mode (-hide / -show): wait (blocking) until the OSD window is available.
		/// </summary>
		public bool AttachBlocking(int timeoutMs)
		{
			Stopwatch sw = Stopwatch.StartNew();

			attachStart = DateTime.UtcNow;
			triggerCount = 0;

			while (true)
			{
				if (TryAttach(true))
				{
					return true;
				}

				if (sw.ElapsedMilliseconds > timeoutMs)
				{
					return false;
				}

				Thread.Sleep(1000);
			}
		}

		/// <summary>
		/// The low level keyboard hook is only needed for the "volume in system tray" popup, so it is only
		/// installed while that option is on (a global keyboard hook is also something virus scanners
		/// look at with suspicion, so we avoid it when it is not needed).
		/// </summary>
		public void ApplyKeyHookSetting()
		{
			bool want = Settings.Default.VolumeInSystemTray;

			try
			{
				if (want && !keyHookActive)
				{
					KeyHook.VolumeKeyPressed += KeyHook_VolumeKeyPressed;
					KeyHook.VolumeKeyReleased += KeyHook_VolumeKeyReleased;
					KeyHook.StartListening();
					keyHookActive = true;
					Log.Write("Keyboard hook installed");
				}
				else
					if (!want && keyHookActive)
					{
						KeyHook.VolumeKeyPressed -= KeyHook_VolumeKeyPressed;
						KeyHook.VolumeKeyReleased -= KeyHook_VolumeKeyReleased;
						KeyHook.StopListening();
						keyHookActive = false;
						hideTimer.Stop();
						showVolumeWindow(false);
						Log.Write("Keyboard hook removed");
					}
			}
			catch (Exception ex)
			{
				Log.Write("ApplyKeyHookSetting failed", ex);
			}
		}

		private void CaptureNotifyIconIdentifier()
		{
			try
			{
				FieldInfo idFieldInfo = notifyIcon.GetType().GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
				int iconID = (int)idFieldInfo.GetValue(notifyIcon);

				FieldInfo windowFieldInfo = notifyIcon.GetType().GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
				NativeWindow nativeWindow = (NativeWindow)windowFieldInfo.GetValue(notifyIcon);
				IntPtr iconhandle = nativeWindow.Handle;

				notifyIconIdentifier = new NOTIFYICONIDENTIFIER()
				{
					hWnd = iconhandle,
					uID = (uint)iconID
				};

				notifyIconIdentifier.cbSize = (uint)Marshal.SizeOf(notifyIconIdentifier);
			}
			catch (Exception ex)
			{
				// not fatal: the volume popup is then placed near the clock instead of above the tray icon
				Log.Write("Could not get tray icon identifier", ex);
			}
		}

		// ---- finding the OSD window ----------------------------------------------------------------

		private static int GetWindowsBuildNumber()
		{
			if (cachedBuildNumber >= 0)
			{
				return cachedBuildNumber;
			}

			int build = 0;

			try
			{
				object value = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", null);

				if (value == null || !int.TryParse(value.ToString(), out build))
				{
					build = 0;
				}
			}
			catch
			{
				build = 0;
			}

			if (build == 0)
			{
				try
				{
					string desc = RuntimeInformation.OSDescription;
					int.TryParse(desc.Substring(desc.LastIndexOf('.') + 1), out build);
				}
				catch
				{
					build = 0;
				}
			}

			cachedBuildNumber = build;
			return build;
		}

		private IntPtr FindOSDWindow()
		{
			bool win11 = GetWindowsBuildNumber() >= 22000;

			string outerClass = win11 ? "XamlExplorerHostIslandWindow" : "NativeHWNDHost";
			string innerClass = win11 ? "Windows.UI.Composition.DesktopWindowContentBridge" : "DirectUIHWND";
			string innerName = win11 ? "DesktopWindowXamlSource" : "";

			// 1st pass: exactly the criteria of earlier versions

			IntPtr hwnd = internalFind(outerClass, "", innerClass, innerName);

			// 2nd pass: same window classes, but tolerate other window titles
			// (Windows updates have changed the titles before)

			if (hwnd == IntPtr.Zero)
			{
				hwnd = internalFind(outerClass, null, innerClass, null);

				if (hwnd != IntPtr.Zero)
				{
					Log.Write("OSD window found with relaxed criteria (window titles differ from expected)");
				}
			}

			return hwnd;
		}

		private IntPtr internalFind(String outerClass, String outerName, String innerClass, String innerName)
		{
			IntPtr best = IntPtr.Zero;
			int bestScore = -1;
			int pairCount = 0;

			IntPtr hwndFound = IntPtr.Zero;

			// search for all windows with outerClass and outerName
			// (the hwndFound cursor makes FindWindowEx walk through all of them)

			while (pairCount < 64 && (hwndFound = FindWindowEx(IntPtr.Zero, hwndFound, outerClass, outerName)) != IntPtr.Zero)
			{
				// the real OSD host has a child of the expected kind

				if (FindWindowEx(hwndFound, IntPtr.Zero, innerClass, innerName) == IntPtr.Zero)
				{
					continue;
				}

				pairCount++;

				// There can be several look-alike windows (Windows 11 has more than one XAML island host).
				// Prefer one that has a real size and is currently visible (the OSD is visible right after
				// we triggered it); on a tie the first one wins, as in earlier versions.

				int score = 0;
				RECT rc;

				if (GetWindowRect(hwndFound, out rc) && rc.right > rc.left && rc.bottom > rc.top)
				{
					score += 2;
				}

				if (IsWindowVisible(hwndFound))
				{
					score += 1;
				}

				if (score > bestScore)
				{
					best = hwndFound;
					bestScore = score;
				}
			}

			if (pairCount > 1)
			{
				Log.Write("Found " + pairCount + " OSD window candidates, using 0x" + best.ToString("X") + " (score " + bestScore + ")");
			}

			return best;
		}

		/// <summary>
		/// Makes Windows create / show its volume OSD by toggling mute twice.
		/// Unlike volume up/down this leaves the volume level (and the mute state) exactly as it was.
		/// </summary>
		private void TriggerOSD()
		{
			Log.Write("Triggering OSD (mute toggle x2)");

			PressKey(Keys.VolumeMute);
			Thread.Sleep(250);
			PressKey(Keys.VolumeMute);
		}

		private static void PressKey(Keys key)
		{
			keybd_event((byte)key, 0, 0, 0);
			keybd_event((byte)key, 0, KEYEVENTF_KEYUP, 0);
		}

		/// <summary>
		/// One attempt to find (and if allowed, provoke) the OSD window. Never throws.
		/// </summary>
		private bool TryAttach(bool allowTrigger)
		{
			try
			{
				if (hWndInject != IntPtr.Zero && IsWindow(hWndInject))
				{
					return true;
				}

				hWndInject = IntPtr.Zero;

				// Nothing to look for before the desktop (taskbar) exists.

				if (FindWindow("Shell_TrayWnd", null) == IntPtr.Zero)
				{
					return false;
				}

				hWndInject = FindOSDWindow();

				if (hWndInject == IntPtr.Zero && allowTrigger && triggerCount < TriggerScheduleSeconds.Length)
				{
					double elapsed = (DateTime.UtcNow - attachStart).TotalSeconds;

					if (elapsed >= TriggerScheduleSeconds[triggerCount])
					{
						triggerCount++;

						TriggerOSD();

						for (int i = 0; i < 12 && hWndInject == IntPtr.Zero; i++)
						{
							Thread.Sleep(150);
							hWndInject = FindOSDWindow();
						}
					}
				}

				if (hWndInject == IntPtr.Zero)
				{
					return false;
				}

				Log.Write("OSD window found: 0x" + hWndInject.ToString("X"));
				return true;
			}
			catch (Exception ex)
			{
				Log.Write("TryAttach failed", ex);
				hWndInject = IntPtr.Zero;
				return false;
			}
		}

		/// <summary>
		/// For an explicit user request (menu / click): try hard, right now.
		/// </summary>
		private bool EnsureAttached()
		{
			if (hWndInject != IntPtr.Zero && IsWindow(hWndInject))
			{
				return true;
			}

			attachStart = DateTime.UtcNow;
			triggerCount = 0;

			return TryAttach(true);
		}

		// ---- background attach / watchdog ------------------------------------------------------------

		private void StartAttaching(bool allowTriggers)
		{
			attachStart = DateTime.UtcNow;
			triggerCount = allowTriggers ? 0 : TriggerScheduleSeconds.Length;
			gaveUpMessageShown = false;

			watchdogTimer.Stop();
			attachTimer.Stop();
			attachTimer.Interval = AttachRetryIntervalMs;

			if (TryAttach(true))
			{
				OnAttached();
			}
			else
			{
				Log.Write("OSD window not available yet, will keep trying in the background");

				if (notifyIcon != null)
				{
					notifyIcon.Text = "HideVolumeOSD - waiting for Windows...";
				}

				attachTimer.Start();
			}
		}

		private void AttachTimer_Tick(object sender, EventArgs e)
		{
			attachTimer.Stop();

			double elapsed = (DateTime.UtcNow - attachStart).TotalSeconds;
			bool active = elapsed <= AttachActiveSeconds;

			if (TryAttach(active))
			{
				OnAttached();
				return;
			}

			if (!active)
			{
				// we did our best: keep looking, but slowly and without poking Windows

				attachTimer.Interval = AttachIdleIntervalMs;

				if (!gaveUpMessageShown)
				{
					gaveUpMessageShown = true;

					Log.Write("OSD window still not found after " + AttachActiveSeconds + "s, continuing passively");

					if (notifyIcon != null)
					{
						notifyIcon.Text = "HideVolumeOSD - OSD window not found";
						notifyIcon.ShowBalloonTip(5000, "HideVolumeOSD", "Windows' volume OSD window was not found yet. HideVolumeOSD keeps looking in the background. Left-click the tray icon to try again.", ToolTipIcon.Warning);
					}
				}
			}

			attachTimer.Start();
		}

		private void OnAttached()
		{
			attachTimer.Stop();

			if (notifyIcon != null)
			{
				notifyIcon.Text = defaultTrayText;

				// apply the remembered state

				if (Settings.Default.HideOSD)
					HideOSD();
				else
					ShowOSD();
			}

			watchdogTimer.Start();
		}

		private void WatchdogTimer_Tick(object sender, EventArgs e)
		{
			try
			{
				if (!Settings.Default.HideOSD)
				{
					return;
				}

				if (hWndInject == IntPtr.Zero || !IsWindow(hWndInject))
				{
					// Explorer restarted, or Windows re-created the OSD window (e.g. after an update or a
					// very long uptime): find it again and hide it again.

					Log.Write("OSD window handle is gone, looking for it again");

					hWndInject = IntPtr.Zero;

					// only the first few times we may poke Windows to re-create it; after that just look passively,
					// so a misbehaving system can never cause repeated mute clicks

					StartAttaching(watchdogReattachCount++ < 3);
					return;
				}

				if (!IsIconic(hWndInject))
				{
					if (reminimizeLogCount++ < 10)
					{
						Log.Write("OSD window was restored by Windows, minimizing it again");
					}

					ShowWindow(hWndInject, SW_MINIMIZE);
				}
			}
			catch (Exception ex)
			{
				Log.Write("Watchdog failed", ex);
			}
		}

		private void Application_ApplicationExit(object sender, EventArgs e)
		{
			try
			{
				attachTimer.Stop();
				watchdogTimer.Stop();
				hideTimer.Stop();

				volumePopup.Stop();

				if (keyHookActive)
				{
					KeyHook.StopListening();
					keyHookActive = false;
				}

				// give the user back the normal OSD (but don't provoke a new one if the window is gone)

				if (hWndInject != IntPtr.Zero && IsWindow(hWndInject))
				{
					ShowWindow(hWndInject, SW_RESTORE);
				}

				Log.Write("Exit");
			}
			catch (Exception ex)
			{
				Log.Write("Exit handler failed", ex);
			}
		}

		// ---- volume popup in the system tray ---------------------------------------------------------

		private void KeyHook_VolumeKeyPressed(object sender, EventArgs e)
		{
			if (Settings.Default.VolumeInSystemTray && Settings.Default.HideOSD)
			{
				hideTimer.Stop();
				showVolumeWindow(true);
			}
		}

		private void KeyHook_VolumeKeyReleased(object sender, EventArgs e)
		{
			if (Settings.Default.VolumeInSystemTray && Settings.Default.HideOSD)
			{
				hideTimer.Interval = Settings.Default.VolumeHideDelay;
				hideTimer.Start();
			}
		}

		private void HideTimer_Tick(object sender, EventArgs e)
		{
			hideTimer.Stop();

			if (Settings.Default.VolumeInSystemTray)
			{
				showVolumeWindow(false);
			}
		}

		// ---- public API ------------------------------------------------------------------------------

		public void HideOSD()
		{
			if (EnsureAttached())
			{
				ShowWindow(hWndInject, SW_MINIMIZE);

				if (!watchdogTimer.Enabled && initialized)
				{
					watchdogTimer.Start();
				}
			}
			else
			{
				ReportNotFound();
			}

			if (notifyIcon != null)
				notifyIcon.Icon = Resources.IconDisabled;
		}

		public void ShowOSD()
		{
			if (EnsureAttached())
			{
				ShowWindow(hWndInject, SW_RESTORE);

				hideTimer.Stop();
				showVolumeWindow(false);
			}
			else
			{
				ReportNotFound();
			}

			if (notifyIcon != null)
				notifyIcon.Icon = Resources.Icon;
		}

		/// <summary>
		/// An explicit hide/show request could not be carried out because the window is missing:
		/// tell the user and keep trying in the background (the remembered state is applied once found).
		/// </summary>
		private void ReportNotFound()
		{
			Log.Write("OSD window not found");

			if (notifyIcon != null)
			{
				notifyIcon.ShowBalloonTip(5000, "HideVolumeOSD", "Windows' volume OSD window was not found yet. HideVolumeOSD will keep trying in the background.", ToolTipIcon.Warning);

				if (initialized && !attachTimer.Enabled)
				{
					attachStart = DateTime.UtcNow;
					triggerCount = 0;
					attachTimer.Interval = AttachRetryIntervalMs;
					attachTimer.Start();
				}
			}
		}

		public void showVolumeWindow(bool bShow)
		{
			if (bShow)
			{
				RECT rect = new RECT();

				bool bOverIcon = false;

				if (Shell_NotifyIconGetRect(ref notifyIconIdentifier, out rect) != 0 || Settings.Default.VolumeDisplayNearClock)
				{
					RECT rcDesktop = new RECT();
					SystemParametersInfo(SPI_GETWORKAREA, IntPtr.Zero, out rcDesktop, IntPtr.Zero);

					int cx = GetSystemMetrics(SM_CXSCREEN);
					int cy = GetSystemMetrics(SM_CYSCREEN);

					int taskBarHeight = cy - rcDesktop.bottom;

					rect.left = (int)(cx - taskBarHeight * 1.8);
					rect.right = cx;

					rect.top = rcDesktop.bottom;
					rect.bottom = cy;
				}
				else
				{
					bOverIcon = true;
				}

				int height = rect.bottom - rect.top;

				switch (Settings.Default.VolumeDisplaySize)
				{
					case 0:

						height = (int)(height / 2.75);
						break;

					case 1:

						height = (int)(height / 2);
						break;

					case 2:

						height = (int)(height / 1.2);
						break;
				}

				int width = (int)(height * 1.8);

				volumePopup.Show();
				volumePopup.Size = new Size(width, height);

				if (bOverIcon)
					volumePopup.Location = new Point(rect.left + (rect.right - rect.left) / 2 - width / 2, rect.top + (rect.bottom - rect.top) / 2 - height / 2);
				else
					volumePopup.Location = new Point(rect.right - width - Settings.Default.VolumeDisplayOffset, rect.top + (rect.bottom - rect.top) / 2 - height / 2);
			}
			else
			{
				volumePopup.Hide();
			}
		}
	}
}
