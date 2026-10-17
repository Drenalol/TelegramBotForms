using Drenalol.Telegram.Bot.Forms;

namespace ExampleBot.Bot;

public enum ProfileState
{
  Show,
  EditName,
}

/// <summary>
/// A stateful input wizard on top of <see cref="ExtendedFormBase{TState}"/>: "Change
/// name" switches to a waiting state, the next free-text message is consumed as the
/// name (the router hands unregistered texts to the stateful form at the top of the
/// chat history), saving returns to the show state.
/// </summary>
public sealed class ProfileForm : ExtendedFormBase<ProfileState>
{
  private readonly Dictionary<string, ProfileState> _buttons = new(StringComparer.OrdinalIgnoreCase);
  private readonly ProfileStore _store;

  private string _language = ExampleTexts.English;

  public ProfileForm(ProfileStore store)
  {
    _store = store;

    AddRenderOn(ProfileState.Show, string.Empty, (context, token) => ShowAsync(context, token));
    AddRenderOn(ProfileState.EditName, string.Empty, EditNameAsync);
  }

  protected override Task<MessageContext> RenderInitAsync(MessageContext context, CancellationToken cancellationToken) =>
    ShowAsync(context, cancellationToken);

  protected override IRender GetRender(MessageContext context)
  {
    if (_buttons.TryGetValue(context.Text, out var state))
      return Renders[state];

    // the wizard is waiting for free input: anything else is the new name
    if (State == ProfileState.EditName)
      return CustomRender(SaveNameAsync);

    throw new InvalidOperationException($"Nothing routes the text: {context.Text}");
  }

  private async Task<MessageContext> ShowAsync(MessageContext context, CancellationToken cancellationToken, string? prefix = null)
  {
    _language = await GetLanguageAsync(context.Chat, cancellationToken);

    _buttons.Clear();
    _buttons[ExampleTexts.ChangeNameButton(_language)] = ProfileState.EditName;

    var text = prefix is null
      ? ExampleTexts.ProfileTitle(_language, _store.GetName(context.Chat.Id))
      : $"{prefix}\n\n{ExampleTexts.ProfileTitle(_language, _store.GetName(context.Chat.Id))}";

    return context.Reply(
      text,
      [
        [new Telegram.Bot.Types.ReplyMarkups.KeyboardButton(ExampleTexts.ChangeNameButton(_language))],
        await CreateBackButtonRowAsync(context.Chat, cancellationToken),
      ]
    );
  }

  private Task<MessageContext> EditNameAsync(MessageContext context, CancellationToken cancellationToken)
  {
    State = ProfileState.EditName;

    return Task.FromResult(context.Reply(ExampleTexts.AskName(_language)));
  }

  private async Task<MessageContext> SaveNameAsync(MessageContext context, CancellationToken cancellationToken)
  {
    _store.SetName(context.Chat.Id, context.Text.Trim());
    State = ProfileState.Show;

    return await ShowAsync(context, cancellationToken, ExampleTexts.NameSaved(_language, _store.GetName(context.Chat.Id)));
  }
}
