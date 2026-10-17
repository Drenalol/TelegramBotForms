using Drenalol.Telegram.Bot.Forms;
using ExampleBot.Data;

namespace ExampleBot.Bot;

/// <summary>
/// The paginated browser: <see cref="PagedFormBase{TItem}"/> does pages, search,
/// uniqueness of row labels, Prev/Next in the chat's language and the Back row; the
/// application supplies data access, row labels, the header and a custom "Refresh" row.
/// </summary>
public sealed class ProductsForm : PagedFormBase<Product>
{
  private readonly ProductCatalog _catalog;

  public ProductsForm(ProductCatalog catalog) => _catalog = catalog;

  protected override int PageSize => 8;

  protected override Task<PagedPage<Product>> LoadPageAsync(string? search, int page, int pageSize, CancellationToken cancellationToken) =>
    Task.FromResult(_catalog.Search(search, page, pageSize));

  protected override string EncodeRow(Product item) => $"{item.Name} · {item.Price:0.##} $";

  protected override Task<MessageContext> RenderItemAsync(Product item, MessageContext context, CancellationToken cancellationToken) =>
    // the detail form wants the id, not the row label
    NavigateToAsync<ProductForm>(context.Forward(item.Id.ToString("D4")), cancellationToken);

  // resolved per render (BuildExtraRowsAsync runs before BuildTextAsync)
  private string _language = ExampleTexts.English;

  protected override Task<string> BuildTextAsync(IReadOnlyList<Product> items, int page, int totalPages, CancellationToken cancellationToken) =>
    Task.FromResult(ExampleTexts.ProductsTitle(_language, items.Count, page, totalPages, Search));

  protected override async Task BuildExtraRowsAsync(IReadOnlyList<Product> items, MessageContext context, CancellationToken cancellationToken)
  {
    _language = await GetLanguageAsync(context.Chat, cancellationToken);

    // a custom action row: routed through the framework's action registry
    AddActionRow(ExampleTexts.RefreshButton(_language), RefreshAsync);
  }

  private Task<MessageContext> RefreshAsync(MessageContext context, CancellationToken cancellationToken) =>
    RenderCurrentPageAsync(context, cancellationToken);
}
