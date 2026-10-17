using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms.Localization;

/// <summary>
/// Resolves the language a chat should be answered in. The default implementation
/// always returns <see cref="ITelegramTextLocalizer.DefaultLanguage"/>; register your
/// own (per-chat store, user profile lookup, ...) to localize the bot per chat.
/// </summary>
public interface IChatLanguageResolver
{
  /// <summary>Language code for this chat, matching the codes of <see cref="ITelegramTextLocalizer.SupportedLanguages"/>.</summary>
  Task<string> ResolveAsync(Chat chat, CancellationToken cancellationToken = default);
}

/// <summary>Always resolves to the localizer's default language.</summary>
public sealed class DefaultChatLanguageResolver : IChatLanguageResolver
{
  private readonly ITelegramTextLocalizer _localizer;

  public DefaultChatLanguageResolver(ITelegramTextLocalizer localizer) => _localizer = localizer;

  public Task<string> ResolveAsync(Chat chat, CancellationToken cancellationToken = default) =>
    Task.FromResult(_localizer.DefaultLanguage);
}
