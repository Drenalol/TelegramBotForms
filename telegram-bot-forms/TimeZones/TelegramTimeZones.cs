namespace Drenalol.Telegram.Bot.Forms.TimeZones;

/// <summary>
/// A curated set of time zones a settings form can offer as buttons. Anything else can
/// still be accepted by typing the IANA id — this list is just the common quick-pick.
/// </summary>
public static class TelegramTimeZones
{
  private static readonly string[] PopularIds =
  [
    "UTC",
    "Europe/London",
    "Europe/Berlin",
    "Europe/Moscow",
    "Europe/Istanbul",
    "America/New_York",
    "America/Chicago",
    "America/Denver",
    "America/Los_Angeles",
    "America/Sao_Paulo",
    "Asia/Dubai",
    "Asia/Kolkata",
    "Asia/Shanghai",
    "Asia/Tokyo",
    "Australia/Sydney"
  ];

  /// <summary>IANA id → friendly label with current UTC offset ("Europe/Moscow · UTC+03:00").</summary>
  public static IReadOnlyList<(string Id, string Label)> Popular()
  {
    var now = DateTime.UtcNow;

    return PopularIds
      .Select(id => (id, FormatLabel(id, now)))
      .ToList();
  }

  /// <summary>Displays a zone id with its current UTC offset; falls back to the raw id.</summary>
  public static string FormatLabel(string id) => FormatLabel(id, DateTime.UtcNow);

  private static string FormatLabel(string id, DateTime now)
  {
    TimeZoneInfo zone;

    try
    {
      zone = TimeZoneInfo.FindSystemTimeZoneById(id);
    }
    catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
    {
      return id;
    }

    var offset = zone.GetUtcOffset(now);
    var sign = offset < TimeSpan.Zero ? "-" : "+";
    var abs = offset.Duration();

    return $"{id} · UTC{sign}{abs.Hours:00}:{abs.Minutes:00}";
  }
}
