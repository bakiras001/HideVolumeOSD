using System;
using System.Threading;
using System.Windows.Forms;

namespace HideVolumeOSD
{
	/// <summary>
	/// 
	/// </summary>
	static class Program
	{
		static Mutex mutex;

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] args)
		{
			// Never die silently: an exception at logon (audio service or Explorer not ready yet, ...)
			// used to make the tray app vanish without any trace. Now it is logged and the app keeps running.

			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
			Application.ThreadException += (sender, e) => Log.Write("UI thread exception", e.Exception);
			AppDomain.CurrentDomain.UnhandledException += (sender, e) => Log.Write("Unhandled exception", e.ExceptionObject as Exception);

			bool createdNew;

			mutex = new Mutex(true, "{00A827A1-C8D4-4FAF-A79B-0193AF81249B}", out createdNew);

			if (!createdNew)
			{
				return;
			}

			try
			{
				if ((args.GetLength(0) == 1))
				{
					// command line mode: HideVolumeOSD.exe -hide | -show

					HideVolumeOSDLib lib = new HideVolumeOSDLib(null);

					if (lib.AttachBlocking(60000))
					{
						if (args[0] == "-hide")
						{
							lib.HideOSD();
						}
						else
							if (args[0] == "-show")
							{
								lib.ShowOSD();
							}
					}
					else
					{
						Log.Write("Command line mode: OSD window not found");
					}
				}
				else
				{
					Application.EnableVisualStyles();
					Application.SetCompatibleTextRenderingDefault(false);

					Autostart.RefreshPathIfEnabled();

					using (ProcessIcon pi = new ProcessIcon())
					{
						pi.Display();

						Application.Run();
					}
				}
			}
			catch (Exception ex)
			{
				Log.Write("Fatal error in Main", ex);
			}
			finally
			{
				mutex.ReleaseMutex();
			}
		}
	}
}
