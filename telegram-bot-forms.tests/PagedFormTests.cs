using Drenalol.Telegram.Bot.Forms.Localization;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Drenalol.Telegram.Bot.Forms.Tests;

[TestFixture]
public class PagedFormTests
{
  private IFormRetriever _router = null!;

  [SetUp]
  public void SetUp() =>
    _router = TestBot.Router(configurator => configurator
      .RegisterStartForm<StartStub>()
      .RegisterFormWithKey<PagedStub>("List"));

  [Test]
  public async Task First_Page_Shows_Items_And_Next()
  {
    var context = await _router.RenderAsync(TestBot.Chat(), "List");

    Assert.That(context!.ReplyText, Is.EqualTo("page 1/2 count 5"));
    Assert.That(context.Labels(), Is.EquivalentTo(["Dup", "Dup #2", "I3", "I4", "I5", "refresh", "Next ▶", "Back"]));
  }

  [Test]
  public async Task Duplicate_Row_Labels_Get_A_Page_Scoped_Counter()
  {
    var context = await _router.RenderAsync(TestBot.Chat(), "List");

    Assert.That(context!.Labels(), Does.Contain("Dup").And.Contain("Dup #2"));
  }

  [Test]
  public async Task Next_And_Prev_Navigate_Pages()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "List");

    var second = await _router.RenderAsync(chat, "Next ▶");
    Assert.That(second!.ReplyText, Is.EqualTo("page 2/2 count 5"));
    Assert.That(second.Labels(), Does.Contain("◀ Prev").And.No.Contain("Next ▶"));

    var first = await _router.RenderAsync(chat, "◀ Prev");
    Assert.That(first!.ReplyText, Is.EqualTo("page 1/2 count 5"));
  }

  [Test]
  public async Task Row_Press_Renders_The_Item()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "List");

    var item = await _router.RenderAsync(chat, "Dup #2");

    Assert.That(item!.ReplyText, Is.EqualTo("item:2"));
  }

  [Test]
  public async Task Press_Of_A_Stale_Row_Label_Becomes_A_Search()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "List");
    await _router.RenderAsync(chat, "Next ▶");

    // "Dup" was a page-1 label; unknown on page 2 it falls through to the search,
    // which is friendlier than acting on a stale row
    var searched = await _router.RenderAsync(chat, "Dup");

    Assert.That(searched!.ReplyText, Is.EqualTo("page 1/1 count 2"));
    Assert.That(searched.Labels(), Does.Contain("Dup").And.Contain("Dup #2"));
  }

  [Test]
  public async Task Free_Text_Filters_And_Resets_The_Page()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "List");
    await _router.RenderAsync(chat, "Next ▶");

    var searched = await _router.RenderAsync(chat, "I1");

    Assert.That(searched!.ReplyText, Is.EqualTo("page 1/1 count 1"));
    Assert.That(searched.Labels(), Does.Contain("I10"));
  }

  [Test]
  public async Task Custom_Action_Row_Routes_And_Can_Repaint()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "List");
    await _router.RenderAsync(chat, "Next ▶");

    var refreshed = await _router.RenderAsync(chat, "refresh");

    Assert.That(refreshed!.ReplyText, Is.EqualTo("page 2/2 count 5"));
  }

  [Test]
  public async Task Back_From_The_List_Leads_To_Start()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "List");

    var back = await _router.RenderAsync(chat, "Back");

    Assert.That(back!.ReplyText, Is.EqualTo("start"));
  }

  [Test]
  public async Task The_Stateful_Instance_Remembers_The_Active_Page_Between_Forms()
  {
    var chat = TestBot.Chat();

    await _router.RenderAsync(chat, "List");
    await _router.RenderAsync(chat, "Next ▶");

    var item = await _router.RenderAsync(chat, "I7");
    Assert.That(item!.ReplyText, Is.EqualTo("item:7"));

    // the browser instance stays atop the history and still sits on page 2
    var repaint = await _router.RenderAsync(chat, "refresh");
    Assert.That(repaint!.ReplyText, Is.EqualTo("page 2/2 count 5"));
  }

  [Test]
  public async Task Pagination_Texts_Follow_The_Chat_Language()
  {
    var router = TestBot.Router(
      services => services.AddSingleton<IChatLanguageResolver, RussianFor99Resolver>(),
      configurator => configurator
        .RegisterStartForm<StartStub>()
        .RegisterFormWithKey<PagedStub>("List"));

    var russian = await router.RenderAsync(TestBot.Chat(99), "List");
    Assert.That(russian!.Labels(), Does.Contain("Вперёд ▶").And.Contain("Назад"));

    var english = await router.RenderAsync(TestBot.Chat(1), "List");
    Assert.That(english!.Labels(), Does.Contain("Next ▶").And.Contain("Back"));
  }
}
