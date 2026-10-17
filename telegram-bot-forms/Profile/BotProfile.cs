using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms.Profile;

/// <summary>
/// Pushes the code-owned BotFather profile texts (bot name, short description on the
/// profile page, the empty-chat welcome card and the slash-command menu) through the
/// Bot API on every bot start, so they are versioned and localized like the rest of
/// the bot UI. BotFather edits the very same fields, so a manual change there survives
/// only until the next restart. A failure here never stops polling: the profile simply
/// keeps whatever was set last.
/// <para>
/// Every field is read back first and written only when it actually differs: Telegram
/// throttles these profile methods hard (a burst of restarts earns a 429 with a
/// multi-hour "retry after"), so a steady-state start must send reads only, and a 429
/// aborts the whole run instead of burning the remaining budget on calls that would
/// fail anyway.
/// </para>
/// </summary>
public static class BotProfile
{
  public static async Task ApplyAsync(ITelegramBotClient client, BotProfileOptions options, ILogger logger, CancellationToken cancellationToken)
  {
    try
    {
      var changed = false;

      // The brand name is identical in every language, so only the default variant is set.
      if (!string.IsNullOrWhiteSpace(options.Name))
      {
        if (await client.GetMyName(cancellationToken: cancellationToken) != options.Name)
        {
          await client.SetMyName(options.Name, cancellationToken: cancellationToken);
          changed = true;
        }
      }

      // The null language code is the default variant Telegram shows to clients whose
      // language is not among the supported ones; the default language fills it.
      string?[] languageCodes = [null, .. options.Languages.Keys];

      foreach (var languageCode in languageCodes)
        changed |= await ApplyLocalizedAsync(client, options, languageCode, logger, cancellationToken);

      if (changed)
        logger.LogInformation("Applied the Telegram bot profile texts");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Failed to apply the Telegram bot profile texts, keeping the BotFather state as is");
    }
  }

  /// <summary>
  /// One language variant of the profile (short description, welcome card, /-menu),
  /// written field by field only where the current Telegram value differs. A regional
  /// code Telegram rejects (e.g. "pt-BR") is retried with its bare language prefix; a
  /// variant that still fails is skipped with a warning, so one rejected language code
  /// never leaves the other locales without their texts. A 429 is never retried or
  /// swallowed here — it propagates to stop the run for the retry-after window.
  /// </summary>
  /// <returns>Whether any field of this variant was actually written.</returns>
  private static async Task<bool> ApplyLocalizedAsync(
    ITelegramBotClient client, BotProfileOptions options, string? languageCode, ILogger logger, CancellationToken cancellationToken)
  {
    var language = languageCode ?? options.DefaultLanguage;

    try
    {
      if (!options.Languages.TryGetValue(language, out var texts))
      {
        // The default variant: Telegram shows it for unsupported client languages
        if (languageCode == null || !options.Languages.TryGetValue(options.DefaultLanguage, out texts))
          return false;
      }

      var changed = false;

      if (texts.ShortDescription is { Length: > 0 } shortDescription)
      {
        if (await client.GetMyShortDescription(languageCode, cancellationToken: cancellationToken) != shortDescription)
        {
          await client.SetMyShortDescription(shortDescription, languageCode, cancellationToken: cancellationToken);
          changed = true;
        }
      }

      if (texts.Welcome is { Length: > 0 } welcome)
      {
        if (await client.GetMyDescription(languageCode, cancellationToken: cancellationToken) != welcome)
        {
          await client.SetMyDescription(welcome, languageCode, cancellationToken: cancellationToken);
          changed = true;
        }
      }

      if (texts.Commands.Count > 0)
      {
        BotCommand[] commands = [.. texts.Commands.Select(command => new BotCommand { Command = command.Command, Description = command.Description })];
        var currentCommands = await client.GetMyCommands(languageCode: languageCode, cancellationToken: cancellationToken);
        if (!currentCommands.SequenceEqual(commands, new BotCommandComparer()))
        {
          await client.SetMyCommands(commands, languageCode: languageCode, cancellationToken: cancellationToken);
          changed = true;
        }
      }

      return changed;
    }
    catch (ApiRequestException ex) when (ex.ErrorCode == 429)
    {
      // The whole method family is throttled; retrying now (including the regional-code
      // fallback below) would only deepen the penalty, so let the outer handler log and stop
      throw;
    }
    catch (ApiRequestException) when (languageCode is not null && languageCode.Contains('-'))
    {
      return await ApplyLocalizedAsync(client, options, languageCode.Split('-')[0], logger, cancellationToken);
    }
    catch (ApiRequestException ex) when (languageCode is not null)
    {
      logger.LogWarning(ex, "Telegram rejected the profile texts of {LanguageCode}, skipping that language", languageCode);
      return false;
    }
  }

  /// <summary>Telegram's <see cref="BotCommand"/> is a plain data object, so commands are compared by value.</summary>
  private sealed class BotCommandComparer : IEqualityComparer<BotCommand>
  {
    public bool Equals(BotCommand? x, BotCommand? y) =>
      ReferenceEquals(x, y) || (x is not null && y is not null && x.Command == y.Command && x.Description == y.Description);

    public int GetHashCode(BotCommand obj) => HashCode.Combine(obj.Command, obj.Description);
  }
}
