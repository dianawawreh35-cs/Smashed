namespace CallCenter.Server.Data.Entities;

/// <summary>
/// A group of menu items — burgers, fries, drinks. Table <c>menu_categories</c>
/// (A-66, S-59).
/// </summary>
/// <remarks>
/// Its own table rather than a text column on the item, for the same reasons
/// branches are: the supervisor renames them, they have an order the menu is
/// read in, and a typo would otherwise silently create a thirteenth category
/// holding one item.
/// </remarks>
public class MenuCategory
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Folded for comparison by <c>NameNormalizer</c>. See <see cref="MenuItem"/>.</summary>
    public string NameNormalised { get; set; } = null!;

    /// <summary>The order the printed menu uses. Burgers first, add-ons last.</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<MenuItem> Items { get; } = new List<MenuItem>();
}

/// <summary>
/// One thing on the menu: what it is called, what is in it, what it costs and a
/// picture of it. Table <c>menu_items</c> (A-66, S-59).
/// </summary>
/// <remarks>
/// An agent taking an order is asked "what comes in the Overdose?" and "how much
/// is a double with a meal?" while the customer waits. Today that is a printed
/// menu beside the laptop, which goes out of date the moment a price changes.
///
/// <b>Arabic only.</b> The source spreadsheet carries English too, and it is not
/// stored: the restaurant's menu is Arabic, the agents speak Arabic to
/// customers, and a second set of names is a second thing to keep correct. The
/// English is still in <c>docs/smashed_menu.xlsx</c> if it is ever wanted.
/// </remarks>
public class MenuItem
{
    public Guid Id { get; set; }

    public Guid CategoryId { get; set; }
    public MenuCategory Category { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>
    /// <see cref="Name"/> folded by <c>CallCenter.Shared.Text.NameNormalizer</c>,
    /// the same treatment contact names and delivery areas get.
    /// </summary>
    /// <remarks>
    /// These are transliterated English words written in Arabic — سماشد,
    /// ماشروم, كرسبي — and nobody spells them consistently. An agent hearing
    /// "crispy" over a bad line has to find كرسبي whether they type كرسبي or
    /// كريسبي.
    /// </remarks>
    public string NameNormalised { get; set; } = null!;

    /// <summary>What is in it, as the menu prints it. Null where the menu says nothing.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// The price of the item alone, or of the sandwich where there is also a
    /// meal.
    /// </summary>
    /// <remarks>
    /// Null means the menu does not print one — there is exactly one such item,
    /// a combination offer. Null and zero are different: zero is the price of a
    /// free extra, and an agent must be able to tell "free" from "ask the
    /// branch".
    /// </remarks>
    public decimal? Price { get; set; }

    /// <summary>
    /// The price with fries and a drink, where the menu offers it. Null for
    /// anything that is not a sandwich.
    /// </summary>
    public decimal? MealPrice { get; set; }

    /// <summary>
    /// True when <see cref="Price"/> is an amount <b>added</b> to something else
    /// rather than a price of its own — the menu writes these as "+2".
    /// </summary>
    /// <remarks>
    /// Without this, "إضافة الجبنة — 2" reads as a portion of cheese costing 2,
    /// and an agent would quote it as a line of its own instead of adding it to
    /// the burger.
    /// </remarks>
    public bool IsSurcharge { get; set; }

    /// <summary>
    /// The file holding this item's picture, under the menu-images folder. Null
    /// where the menu has none — the add-ons have no photographs.
    /// </summary>
    /// <remarks>
    /// A file on disk, not bytes in this row. The first version stored the bytes
    /// here; that was wrong because <b>rsync is incremental and
    /// <c>pg_dump</c> is not</b>. Menu photographs never change, so on disk the
    /// nightly backup copies them once, while in the database they were
    /// re-dumped and re-copied every night for ever. The backup script already
    /// covers a data folder — the recordings — so files were never the extra
    /// thing to remember they were assumed to be.
    ///
    /// The name is the item's id plus an extension. Storing it rather than
    /// deriving it means the list can say whether an item has a picture without
    /// asking the disk once per row.
    /// </remarks>
    public string? ImageFileName { get; set; }

    /// <summary>Position within the category, following the printed menu.</summary>
    public int SortOrder { get; set; }

    /// <summary>False hides it from agents without losing its price or picture.</summary>
    public bool IsActive { get; set; } = true;

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
