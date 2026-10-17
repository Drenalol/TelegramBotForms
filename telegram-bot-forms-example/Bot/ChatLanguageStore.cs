using System.Collections.Concurrent;
using Drenalol.Telegram.Bot.Forms.Localization;
using Telegram.Bot.Types;

namespace ExampleBot.Bot;

/// <summary>
/// Per-chat language in memory (a database or a user profile would be the real store):
/// the framework only asks <see cref="IChatLanguageResolver"/> which language a chat
/// speaks, everything else is up to the application.
/// </summary>
public sealed class ChatLanguageStore : IChatLanguageResolver
{
  private readonly ConcurrentDictionary<long, string> _languages = new();
  private readonly ITelegramTextLocalizer _localizer;

  public ChatLanguageStore(ITelegramTextLocalizer localizer) => _localizer = localizer;

  public void Set(long chatId, string language) => _languages[chatId] = language;

  public Task<string> ResolveAsync(Chat chat, CancellationToken cancellationToken = default) =>
    Task.FromResult(_languages.GetOrAdd(chat.Id, _ => _localizer.DefaultLanguage));
}
