using Telegram.Bot.Types.ReplyMarkups;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// A form with a fixed (language-independent) keyboard, built once per form type and
/// shared process-wide through <see cref="KeyboardCache"/>.
/// </summary>
public abstract class GlobalKeyboardFormBase : FormBase
{
  protected ReplyKeyboardMarkup Keyboard => KeyboardCache.GetOrAdd(GetType().Name, KeyboardFactory);

  protected abstract ReplyKeyboardMarkup KeyboardFactory();
}
