using NUnit.Framework;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Drenalol.Telegram.Bot.Forms.Tests;

[TestFixture]
public class MessageContextTests
{
  [Test]
  public void Reply_Sets_The_Output_Side()
  {
    var context = new MessageContext(TestBot.Chat(), "in");

    context.Reply("out", ParseMode.Html);

    Assert.That(context.ReplyText, Is.EqualTo("out"));
    Assert.That(context.ParseMode, Is.EqualTo(ParseMode.Html));
  }

  [Test]
  public void Reply_With_Rows_Builds_A_Resizeable_Reply_Keyboard()
  {
    var context = new MessageContext(TestBot.Chat(), "in");

    context.Reply("out", [[new KeyboardButton("a")], [new KeyboardButton("b"), new KeyboardButton("c")]]);

    var markup = context.ReplyMarkup as ReplyKeyboardMarkup;

    Assert.That(markup, Is.Not.Null);
    Assert.That(markup!.ResizeKeyboard, Is.True);
    Assert.That(markup.Keyboard.Select(row => row.Count()), Is.EqualTo(new[] { 1, 2 }));
  }

  [Test]
  public void Forward_Replaces_The_Routing_Text()
  {
    var context = new MessageContext(TestBot.Chat(), "label");

    context.Forward("id-42");

    Assert.That(context.Text, Is.EqualTo("id-42"));
  }
}
