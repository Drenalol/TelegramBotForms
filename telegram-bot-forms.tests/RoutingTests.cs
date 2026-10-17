using Drenalol.Telegram.Bot.Forms.Localization;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms.Tests;

[TestFixture]
public class RoutingTests
{
  private IFormRetriever _router = null!;

  [SetUp]
  public void SetUp() =>
    _router = TestBot.Router(configurator => configurator
      .RegisterStartForm<StartStub>()
      .RegisterFormWithKey<MenuStub>("Menu")
      .RegisterFormWithKey<RootClearStub>("Root")
      .RegisterFormWithKey<ProbeStub>("Probe")
      .RegisterFormWithKey<DetailStub>("Detail")
      .RegisterFormWithKey<NoHistoryStub>("NoHistory")
      .RegisterFormWithKey<BoomStub>("Boom")
      .RegisterFormWithKey<WizardStub>("Wizard")
      .RegisterCommand<HelpStub>("help"));

  [Test]
  public async Task Start_Routes_Start_Form_With_Deep_Link_Parameter()
  {
    var context = await _router.RenderAsync(TestBot.Chat(), "/start deep_param");

    Assert.That(context, Is.Not.Null);
    Assert.That(context!.ReplyText, Is.EqualTo("start"));
  }

  [Test]
  public async Task Registered_Key_Routes_Its_Form()
  {
    var context = await _router.RenderAsync(TestBot.Chat(), "Menu");

    Assert.That(context!.ReplyText, Is.EqualTo("menu"));
  }

  [Test]
  public async Task Unregistered_Text_Without_History_Keeps_Silent()
  {
    var context = await _router.RenderAsync(TestBot.Chat(), "nothing routes me");

    Assert.That(context, Is.Null);
  }

  [Test]
  public async Task Command_Wins_Over_History_And_Gets_All_Arguments()
  {
    await _router.RenderAsync(TestBot.Chat(), "Wizard");

    var context = await _router.RenderAsync(TestBot.Chat(), "/help me please");

    Assert.That(context!.ReplyText, Is.EqualTo("help:/help me please"));
  }

  [Test]
  public async Task Command_Wins_Over_A_Form_With_The_Same_Key()
  {
    var router = TestBot.Router(configurator => configurator
      .RegisterStartForm<StartStub>()
      .RegisterFormWithKey<MenuStub>("help")
      .RegisterCommand<HelpStub>("help"));

    var context = await router.RenderAsync(TestBot.Chat(), "help");

    Assert.That(context!.ReplyText, Does.StartWith("help:"));
  }

  [Test]
  public async Task Stateful_Form_Atop_The_History_Consumes_Unregistered_Text()
  {
    await _router.RenderAsync(TestBot.Chat(), "Wizard");
    await _router.RenderAsync(TestBot.Chat(), "edit");

    var context = await _router.RenderAsync(TestBot.Chat(), "free input");

    Assert.That(context!.ReplyText, Is.EqualTo("wizard-saved:free input"));
  }

  [Test]
  public async Task Back_Returns_To_The_Previous_Form()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "Menu");
    await _router.RenderAsync(chat, "Detail");

    var context = await _router.RenderAsync(chat, "Back");

    Assert.That(context!.ReplyText, Is.EqualTo("menu"));
  }

  [Test]
  public async Task Back_From_Empty_History_Goes_To_Start()
  {
    var context = await _router.RenderAsync(TestBot.Chat(), "Back");

    Assert.That(context!.ReplyText, Is.EqualTo("start"));
  }

  [Test]
  public async Task Back_Skips_Completed_Stateful_Forms()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "Wizard"); // W1
    await _router.RenderAsync(chat, "finish"); // W1 completed
    await _router.RenderAsync(chat, "Wizard"); // W2 atop

    var context = await _router.RenderAsync(chat, "Back");

    // W2 popped, W1 is completed and skipped, the history is exhausted -> start
    Assert.That(context!.ReplyText, Is.EqualTo("start"));
  }

  [Test]
  public async Task Back_From_A_Waiting_Wizard_Returns_To_The_Previous_Form()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "Menu");
    await _router.RenderAsync(chat, "Wizard");
    await _router.RenderAsync(chat, "edit");

    var back = await _router.RenderAsync(chat, "Back");

    Assert.That(back!.ReplyText, Is.EqualTo("menu"));
  }

  [Test]
  public async Task IgnoreHistory_Root_Form_Clears_The_History_And_Is_Not_Pushed()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "Menu");
    await _router.RenderAsync(chat, "NoHistory");

    // entering the root form cleared the history and the form itself was not pushed,
    // so "Back" finds nothing to return to and lands on start
    var back = await _router.RenderAsync(chat, "Back");
    Assert.That(back!.ReplyText, Is.EqualTo("start"));
  }

  [Test]
  public async Task Back_Reenters_A_ClearHistory_Form_Without_Disposing_It()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "Menu");

    var root = await _router.RenderAsync(chat, "Root");
    Assert.That(root!.ReplyText, Is.EqualTo("root:en"));

    await _router.RenderAsync(chat, "Menu");

    // popping the top Menu reveals the Root form itself in the history: re-entering it
    // must not let its own clear-on-navigate dispose its DI scope
    var back = await _router.RenderAsync(chat, "Back");

    Assert.That(back!.Exception, Is.Null);
    Assert.That(back.ReplyText, Is.EqualTo("root:en"));
  }

  [Test]
  public async Task Back_Disposes_The_DI_Scope_Of_The_Popped_Form()
  {
    ProbeStub.Created.Clear();
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "Menu");
    await _router.RenderAsync(chat, "Probe");

    var back = await _router.RenderAsync(chat, "Back");

    Assert.That(back!.ReplyText, Is.EqualTo("menu"));
    Assert.That(ProbeStub.Created.Single().ScopeAlive, Is.False);
  }

  [Test]
  public async Task Form_Exception_Becomes_A_Friendly_Error_Reply()
  {
    var context = await _router.RenderAsync(TestBot.Chat(), "Boom");

    Assert.That(context!.ReplyText, Is.EqualTo("Something went wrong. Please try again or contact the developers."));
    Assert.That(context.Exception, Is.InstanceOf<InvalidOperationException>());
  }

  [Test]
  public void Same_Button_Text_For_Two_Different_Forms_Fails_Fast()
  {
    var services = new ServiceCollection();

    var configurator = services.AddTelegramBotForms(_ => { });
    configurator.RegisterFormWithKey<MenuStub>("Collision");

    Assert.Throws<InvalidOperationException>(
      () => configurator.RegisterFormWithKey<DetailStub>("Collision"));
  }

  [Test]
  public void Same_Button_Text_For_The_Same_Form_Registers_Once()
  {
    var services = new ServiceCollection();

    var configurator = services.AddTelegramBotForms(_ => { });
    configurator.RegisterFormWithKey<MenuStub>("Products");

    Assert.DoesNotThrow(() => configurator.RegisterFormWithKey<MenuStub>("Products"));
  }

  [Test]
  public async Task Navigation_Renders_The_Target_Form()
  {
    var chat = TestBot.Chat();

    var navigation = await _router.RenderAsync(chat, "Wizard");
    Assert.That(navigation!.ReplyText, Is.EqualTo("wizard-init"));

    var start = await _router.RenderAsync(chat, "to-start");
    Assert.That(start!.ReplyText, Is.EqualTo("start"));
  }

  [Test]
  public async Task Chats_Have_Independent_Histories()
  {
    await _router.RenderAsync(TestBot.Chat(1), "Menu");

    var other = await _router.RenderAsync(TestBot.Chat(2), "Back");

    Assert.That(other!.ReplyText, Is.EqualTo("start"));
  }

  [Test]
  public async Task Async_Back_Row_Carries_The_Localized_Back_Text_Not_The_Language_Code()
  {
    var router = TestBot.Router(
      services => services.AddSingleton<IChatLanguageResolver, RussianFor99Resolver>(),
      configurator => configurator
        .RegisterStartForm<StartStub>()
        .RegisterFormWithKey<AsyncBackRowStub>("AsyncBack"));

    var english = await router.RenderAsync(TestBot.Chat(1), "AsyncBack");
    Assert.That(english!.Labels(), Is.EquivalentTo(["Back"]));

    var russian = await router.RenderAsync(TestBot.Chat(99), "AsyncBack");
    Assert.That(russian!.Labels(), Is.EquivalentTo(["Назад"]));
  }
}
