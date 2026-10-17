using Drenalol.Telegram.Bot.Forms;
using ExampleBot.Data;

namespace ExampleBot.Bot;

/// <summary>
/// A detail page reached by navigation only (no keyboard text registers it): the list
/// forwards the item id, the page renders the card and a Back row that walks the
/// history right back to the exact page of the list the user came from.
/// </summary>
public sealed class ProductForm : FormBase
{
  private readonly ProductCatalog _catalog;

  public ProductForm(ProductCatalog catalog) => _catalog = catalog;

  public override async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    if (!int.TryParse(context.Text, out var id) || _catalog.Find(id) is not { } product)
      return context.Reply("Product not found.");

    return context.Reply(
      ExampleTexts.ProductCard(await GetLanguageAsync(context.Chat, cancellationToken), product),
      [await CreateBackButtonRowAsync(context.Chat, cancellationToken)]
    );
  }
}
