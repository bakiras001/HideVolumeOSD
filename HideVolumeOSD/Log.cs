using System;
using System.IO;

namespace HideVolumeOSD
{
	/// <summary>
	/// Tiny diagnostic log (%LOCALAPPDATA%\HideVolumeOSD\HideVolumeOSD.log).
	/// It is the only file this program ever writes besides its user settings,
	/// and it never throws: logging must not be able to break the app.
	/// </summary>
	static class Log
	{
		static readonly object sync = new object();

		static readonly string path = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"HideVolumeOSD", "HideVolumeOSD.log");

		public static string FilePath
		{
			get { return path; }
		}

		public static void Write(string message)
		{
			try
			{
				lock (sync)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(path));

					FileInfo fi = new FileInfo(path);

					// keep the log small: start over once it gets big
					if (fi.Exists && fi.Length > 256 * 1024)
					{
						fi.Delete();
					}

					File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
				}
			}
			catch
			{
			}
		}

		public static void Write(string message, Exception ex)
		{
			Write(message + ": " + (ex == null ? "(null)" : ex.ToString()));
		}
	}
}
