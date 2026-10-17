namespace Drenalol.Telegram.Bot.Forms.Localization;

/// <summary>
/// The framework-owned UI texts. Everything a concrete bot says beyond these keys
/// (menu labels, titles, confirmations) belongs to the application's own catalog.
/// </summary>
public enum TextKey
{
  /// <summary>The navigation "Back" button.</summary>
  Back,

  /// <summary>The "previous page" button of a paged list form.</summary>
  PreviousPage,

  /// <summary>The "next page" button of a paged list form.</summary>
  NextPage,

  /// <summary>The reply shown when a form throws an unhandled exception.</summary>
  UnhandledError,
}
