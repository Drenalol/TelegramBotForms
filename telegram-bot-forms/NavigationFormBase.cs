using System.Collections.Concurrent;
using Telegram.Bot.Types.ReplyMarkups;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// A stateful form whose states are driven by a reply keyboard: every button text is
/// mapped to a state, so a button press renders that state's action. The incoming text
/// is matched case-insensitively, which is what makes the same keyboard work across
/// languages when several languages share a button text.
/// </summary>
public abstract class NavigationFormBase<TState> : ExtendedFormBase<TState> where TState : Enum, new()
{
  protected readonly ConcurrentDictionary<string, TState> _buttons = new(StringComparer.OrdinalIgnoreCase);
  protected readonly List<KeyboardButton[]> Rows = new();

  /// <summary>Adds a one-button row whose press routes to <paramref name="state"/>.</summary>
  protected void AddRow(string text, TState state)
  {
    Rows.Add([new KeyboardButton(text)]);
    _buttons[text] = state;
  }

  /// <summary>Adds a custom button row whose press (by button text) routes to <paramref name="state"/>.</summary>
  protected void AddRow(KeyboardButton keyboardButton, TState state)
  {
    Rows.Add([keyboardButton]);
    if (keyboardButton.Text != null)
      _buttons[keyboardButton.Text] = state;
  }

  /// <summary>Adds the "Back" row in the default language (handled by the router, not by this form).</summary>
  protected void AddBackButton()
  {
    Rows.Add(CreateBackButtonRow());
  }

  /// <summary>Adds the "Back" row with explicit (localized) text.</summary>
  protected void AddBackButton(string text)
  {
    Rows.Add(CreateBackButtonRow(text));
  }

  protected ReplyKeyboardMarkup BuildKeyboard(bool resizeKeyboard = true)
  {
    return new ReplyKeyboardMarkup(Rows)
    {
      ResizeKeyboard = resizeKeyboard,
      OneTimeKeyboard = false
    };
  }

  protected override IRender GetRender(MessageContext context)
  {
    if (!_buttons.TryGetValue(context.Text, out var retrievedState))
      return ReenterOnUnknownText(context);

    if (!Renders.TryGetValue(retrievedState, out var retrievedRender))
      throw new InvalidOperationException($"No render action found by state: {retrievedState}");

    return retrievedRender;
  }

  /// <summary>
  /// What renders input the keyboard does not register (stray free text, an encoded
  /// location or contact): by default the waiting form re-shows its current state
  /// instead of replying with the unhandled-error text. Override to consume unknown
  /// input differently, e.g. as a list search.
  /// </summary>
  protected virtual IRender ReenterOnUnknownText(MessageContext context)
  {
    if (State is { } current && Renders.TryGetValue(current, out var currentRender))
      return currentRender;

    throw new InvalidOperationException($"State not registered for text: {context.Text}");
  }
}
