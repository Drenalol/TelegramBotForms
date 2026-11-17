using NUnit.Framework;
using Telegram.Bot.Types;
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

  [Test]
  public void Text_Incoming_Carries_No_Structured_Payload()
  {
    var context = new MessageContext(TestBot.Chat(), "in");

    Assert.That(context.Location, Is.Null);
    Assert.That(context.Contact, Is.Null);
  }

  [Test]
  public void Constructor_Carries_The_Shared_Location()
  {
    var location = new Location { Latitude = 55.7558, Longitude = 37.6173 };

    var context = new MessageContext(TestBot.Chat(), "55.7558,37.6173", location);

    Assert.That(context.Location!.Latitude, Is.EqualTo(55.7558));
    Assert.That(context.Location.Longitude, Is.EqualTo(37.6173));
    Assert.That(context.Contact, Is.Null);
  }

  [Test]
  public void Constructor_Carries_The_Shared_Contact()
  {
    var contact = new Contact { PhoneNumber = "+79991234567", FirstName = "Ada" };

    var context = new MessageContext(TestBot.Chat(), "+79991234567", contact: contact);

    Assert.That(context.Contact!.PhoneNumber, Is.EqualTo("+79991234567"));
    Assert.That(context.Contact.FirstName, Is.EqualTo("Ada"));
    Assert.That(context.Location, Is.Null);
  }

  [Test]
  public void Constructor_Carries_The_Callback_Query_Of_A_Tapped_Button()
  {
    var context = new MessageContext(TestBot.Chat(), "mute 42", callbackQueryId: "query-7");

    Assert.That(context.CallbackQueryId, Is.EqualTo("query-7"));
    Assert.That(new MessageContext(TestBot.Chat(), "written").CallbackQueryId, Is.Null);
  }

  [Test]
  public void ReplyRich_Sends_Rich_Instead_Of_Text()
  {
    var context = new MessageContext(TestBot.Chat(), "in");

    context.Reply("text");
    context.ReplyRich(Rich(), [[new KeyboardButton("a")]]);

    Assert.That(context.RichReply, Is.Not.Null);
    Assert.That(context.ReplyText, Is.Null, "one turn sends one message");
    Assert.That((context.ReplyMarkup as ReplyKeyboardMarkup)?.Keyboard.SelectMany(row => row).Count(), Is.EqualTo(1));
  }

  [Test]
  public void Reply_After_ReplyRich_Sends_Text_Instead()
  {
    var context = new MessageContext(TestBot.Chat(), "in");

    context.ReplyRich(Rich());
    context.Reply("text");

    Assert.That(context.RichReply, Is.Null);
    Assert.That(context.ReplyText, Is.EqualTo("text"));
  }

  [Test]
  public void WithTextFallback_Keeps_Both_Sides_For_The_Host_To_Degrade()
  {
    var context = new MessageContext(TestBot.Chat(), "in");

    context.ReplyRich(Rich()).WithTextFallback("text", ParseMode.Html);

    // the rich message goes first, the text is what the host sends when it is refused
    Assert.That(context.RichReply, Is.Not.Null);
    Assert.That(context.ReplyText, Is.EqualTo("text"));
    Assert.That(context.ParseMode, Is.EqualTo(ParseMode.Html));
  }

  [Test]
  public void Ephemeral_Arms_The_Reply_Of_A_Button_Turn()
  {
    var context = new MessageContext(TestBot.Chat(), "mute 42", callbackQueryId: "query-7");

    context.Reply("done").Ephemeral(replaceCallbackQueryMessage: true);

    Assert.That(context.EphemeralReply, Is.True);
    Assert.That(context.ReplaceCallbackQueryMessage, Is.True);

    // a written message has no query to answer: the flags are set here and the host is
    // what refuses them, because only the host knows the query id is missing
    var written = new MessageContext(TestBot.Chat(), "written").Reply("done").Ephemeral();

    Assert.That(written.CallbackQueryId, Is.Null);
    Assert.That(written.ReplaceCallbackQueryMessage, Is.False);
  }

  [Test]
  public void ReplaceMessage_Arms_The_Reply_Of_A_Button_Turn()
  {
    var context = new MessageContext(TestBot.Chat(), "rate 7 2026-11-16 sunset beautiful", callbackQueryId: "query-7", callbackMessageId: 431);

    context.Reply("noted").ReplaceMessage();

    Assert.That(context.ReplacesMessage, Is.True);

    // the message the pressed button sits in is the one the host edits
    Assert.That(context.CallbackMessageId, Is.EqualTo(431));

    // a written message has nothing to edit: the flag is set here and the host is what refuses
    // it, because only the host knows the id is missing
    var written = new MessageContext(TestBot.Chat(), "written").Reply("noted").ReplaceMessage();

    Assert.That(written.CallbackMessageId, Is.Null);
  }

  private static InputRichMessage Rich() =>
    new() { Blocks = [new InputRichBlockParagraph { Text = new RichTextText { Text = "hello" } }] };
}
