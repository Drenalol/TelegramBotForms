using Drenalol.Telegram.Bot.Forms;
using ExampleBot.Data;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ExampleBot.Bot;

/// <summary>
/// A detail page reached by navigation only (no keyboard text registers it): the list
/// forwards the item id, the page renders the card and a Back row that walks the
/// history right back to the exact page of the list the user came from.
/// <para>
/// The card is a <b>rich message</b>: a real table for the attributes, a collapsible
/// details block and a button row inside the message. Rich messages need a recent client,
/// so a bot that cannot assume one keeps a text version of the same screen (the framework
/// README describes where that fallback belongs).
/// </para>
/// </summary>
public sealed class ProductForm : FormBase
{
  private readonly ProductCatalog _catalog;
  private readonly WatchStore _watched;

  public ProductForm(ProductCatalog catalog, WatchStore watched)
  {
    _catalog = catalog;
    _watched = watched;
  }

  public override async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    if (!int.TryParse(context.Text, out var id) || _catalog.Find(id) is not { } product)
      return context.Reply("Product not found.");

    var language = await GetLanguageAsync(context.Chat, cancellationToken);
    var backRow = await CreateBackButtonRowAsync(context.Chat, cancellationToken);

    return context.ReplyRich(BuildCard(product, language, _watched.IsWatched(context.Chat.Id, product.Id)), [backRow]);
  }

  /// <summary>The card as a rich message: heading, attribute table, the long note behind
  /// a collapsible detail, the watch button (its callback data is a registered command, so
  /// the tap routes whatever form happens to wait in this chat's history) and Back.</summary>
  private static InputRichMessage BuildCard(Product product, string language, bool watching) =>
    new()
    {
      Blocks =
      [
        new InputRichBlockParagraph { Text = Bold(Text(product.Name)) },
        new InputRichBlockTable
        {
          IsBordered = true,
          IsStriped = true,
          Cells =
          [
            [Header(Text(ExampleTexts.SpecField(language))), Header(Text(ExampleTexts.SpecValue(language)))],
            [Cell(Text(ExampleTexts.SpecCategory(language)), RichBlockTableCellAlign.Left), Cell(Text(product.Category), RichBlockTableCellAlign.Right)],
            [Cell(Text(ExampleTexts.SpecPrice(language)), RichBlockTableCellAlign.Left), Cell(Text($"{product.Price:0.##} $"), RichBlockTableCellAlign.Right)],
            [Cell(Text(ExampleTexts.SpecId(language)), RichBlockTableCellAlign.Left), Cell(Text(product.Id.ToString()), RichBlockTableCellAlign.Right)],
          ],
        },
        new InputRichBlockDetails
        {
          Summary = Text(ExampleTexts.DetailsSummary(language)),
          Blocks = [new InputRichBlockParagraph { Text = Text(ExampleTexts.DetailsNote(language)) }],
        },
        new InputRichBlockButtons
        {
          Buttons =
          [
            // a tap inside a rich message is a callback: the data has to be something the
            // router resolves on its own, and a command is exactly that
            new RichMessageButton(Text(ExampleTexts.WatchButton(language, watching)))
            {
              CallbackData = $"watch {product.Id}",
              Style = RichMessageButtonStyle.Primary,
            },
          ],
        },
        new InputRichBlockFooter { Text = Text(ExampleTexts.RichCardFooter(language)) },
      ],
    };

  private static RichText Text(string value) => new RichTextText { Text = value };

  private static RichText Bold(RichText inner) => new RichTextBold { Text = inner };

  // Valign is mandatory on every cell: Telegram.Bot's converter throws on its default value
  private static RichBlockTableCell Cell(RichText text, RichBlockTableCellAlign align) =>
    new() { Text = text, Align = align, Valign = RichBlockTableCellValign.Top };

  private static RichBlockTableCell Header(RichText text) =>
    new() { Text = text, IsHeader = true, Align = RichBlockTableCellAlign.Center, Valign = RichBlockTableCellValign.Top };
}
