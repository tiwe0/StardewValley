using System;
using System.Runtime.InteropServices;

namespace StardewValley.NativeClipboard
{
	/// <summary>Provides a wrapper around SDL's clipboard API for OSX.</summary>
	internal sealed class OsxSdlClipboard : SdlClipboard
	{
		private const string DylibName = "libSDL2-2.0.0.dylib";

		/// <inheritdoc cref="M:StardewValley.NativeClipboard.OsxSdlClipboard.GetTextImpl" />
		[DllImport("libSDL2-2.0.0.dylib", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr SDL_GetClipboardText();

		/// <inheritdoc cref="M:StardewValley.NativeClipboard.OsxSdlClipboard.SetTextImpl(System.IntPtr)" />
		[DllImport("libSDL2-2.0.0.dylib", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetClipboardText")]
		private static extern int SDL_SetClipboardText(IntPtr text);

		/// <summary>Constructs an instance and sets the providing platform name.</summary>
		public OsxSdlClipboard()
		{
			PlatformName = "OSX";
		}

		/// <inheritdoc />
		protected override IntPtr GetTextImpl()
		{
			return SDL_GetClipboardText();
		}

		/// <inheritdoc />
		protected override int SetTextImpl(IntPtr text)
		{
			return SDL_SetClipboardText(text);
		}
	}
}
