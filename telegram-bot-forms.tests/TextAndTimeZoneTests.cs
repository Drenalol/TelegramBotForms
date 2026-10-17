using Drenalol.Telegram.Bot.Forms.Localization;
using Drenalol.Telegram.Bot.Forms.TimeZones;
using NUnit.Framework;

namespace Drenalol.Telegram.Bot.Forms.Tests;

[TestFixture]
public class TextAndTimeZoneTests
{
  [Test]
  public void Default_Localizer_Answers_Known_Languages()
  {
    var localizer = new DefaultTextLocalizer();

    Assert.That(localizer.Get(TextKey.Back, "ru"), Is.EqualTo("Назад"));
    Assert.That(localizer.Get(TextKey.Back, "en"), Is.EqualTo("Back"));
    Assert.That(localizer.SupportedLanguages, Is.EquivalentTo(new[] { "ru", "en" }));
  }

  [Test]
  public void Default_Localizer_Falls_Back_For_Unknown_Language()
  {
    var localizer = new DefaultTextLocalizer();

    Assert.That(localizer.Get(TextKey.NextPage, "de"), Is.EqualTo(localizer.Get(TextKey.NextPage, localizer.DefaultLanguage)));
  }

  [Test]
  public async Task Default_Chat_Language_Resolver_Answers_Default_Language()
  {
    var localizer = new DefaultTextLocalizer();
    var resolver = new DefaultChatLanguageResolver(localizer);

    Assert.That(await resolver.ResolveAsync(TestBot.Chat()), Is.EqualTo(localizer.DefaultLanguage));
  }

  [Test]
  public void Popular_Time_Zones_Carry_Utc_Offsets()
  {
    var zones = TelegramTimeZones.Popular();

    Assert.That(zones.Select(zone => zone.Id), Does.Contain("UTC"));
    Assert.That(zones.First(zone => zone.Id == "UTC").Label, Does.Contain("UTC+00:00"));
  }

  [Test]
  public void Unknown_Time_Zone_Labels_Fall_Back_To_The_Raw_Id() =>
    Assert.That(TelegramTimeZones.FormatLabel("Not/AZone"), Is.EqualTo("Not/AZone"));
}
