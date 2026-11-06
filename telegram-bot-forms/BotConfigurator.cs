using Drenalol.Telegram.Bot.Forms.Persistence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
  private readonly List<Type> _formTypes;
  private ServiceDescriptor? _historyStore;

  public BotConfigurator(IServiceCollection services)
  {
    _services = services;
    _formsWithStaticKey = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
    _commands = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
    _formTypes = [];
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

  /// <summary>
  /// Persists every chat's navigation history (and the state its forms capture) in
  /// local files, so histories survive a restart: one JSON file per chat under
  /// <paramref name="configure"/>'s directory. A single-instance option - run several
  /// replicas behind <see cref="UseDistributedHistory"/> instead. Calling this (or
  /// another history option) more than once replaces the previous store.
  /// </summary>
  public BotConfigurator UseFileHistory(Action<FileChatHistoryStoreOptions>? configure = null)
  {
    var options = new FileChatHistoryStoreOptions();
    configure?.Invoke(options);

    _historyStore = ServiceDescriptor.Singleton<IChatHistoryStore>(provider =>
      new FileChatHistoryStore(
        options,
        provider.GetService<ILoggerFactory>()?.CreateLogger<FileChatHistoryStore>()
        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FileChatHistoryStore>.Instance
      ));

    return this;
  }

  /// <summary>
  /// Persists every chat's navigation history (and the state its forms capture) in the
  /// <see cref="IDistributedCache"/> registered in the application: Redis with
  /// <c>services.AddStackExchangeRedisCache(...)</c>, in-memory with
  /// <c>AddDistributedMemoryCache()</c>, or any other distributed cache. Register the
  /// cache before <see cref="Build"/>.
  /// </summary>
  public BotConfigurator UseDistributedHistory(Action<ChatHistoryPersistenceOptions>? configure = null)
  {
    var options = new ChatHistoryPersistenceOptions();
    configure?.Invoke(options);

    _historyStore = ServiceDescriptor.Singleton<IChatHistoryStore>(provider =>
    {
      var cache = provider.GetService<IDistributedCache>()
        ?? throw new InvalidOperationException(
          "Drenalol.Telegram.Bot.Forms: UseDistributedHistory needs an IDistributedCache - call " +
          "services.AddStackExchangeRedisCache(...) or AddDistributedMemoryCache() before BotConfigurator.Build().");

      return new DistributedCacheChatHistoryStore(
        cache,
        options,
        provider.GetService<ILoggerFactory>()?.CreateLogger<DistributedCacheChatHistoryStore>()
        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DistributedCacheChatHistoryStore>.Instance
      );
    });

    return this;
  }

  /// <summary>
  /// Builds the router, optionally over an <see cref="IChatHistoryStore"/> registered
  /// as an application singleton (a custom store). Call once, after all forms,
  /// commands and history options are registered.
  /// </summary>
  public void Build()
  {
    if (_historyStore != null)
      _services.Add(_historyStore);

    _services.AddSingleton<IFormRetriever>(provider =>
      (FormCollection)ActivatorUtilities.CreateInstance(
        provider,
        typeof(FormCollection),
        _formsWithStaticKey,
        _commands,
        _formTypes
      ));
  }

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

  private void InternalRegisterForm(Type type, Func<IServiceProvider, object>? implementationFactory)
  {
    // every registered form type is known to the history restore, keyed by FullName
    _formTypes.Add(type);
    _services.AddTransient(type, implementationFactory ?? (provider => ActivatorUtilities.CreateInstance(provider, type)));
  }
}
