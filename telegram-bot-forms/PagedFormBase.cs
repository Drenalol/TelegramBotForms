using Drenalol.Telegram.Bot.Forms.Localization;
using Telegram.Bot.Types.ReplyMarkups;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>The states a paged list form handles out of the box.</summary>
public enum PagedFormState
{
  Previous,
  Next,
  Item,
}

/// <summary>One page of data plus the total number of matching items.</summary>
public sealed record PagedPage<TItem>(IReadOnlyList<TItem> Items, int TotalCount) where TItem : notnull;

/// <summary>
/// A paginated reply-keyboard browser: rows of readable labels built by
/// <see cref="EncodeRow"/>, ◀ Prev / Next ▶ navigation in the chat's language, a "Back"
/// row and an optional free-text search (any unregistered message filters the list).
/// <para>
/// Keyboard labels double as routing keys, and a raw GUID overflows the Telegram
/// reply-markup limits on big accounts - so labels are produced by
/// <see cref="EncodeRow"/>, made unique per page ("label #2") and mapped back to items
/// through a page-scoped map. A press on a stale label simply repaints the page.
/// </para>
/// <para>
/// The form is stateful: the browser instance (with its current page and search) lives
/// in the chat's navigation history, so "Back" returns to the exact page the user left.
/// </para>
/// </summary>
public abstract class PagedFormBase<TItem> : NavigationFormBase<PagedFormState> where TItem : notnull
{
  private const int DefaultPageSize = 20;

  // Page-scoped label -> item map: labels are readable but not unique by themselves,
  // the items live here until the next render rebuilds the map.
  private readonly Dictionary<string, TItem> _itemsByLabel = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, RenderAction> _actions = new(StringComparer.OrdinalIgnoreCase);

  private int _page = 1;
  private string? _search;

  protected PagedFormBase()
  {
    AddRenderOn(PagedFormState.Previous, string.Empty, PreviousAsync);
    AddRenderOn(PagedFormState.Next, string.Empty, NextAsync);
    AddRenderOn(PagedFormState.Item, string.Empty, ItemAsync);
  }

  /// <summary>Items per page. The reply-markup size limit suggests keeping it at or below 20.</summary>
  protected virtual int PageSize => DefaultPageSize;

  /// <summary>When true (the default), any unregistered text filters the list via <see cref="LoadPageAsync"/>.</summary>
  protected virtual bool SupportsSearch => true;

  /// <summary>The current 1-based page.</summary>
  protected int Page => _page;

  /// <summary>The active search query, or null when the list is unfiltered.</summary>
  protected string? Search => _search;

  /// <summary>Loads one page of data (1-based <paramref name="page"/>), optionally filtered.</summary>
  protected abstract Task<PagedPage<TItem>> LoadPageAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

  /// <summary>Turns an item into its readable keyboard label (keep it short: labels are routing keys).</summary>
  protected abstract string EncodeRow(TItem item);

  /// <summary>Renders the item the user picked (show it here or navigate to a detail form).</summary>
  protected abstract Task<MessageContext> RenderItemAsync(TItem item, MessageContext context, CancellationToken cancellationToken);

  /// <summary>Builds the message above the keyboard (title, counters, hints).</summary>
  protected abstract Task<string> BuildTextAsync(IReadOnlyList<TItem> items, int page, int totalPages, CancellationToken cancellationToken);

  /// <summary>
  /// Adds the item rows and the navigation rows; override to append custom rows between
  /// them (register their routing once with <see cref="AddAction"/>).
  /// </summary>
  protected virtual Task BuildExtraRowsAsync(IReadOnlyList<TItem> items, MessageContext context, CancellationToken cancellationToken) =>
    Task.CompletedTask;

  /// <summary>Registers routing for a custom button text (idempotent, safe to call per render).</summary>
  protected void AddAction(string text, RenderAction action) => _actions[text] = action;

  /// <summary>Adds a keyboard row routed to a custom action (see <see cref="AddAction"/>).</summary>
  protected void AddActionRow(string text, RenderAction action)
  {
    Rows.Add([new KeyboardButton(text)]);
    AddAction(text, action);
  }

  /// <summary>Re-renders the current page (after an external change or a custom action).</summary>
  protected Task<MessageContext> RenderCurrentPageAsync(MessageContext context, CancellationToken cancellationToken) =>
    RenderListAsync(context, cancellationToken);

  protected override Task<MessageContext> RenderInitAsync(MessageContext context, CancellationToken cancellationToken)
  {
    _search = null;
    _page = 1;

    return RenderListAsync(context, cancellationToken);
  }

  protected override IRender GetRender(MessageContext context) =>
    _actions.TryGetValue(context.Text, out var action) ? CustomRender(action) : base.GetRender(context);

  // Any unregistered text (no command, no button, no row) filters the list; a list
  // without a search keeps the base behaviour.
  protected override IRender ReenterOnUnknownText(MessageContext context) => SupportsSearch
    ? CustomRender(SearchAsync)
    : base.ReenterOnUnknownText(context);

  private Task<MessageContext> PreviousAsync(MessageContext context, CancellationToken cancellationToken)
  {
    if (_page > 1)
      _page--;

    return RenderListAsync(context, cancellationToken);
  }

  private Task<MessageContext> NextAsync(MessageContext context, CancellationToken cancellationToken)
  {
    _page++;

    return RenderListAsync(context, cancellationToken);
  }

  private Task<MessageContext> SearchAsync(MessageContext context, CancellationToken cancellationToken)
  {
    var query = context.Text.Trim();

    if (query.Length == 0)
      return RenderListAsync(context, cancellationToken);

    _search = query;
    _page = 1;

    return RenderListAsync(context, cancellationToken);
  }

  private Task<MessageContext> ItemAsync(MessageContext context, CancellationToken cancellationToken)
  {
    // The row was rendered before and the item is gone meanwhile — repaint the current
    // page instead of parsing a stale label
    if (!_itemsByLabel.TryGetValue(context.Text, out var item))
      return RenderListAsync(context, cancellationToken);

    return RenderItemAsync(item, context, cancellationToken);
  }

  private async Task<MessageContext> RenderListAsync(MessageContext context, CancellationToken cancellationToken)
  {
    var result = await LoadPageAsync(_search, _page, PageSize, cancellationToken);

    var totalPages = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)PageSize));

    // The item that made this page exist was deleted — fall back to the last real page
    if (result.Items.Count == 0 && _page > totalPages)
    {
      _page = totalPages;
      result = await LoadPageAsync(_search, _page, PageSize, cancellationToken);
    }

    Rows.Clear();
    _buttons.Clear();
    _itemsByLabel.Clear();

    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var item in result.Items)
    {
      var label = MakeUniqueLabel(EncodeRow(item), used);

      _itemsByLabel[label] = item;
      AddRow(label, PagedFormState.Item);
    }

    await BuildExtraRowsAsync(result.Items, context, cancellationToken);

    var language = await GetLanguageAsync(context.Chat, cancellationToken);

    if (_page > 1)
    {
      var prev = Localizer.PreviousPage(language);
      _buttons[prev] = PagedFormState.Previous;

      if (_page < totalPages)
      {
        var next = Localizer.NextPage(language);
        _buttons[next] = PagedFormState.Next;

        Rows.Add([new KeyboardButton(prev), new KeyboardButton(next)]);
      }
      else
        Rows.Add([new KeyboardButton(prev)]);
    }
    else if (totalPages > 1)
    {
      // First page: no "back" to draw, so the "forward" button spans the row
      var next = Localizer.NextPage(language);
      _buttons[next] = PagedFormState.Next;

      Rows.Add([new KeyboardButton(next)]);
    }

    AddBackButton(Localizer.Back(language));

    var text = await BuildTextAsync(result.Items, _page, totalPages, cancellationToken);

    return context.Reply(text, replyMarkup: BuildKeyboard());
  }

  /// <summary>
  /// Labels are not unique by nature (two identical items read the same), so duplicates
  /// of a page get a counter suffix; the same page-scoped map routes presses back.
  /// </summary>
  private static string MakeUniqueLabel(string label, HashSet<string> used)
  {
    if (used.Add(label))
      return label;

    var counter = 2;
    string candidate;

    do
      candidate = $"{label} #{counter++}";
    while (!used.Add(candidate));

    return candidate;
  }
}
