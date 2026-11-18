using ProjectMass.Models;

namespace ProjectMass.Services;

// Picks a pictogram category for foods without a photo: stored category first, then Open Food Facts tags, then the name.
public static class FoodCategories
{
    // Checked in this order: "Skyr mit Beeren" is dairy, "Thunfisch in Wasser" fish, "Whey-Shake" a drink.
    private static readonly (FoodCategory Category, string[] Long, string[] Exact)[] Keywords =
    {
        (FoodCategory.Dairy, new[] { "skyr", "joghurt", "jogurt", "yogurt", "quark", "milch", "käse", "kaese", "frischkäse", "mozzarella", "parmesan", "feta", "pudding", "kefir", "sahne", "cottage" }, new[] { "butter" }),
        (FoodCategory.Egg, new[] { "rührei", "spiegelei", "omelett", "eiklar" }, new[] { "ei", "eier" }),
        (FoodCategory.Fish, new[] { "fisch", "lachs", "thunfisch", "garnele", "shrimp", "forelle", "kabeljau", "seelachs", "hering", "makrele", "sardine" }, Array.Empty<string>()),
        (FoodCategory.Meat, new[] { "hähnchen", "haehnchen", "huhn", "chicken", "pute", "truthahn", "rind", "schwein", "hack", "steak", "schinken", "wurst", "salami", "speck", "bacon", "lamm", "döner", "burger", "fleisch", "kebab" }, Array.Empty<string>()),
        (FoodCategory.Drink, new[] { "shake", "saft", "smoothie", "limo", "kaffee", "espresso", "latte", "schorle", "drink", "getränk" }, new[] { "cola", "tee", "wasser", "bier", "wein" }),
        (FoodCategory.Nuts, new[] { "nuss", "nüsse", "mandel", "erdnuss", "cashew", "pistazie", "walnuss", "haselnuss", "samen", "kerne" }, Array.Empty<string>()),
        (FoodCategory.Sweets, new[] { "schoko", "riegel", "keks", "cookie", "gummi", "bonbon", "kuchen", "torte", "waffel", "praline", "eiscreme", "speiseeis", "zucker", "honig", "marmelade", "nutella", "croissant", "donut" }, new[] { "eis" }),
        (FoodCategory.Grain, new[] { "brot", "brötchen", "toast", "hafer", "müsli", "muesli", "porridge", "reis", "nudel", "pasta", "spaghetti", "gnocchi", "kartoffel", "pommes", "wrap", "tortilla", "mehl", "cornflakes", "bagel", "couscous", "bulgur", "quinoa" }, Array.Empty<string>()),
        (FoodCategory.Fruit, new[] { "apfel", "banane", "beere", "erdbeer", "himbeer", "heidelbeer", "orange", "mandarine", "traube", "kiwi", "mango", "ananas", "birne", "pfirsich", "melone", "obst", "kirsche", "zitrone" }, Array.Empty<string>()),
        (FoodCategory.Vegetable, new[] { "gemüse", "salat", "tomate", "gurke", "paprika", "brokkoli", "karotte", "möhre", "zwiebel", "spinat", "zucchini", "pilz", "champignon", "bohne", "linse", "erbse", "kichererbse", "mais", "kohl", "avocado" }, Array.Empty<string>())
    };

    // Open Food Facts category tags (en:...) mapped to our categories, most specific first.
    private static readonly (FoodCategory Category, string[] Tags)[] OffTags =
    {
        (FoodCategory.Drink, new[] { "en:beverages", "en:drinks", "en:waters" }),
        (FoodCategory.Dairy, new[] { "en:dairies", "en:cheeses", "en:yogurts", "en:milks", "en:fermented-milk-products" }),
        (FoodCategory.Egg, new[] { "en:eggs" }),
        (FoodCategory.Fish, new[] { "en:fishes", "en:seafood" }),
        (FoodCategory.Meat, new[] { "en:meats", "en:poultries", "en:poultry", "en:sausages", "en:hams" }),
        (FoodCategory.Nuts, new[] { "en:nuts", "en:seeds", "en:nut-butters" }),
        (FoodCategory.Sweets, new[] { "en:sweet-snacks", "en:chocolates", "en:confectioneries", "en:biscuits-and-cakes", "en:desserts" }),
        (FoodCategory.Grain, new[] { "en:cereals-and-potatoes", "en:breads", "en:pastas", "en:rices", "en:breakfast-cereals", "en:cereals-and-their-products" }),
        (FoodCategory.Fruit, new[] { "en:fruits", "en:fruits-based-foods" }),
        (FoodCategory.Vegetable, new[] { "en:vegetables", "en:vegetables-based-foods", "en:legumes" }),
        (FoodCategory.Meal, new[] { "en:meals", "en:prepared-meals" })
    };

    public static FoodCategory Resolve(FoodItem food)
    {
        if (food.Category != FoodCategory.Unknown) return food.Category;
        if (string.Equals(food.Brand, "Rezept", StringComparison.OrdinalIgnoreCase)) return FoodCategory.Meal;
        return FromName(food.Name);
    }

    public static FoodCategory FromOffTags(IEnumerable<string> tags)
    {
        var set = tags.Select(t => t.Trim().ToLowerInvariant()).ToHashSet();
        foreach (var (category, known) in OffTags)
            if (known.Any(set.Contains)) return category;
        return FoodCategory.Unknown;
    }

    public static FoodCategory FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return FoodCategory.Unknown;
        var text = name.ToLowerInvariant();
        var tokens = text.Split(new[] { ' ', '-', ',', '(', ')', '/', '.', '&', '+' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var (category, longWords, exact) in Keywords)
            if (longWords.Any(text.Contains) || exact.Any(e => tokens.Contains(e))) return category;
        return FoodCategory.Unknown;
    }

    public static string Label(FoodCategory category) => category switch
    {
        FoodCategory.Dairy => "Milchprodukt",
        FoodCategory.Meat => "Fleisch",
        FoodCategory.Fish => "Fisch",
        FoodCategory.Egg => "Ei",
        FoodCategory.Grain => "Getreide und Beilagen",
        FoodCategory.Fruit => "Obst",
        FoodCategory.Vegetable => "Gemüse",
        FoodCategory.Drink => "Getränk",
        FoodCategory.Sweets => "Süßes",
        FoodCategory.Nuts => "Nüsse und Samen",
        FoodCategory.Meal => "Gericht",
        _ => "Lebensmittel"
    };
}
