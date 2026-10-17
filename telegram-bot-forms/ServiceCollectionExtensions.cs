using Drenalol.Telegram.Bot.Forms.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>DI entry point of the framework.</summary>
public static class ServiceCollectionExtensions
{
  /// <summary>
  /// Registers the bot runtime (long polling host, message buses, bot client from
  /// <see cref="TelegramBotFormsOptions.Token"/>) and returns the fluent
  /// <see cref="BotConfigurator"/> for forms and commands. Finish registration with
  /// <see cref="BotConfigurator.Build"/>.
  /// </summary>
  public static BotConfigurator AddTelegramBotForms(this IServiceCollection services) =>
    services.AddTelegramBotForms(_ => { });

  /// <summary>
  /// Registers the bot runtime (long polling host, message buses, bot client) with
  /// inline options and returns the fluent <see cref="BotConfigurator"/> for forms and
  /// commands. Finish registration with <see cref="BotConfigurator.Build"/>.
  /// <para>
  /// Options can also be bound from configuration afterwards:
  /// <c>services.Configure&lt;TelegramBotFormsOptions&gt;(config.GetSection("TelegramBotForms"))</c>.
  /// </para>
  /// </summary>
  public static BotConfigurator AddTelegramBotForms(this IServiceCollection services, Action<TelegramBotFormsOptions> configure)
  {
    services.Configure(configure);

    // Replaceable defaults: register your own ITelegramTextLocalizer /
    // IChatLanguageResolver before this call (or with TryAdd semantics) to localize.
    services.TryAddSingleton<ITelegramTextLocalizer, DefaultTextLocalizer>();
    services.TryAddSingleton<IChatLanguageResolver, DefaultChatLanguageResolver>();

    services.AddSingleton<TelegramBotBus>();
    services.AddSingleton<ITelegramBotClient>(
      provider =>
      {
        var options = provider.GetRequiredService<IOptions<TelegramBotFormsOptions>>().Value;

        if (string.IsNullOrWhiteSpace(options.Token))
          throw new InvalidOperationException("Drenalol.Telegram.Bot.Forms: TelegramBotFormsOptions.Token is not configured.");

        return new TelegramBotClient(options.Token, new HttpClient());
      }
    );
    services.AddHostedService<TelegramBotStartup>();

    return new BotConfigurator(services);
  }
}
