#nullable enable

namespace BetterSpire2.Core;

internal static class DamageTrackerRefreshThrottle
{
	private static ulong _lastIntentProcessRefreshMs;

	internal static bool TryAcquireIntentProcessRefresh(ulong ticksMsec, ulong intervalMs)
	{
		if (ticksMsec - _lastIntentProcessRefreshMs < intervalMs)
		{
			return false;
		}

		_lastIntentProcessRefreshMs = ticksMsec;
		return true;
	}
}