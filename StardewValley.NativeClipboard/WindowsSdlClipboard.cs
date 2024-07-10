namespace StardewValley.NativeClipboard
{
	/// <summary>Provides a wrapper around SDL's clipboard API for Windows.</summary>
	internal sealed class WindowsSdlClipboard : SdlClipboard
	{
		/// <summary>Constructs an instance and sets the providing platform name.</summary>
		public WindowsSdlClipboard()
		{
			PlatformName = "Windows";
		}
	}
}
