using Microsoft.Extensions.DependencyInjection;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// Fluent registration of the bot's forms and commands, returned by
/// <c>AddTelegramBotForms</c>. Keyboard button texts double as routing keys, so a
/// localized bot registers every language variant of a button with
/// <see cref="RegisterFormWithKey{T}(string)"/>.
/// </summary>
public class BotConfigurator
{
  private readonly IServiceCollection _services;
  private readonly Dictionary<string, Type> _formsWithStaticKey;
  private readonly Dictionary<string, Type> _commands;

  public BotConfigurator(IServiceCollection services)
  {
    _services = services;
    _formsWithStaticKey = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
    _commands = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>Registers the form behind "/start" (with or without a deep-link parameter).</summary>
  public BotConfigurator RegisterStartForm<T>() where T : FormBase
  {
    InternalRegisterForm("/start", typeof(T), null);

    return this;
  }

  /// <summary>Registers a form behind a fixed button text (e.g. one language variant of a menu button).</summary>
  public BotConfigurator RegisterFormWithKey<T>(string triggerKey) where T : FormBase
  {
    RegisterFormWithKey<T>(triggerKey, null);

    return this;
  }

  /// <summary>Registers a form behind a fixed button text with a custom DI factory.</summary>
  public BotConfigurator RegisterFormWithKey<T>(string triggerKey, Func<IServiceProvider, object>? implementationFactory) where T : FormBase
  {
    InternalRegisterForm(triggerKey, typeof(T), implementationFactory);

    return this;
  }

  /// <summary>Registers a form reachable only by navigation (a detail page, a wizard step).</summary>
  public BotConfigurator RegisterForm<T>(Func<IServiceProvider, object>? implementationFactory = null) where T : FormBase
  {
    InternalRegisterForm(typeof(T), implementationFactory);

    return this;
  }

  /// <summary>
  /// Registers a free-text command (matched by the first message token, with or without
  /// a leading "/"), e.g. "link &lt;api key&gt;".
  /// </summary>
  public BotConfigurator RegisterCommand<T>(string prefix) where T : class, IBotCommand
  {
    _commands.Add(prefix, typeof(T));
    _services.AddTransient(typeof(T));

    return this;
  }

  /// <summary>Builds the router. Call once, after all forms and commands are registered.</summary>
  public void Build() =>
    _services.AddSingleton<IFormRetriever>(provider =>
      (FormCollection)ActivatorUtilities.CreateInstance(provider, typeof(FormCollection), _formsWithStaticKey, _commands));

  /// <summary>
  /// Button texts double as routing keys. Languages legitimately share a text (French
  /// and German both say "Abonnements"), so the same (key, form) pair registers once;
  /// one text claimed by two different forms is a localization collision and fails fast.
  /// </summary>
  private void InternalRegisterForm(string triggerKey, Type type, Func<IServiceProvider, object>? implementationFactory)
  {
    if (_formsWithStaticKey.TryGetValue(triggerKey, out var owner))
    {
      if (owner != type)
        throw new InvalidOperationException(
          $"Bot button \"{triggerKey}\" already routes {owner.Name}; {type.Name} claims the same text. Localized texts of different forms must not collide.");

      return;
    }

    _formsWithStaticKey.Add(triggerKey, type);
    InternalRegisterForm(type, implementationFactory);
  }

  private void InternalRegisterForm(Type type, Func<IServiceProvider, object>? implementationFactory) =>
    _services.AddTransient(type, implementationFactory ?? (provider => ActivatorUtilities.CreateInstance(provider, type)));
}
