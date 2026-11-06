using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AppHub.Helpers;

public static class ShortcutHelper
{
	[ComImport]
	[Guid("00021401-0000-0000-C000-000000000046")]
	private class ShellLink
	{
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("000214F9-0000-0000-C000-000000000046")]
	private interface IShellLinkW
	{
		void GetPath([Out] StringBuilder pszFile, int cchMaxPath, out WIN32_FIND_DATAW pfd, int fFlags);

		void GetIDList(out nint ppidl);

		void SetIDList(nint pidl);

		void GetDescription([Out] StringBuilder pszName, int cchMaxName);

		void SetDescription(string pszName);

		void GetWorkingDirectory([Out] StringBuilder pszDir, int cchMaxPath);

		void SetWorkingDirectory(string pszDir);

		void GetArguments([Out] StringBuilder pszArgs, int cchMaxPath);

		void SetArguments(string pszArgs);

		void GetHotkey(out short pwHotkey);

		void SetHotkey(short wHotkey);

		void GetShowCmd(out int piShowCmd);

		void SetShowCmd(int iShowCmd);

		void GetIconLocation([Out] StringBuilder pszIconPath, int cchIconPath, out int piIcon);

		void SetIconLocation(string pszIconPath, int iIcon);

		void SetRelativePath(string pszPathRel, int dwReserved);

		void Resolve(nint hwnd, int fFlags);

		void SetPath(string pszFile);
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("0000010B-0000-0000-C000-000000000046")]
	private interface IPersistFile
	{
		void GetClassID(out Guid pClassID);

		[PreserveSig]
		int IsDirty();

		void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);

		void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);

		void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

		void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WIN32_FIND_DATAW
	{
		public uint dwFileAttributes;

		public long ftCreationTime;

		public long ftLastAccessTime;

		public long ftLastWriteTime;

		public uint nFileSizeHigh;

		public uint nFileSizeLow;

		public uint dwReserved0;

		public uint dwReserved1;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string cFileName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
		public string cAlternateFileName;
	}

	public static ShortcutInfo Resolve(string shortcutPath)
	{
		IShellLinkW obj = (IShellLinkW)new ShellLink();
		((IPersistFile)obj).Load(shortcutPath, 0);
		StringBuilder target = new StringBuilder(260);
		StringBuilder args = new StringBuilder(1024);
		StringBuilder workingDir = new StringBuilder(260);
		obj.GetPath(target, target.Capacity, out var _, 0);
		obj.GetArguments(args, args.Capacity);
		obj.GetWorkingDirectory(workingDir, workingDir.Capacity);
		return new ShortcutInfo(target.ToString(), args.ToString(), workingDir.ToString());
	}
}
