using Drenalol.Telegram.Bot.Forms;

namespace ExampleBot.Data;

public sealed record Product(int Id, string Name, string Category, decimal Price);

/// <summary>Fake in-memory product data: the example needs a list to page through.</summary>
public sealed class ProductCatalog
{
  private static readonly string[] Adjectives = ["Compact", "Pro", "Ultra", "Mini", "Turbo", "Classic", "Smart", "Heavy"];
  private static readonly string[] Nouns = ["Widget", "Gadget", "Sensor", "Module", "Adapter", "Beacon", "Relay", "Dock"];
  private static readonly string[] Categories = ["Tools", "Electronics", "Spare parts"];

  private readonly List<Product> _products;

  public ProductCatalog()
  {
    _products = [];

    for (var i = 1; i <= 45; i++)
    {
      var name = $"{Adjectives[i * 3 % Adjectives.Length]} {Nouns[i * 5 % Nouns.Length]} {i}";
      var category = Categories[i % Categories.Length];
      var price = 3.5m + i * 2.25m;

      _products.Add(new Product(i, name, category, price));
    }
  }

  public PagedPage<Product> Search(string? search, int page, int pageSize)
  {
    var filtered = string.IsNullOrWhiteSpace(search)
      ? _products
      : _products
        .Where(product => product.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                          product.Category.Contains(search, StringComparison.OrdinalIgnoreCase))
        .ToList();

    var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

    return new PagedPage<Product>(items, filtered.Count);
  }

  public Product? Find(int id) => _products.Find(product => product.Id == id);
}
