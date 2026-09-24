using CallCenter.Shared;

namespace CallCenter.Server.Data.Seed;

/// <summary>
/// The starting contents of a new database, from section 7 of
/// <c>docs/SCHEMA.md</c>. Data only — <see cref="DatabaseSeeder"/> applies it.
/// </summary>
public static class SeedData
{
    /// <summary>
    /// The four branches (S-41). Real names rather than placeholders: they are
    /// what the delivery lists below refer to, and an area cannot be attached
    /// to "Branch 3".
    /// </summary>
    /// <remarks>
    /// Still renameable by the supervisor. Anything that referred to a branch
    /// by name would break when they did, which is why nothing does — the
    /// delivery areas hold a branch id, and this list is only used to create
    /// them on a fresh database.
    /// </remarks>
    public static readonly string[] Branches =
    [
        "رافات", "بطن الهوى", "ايكون", "نابلس",
    ];

    /// <summary>
    /// The menu's categories, in the order the printed menu reads (A-66).
    /// </summary>
    public static readonly string[] MenuCategories =
    [
        "كلاسيك سماشد برغر",
        "ماشروم سويس سماشد برغر",
        "رويال سماشد برغر",
        "روست بيف سماشد برغر",
        "فلات برغر",
        "وجبات الأطفال",
        "وجبات",
        "عروض خاصة",
        "المقبلات",
        "بطاطا",
        "مشروبات",
        "إضافات",
    ];

    /// <summary>One line of the printed menu (A-66).</summary>
    /// <param name="Category">Matched by name against <see cref="MenuCategories"/>.</param>
    /// <param name="Price">
    /// The item alone, or the sandwich price where a meal is also offered. Null
    /// where the menu prints none — which is not the same as zero, the price of a
    /// free extra.
    /// </param>
    /// <param name="MealPrice">With fries and a drink, where the menu offers it.</param>
    /// <param name="IsSurcharge">
    /// The menu writes these as "+2": an amount added to another item, not a
    /// price of its own.
    /// </param>
    /// <param name="Image">
    /// A file under <c>Data/Seed/MenuImages</c>, embedded in the assembly. Null
    /// for the add-ons, which the printed menu does not photograph.
    /// </param>
    public record MenuSeedItem(
        string Category,
        string Name,
        string? Description,
        decimal? Price,
        decimal? MealPrice,
        bool IsSurcharge,
        string? Image);

    /// <summary>
    /// The menu as printed, transcribed from <c>docs/smashed_menu.xlsx</c>
    /// (A-66).
    /// </summary>
    /// <remarks>
    /// <b>Arabic only.</b> The spreadsheet carries English names and contents as
    /// well and they are deliberately not stored: the menu is Arabic, the agents
    /// speak Arabic to customers, and a second set of names is a second thing to
    /// keep correct. The English is still in the spreadsheet if it is ever
    /// wanted.
    ///
    /// Prices come from the menu in four shapes and each means something
    /// different — see <see cref="MenuSeedItem"/>. The one item with no price is
    /// a combination offer the printed menu does not price.
    /// </remarks>
    public static readonly MenuSeedItem[] MenuItems =
    [
        new("كلاسيك سماشد برغر", "سنجل سماشد برغر", "لحمة 150 غم، سبيشل صوص، خس، بندورة، بصل، جبنة شيدر. الوجبة تشمل بطاطا وكولا.", 26m, 36m, false, "item00.png"),
        new("كلاسيك سماشد برغر", "دبل سماشد برغر", "لحمة 200 غم، سبيشل صوص، خس، بندورة، بصل، جبنة شيدر. الوجبة تشمل بطاطا وكولا.", 30m, 40m, false, "item01.png"),
        new("كلاسيك سماشد برغر", "جريلد تشيز بن", "طبقتين من الخبز المحمص المحشو بالجبنة، 250 غم لحمة، سبيشل صوص، خس، بندورة، بصل. الوجبة تشمل بطاطا وكولا.", 37m, 47m, false, "item02.png"),
        new("كلاسيك سماشد برغر", "اوفردوز سماشد برغر", "لحمة 300 غم، سبيشل صوص، خس، بندورة، بصل، جبنة شيدر. الوجبة تشمل بطاطا وكولا.", 37m, 47m, false, "item03.png"),
        new("ماشروم سويس سماشد برغر", "سنجل ماشروم سويس", "لحمة 150 غم، ماشروم صوص، خس، بندورة، جبنة ايمنتال. الوجبة تشمل بطاطا وكولا.", 30m, 40m, false, "item04.png"),
        new("ماشروم سويس سماشد برغر", "دبل ماشروم سويس", "لحمة 200 غم، ماشروم صوص، خس، بندورة، جبنة ايمنتال. الوجبة تشمل بطاطا وكولا.", 36m, 46m, false, "item05.png"),
        new("رويال سماشد برغر", "سنجل رويال برغر", "لحمة 150 غم، كاتشب، مايونيز، ماسترد، خس، بندورة، بصل، مخلل، جبنة شيدر. الوجبة تشمل بطاطا وكولا.", 27m, 37m, false, "item06.png"),
        new("رويال سماشد برغر", "دبل رويال برغر", "لحمة 200 غم، كاتشب، مايونيز، ماسترد، خس، بندورة، بصل، مخلل، جبنة شيدر. الوجبة تشمل بطاطا وكولا.", 31m, 41m, false, "item07.png"),
        new("روست بيف سماشد برغر", "سنجل روست بيف برغر", "150 غم لحمة، مخلل، ماسترد، بصل مكرمل، صوص الروست بيف الخاص، شرائح الروست بيف، جبنة ايمنتال. الوجبة تشمل بطاطا وكولا.", 31m, 41m, false, "item08.png"),
        new("روست بيف سماشد برغر", "دبل روست بيف برغر", "200 غم لحمة، مخلل، ماسترد، بصل مكرمل، صوص الروست بيف الخاص، شرائح الروست بيف، جبنة ايمنتال. الوجبة تشمل بطاطا وكولا.", 36m, 46m, false, "item09.png"),
        new("روست بيف سماشد برغر", "شيكن راب", "المكونات غير مذكورة في المنيو. الوجبة تشمل بطاطا وكولا.", 26m, 36m, false, "item10.png"),
        new("فلات برغر", "سنجل فلات برغر", "لحمة 150 غم، سبيشل صوص، خس، بندورة، بصل، جبنة شيدر، خبزة مكبوسة عالجريل. الوجبة تشمل بطاطا ومشروب.", 27m, 37m, false, "item11.png"),
        new("فلات برغر", "دبل فلات برغر", "لحمة 200 غم، سبيشل صوص، خس، بندورة، بصل، جبنة شيدر، خبزة مكبوسة عالجريل. الوجبة تشمل بطاطا ومشروب.", 30m, 40m, false, "item12.png"),
        new("وجبات الأطفال", "سماشد برغر (وجبة أطفال)", "لحمة 100 غم، خس، بندورة، سبيشل صوص، جبنة، بطاطا، عصير، هدية.", 25m, null, false, "item13.png"),
        new("وجبات الأطفال", "كرسبي برغر (وجبة أطفال)", "دجاج كرسبي مقلي و مقرمش، خس، مايونيز، جبنة، بطاطا، عصير، هدية.", 25m, null, false, "item14.png"),
        new("وجبات الأطفال", "وجبة كرسبي (وجبة أطفال)", "قطع دجاج كرسبي مقلي و مقرمش، بطاطا، عصير، هدية.", 25m, null, false, "item15.png"),
        new("وجبات", "وجبة كرسبي الدجاج", "5 قطع كرسبي، بطاطا، كولا، خبزة.", 35m, null, false, "item16.png"),
        new("عروض خاصة", "2 ميني برجر (أي نوع)", "مع بطاطا وكولا وصوصات.", 43m, null, false, "item17.png"),
        new("عروض خاصة", "4 لقيمات سماشد", "مع بطاطا وكولا وصوصات.", 40m, null, false, "item18.png"),
        new("عروض خاصة", "عرض لقيمات سماشد (لقيمات + بطاطا + كولا)", "ظاهر في صورة العروض الخاصة، لكن لا يوجد اسم أو سعر مكتوب بجانبه.", null, null, false, "item19.png"),
        new("المقبلات", "قطع كرسبي", "5 قطع.", 25m, null, false, "item20.png"),
        new("المقبلات", "حلقات البصل", "5 قطع.", 10m, null, false, "item21.png"),
        new("المقبلات", "اصابع موزاريلا", "4 قطع.", 20m, null, false, "item22.png"),
        new("المقبلات", "أجنحة دجاج", "10 قطع.", 25m, null, false, "item23.png"),
        new("المقبلات", "قطع كرسبي مع بطاطا بالجبنة", "قطع كرسبي مع بطاطا بالجبنة. عدد القطع غير مذكور.", 25m, null, false, "item24.png"),
        new("المقبلات", "تشكن بوب كورن", "12 قطعة.", 20m, null, false, "item25.png"),
        new("المقبلات", "كرات الجبنة مع الهالبينو", "5 قطع.", 15m, null, false, "item26.png"),
        new("بطاطا", "بطاطا عادية (صغير)", "بطاطا عادية، حجم صغير.", 8m, null, false, "item27.png"),
        new("بطاطا", "بطاطا عادية (كبير)", "بطاطا عادية، حجم كبير.", 16m, null, false, "item28.png"),
        new("بطاطا", "بطاطا عادية مع جبنة وهالبينو", "بطاطا عادية مع جبنة وهالبينو.", 18m, null, false, "item29.png"),
        new("بطاطا", "بطاطا كيرلي (صغير)", "بطاطا كيرلي، حجم صغير.", 12m, null, false, "item30.png"),
        new("بطاطا", "بطاطا كيرلي (كبير)", "بطاطا كيرلي، حجم كبير.", 20m, null, false, "item31.png"),
        new("بطاطا", "بطاطا كيرلي مع جبنة وهالبينو", "بطاطا كيرلي مع جبنة وهالبينو.", 25m, null, false, "item32.png"),
        new("بطاطا", "بطاطا ويدجز (صغير)", "بطاطا ويدجز، حجم صغير.", 10m, null, false, "item33.png"),
        new("بطاطا", "بطاطا ويدجز (كبير)", "بطاطا ويدجز، حجم كبير.", 18m, null, false, "item34.png"),
        new("بطاطا", "بطاطا ويدجز مع جبنة وهالبينو", "بطاطا ويدجز مع جبنة وهالبينو.", 25m, null, false, "item35.png"),
        new("مشروبات", "ماء", "مياه معبأة.", 3m, null, false, "item36.png"),
        new("مشروبات", "عصير", "عصير معبأ.", 3m, null, false, "item37.png"),
        new("مشروبات", "شات كولا", "كولا علبة.", 3m, null, false, "item38.png"),
        new("إضافات", "إضافة الجبنة على البرجر", "جبنة إضافية على أي برجر.", 2m, null, true, null),
        new("إضافات", "تبديل إلى كيرلي", "تبديل بطاطا الوجبة إلى كيرلي.", 4m, null, true, null),
        new("إضافات", "تبديل إلى ويدجز", "تبديل بطاطا الوجبة إلى ويدجز.", 2m, null, true, null),
        new("إضافات", "إضافة صلصة جبنة شيدر سائلة", "صلصة جبنة شيدر سائلة إضافية.", 3m, null, true, null),
        new("إضافات", "إضافة هالبينو", "هالبينو إضافي.", 0m, null, true, null),
    ];

    /// <summary>
    /// Where the restaurant delivers, which branch covers it and what it costs
    /// (A-65, S-58). Imported from the branches' own price lists.
    /// </summary>
    /// <remarks>
    /// 228 areas, from two spreadsheets the restaurant already kept: one for
    /// the three Ramallah branches, one for Nablus. Three things were cleaned on
    /// the way in, each recorded here because a later reader comparing this
    /// against the spreadsheets will otherwise think the import lost rows:
    ///
    /// <list type="bullet">
    ///   <item>One Ramallah row spelled the Rafat branch without its alif. Same
    ///     branch, one letter; merged.</item>
    ///   <item>The Nablus list held one area twice, identical branch and price.
    ///     Imported once.</item>
    ///   <item>Three areas beside the Rafat branch are priced <b>0</b>. That is
    ///     free delivery for the street outside, not missing data, and nothing
    ///     may treat it as unset.</item>
    /// </list>
    ///
    /// The branch is named rather than keyed because these are seed rows: the
    /// seeder looks each one up by name against the branches it has just
    /// created.
    /// </remarks>
    public static readonly (string Area, string Branch, decimal Price)[] DeliveryAreas =
    [
        ("رافات", "رافات", 10m),
        ("الماصيون", "رافات", 10m),
        ("ام الشرايط", "رافات", 10m),
        ("سطح مرحبا", "رافات", 10m),
        ("البيرة", "رافات", 10m),
        ("الشرفة", "رافات", 10m),
        ("قلنديا البلد", "رافات", 10m),
        ("مستشفى رام الله", "رافات", 10m),
        ("كفر عقب", "رافات", 20m),
        ("بيرنبالا", "رافات", 20m),
        ("الجيب", "رافات", 30m),
        ("بدو", "رافات", 40m),
        ("مخيم قلنديا", "رافات", 25m),
        ("الجديرة", "رافات", 15m),
        ("الرام", "رافات", 40m),
        ("تل النصبة", "رافات", 10m),
        ("القبيبة", "رافات", 60m),
        ("بيت اجزا", "رافات", 60m),
        ("سميراميس", "رافات", 15m),
        ("بيت حنينا ضفة", "رافات", 33m),
        ("بيت سوريك", "رافات", 60m),
        ("مفروشات سلهب بجانب الفرع", "رافات", 0m),
        ("عيادة الدكتورة ضحى", "رافات", 0m),
        ("اماني الشني", "رافات", 0m),
        ("الطيرة", "بطن الهوى", 10m),
        ("الارسال", "بطن الهوى", 10m),
        ("حديقة قدورة", "بطن الهوى", 10m),
        ("البالوع", "بطن الهوى", 10m),
        ("المصايف", "بطن الهوى", 10m),
        ("بطن الهوى", "بطن الهوى", 10m),
        ("عين مصباح", "بطن الهوى", 10m),
        ("وسط البلد", "بطن الهوى", 10m),
        ("رام الله التحتا", "بطن الهوى", 10m),
        ("بيتونيا", "بطن الهوى", 10m),
        ("عين منجد", "بطن الهوى", 10m),
        ("عين عريك", "بطن الهوى", 30m),
        ("عين كينيا", "بطن الهوى", 20m),
        ("صناعة البيرة", "بطن الهوى", 10m),
        ("شارع نابلس", "بطن الهوى", 10m),
        ("شارع المستشفى", "بطن الهوى", 10m),
        ("سردا", "ايكون", 10m),
        ("الريحان", "ايكون", 12m),
        ("المزرعة الغربية", "ايكون", 25m),
        ("كوبر", "ايكون", 25m),
        ("بيرزيت", "ايكون", 15m),
        ("عين يبرود", "ايكون", 35m),
        ("ابو شخيدم", "ايكون", 20m),
        ("ضاحية النجمة", "ايكون", 10m),
        ("الحي الدبلوماسي", "ايكون", 10m),
        ("دوار القرع", "ايكون", 30m),
        ("روابي", "ايكون", 30m),
        ("ابو قش", "ايكون", 12m),
        ("ضاحية الغدير", "ايكون", 12m),
        ("ضاحية الريف", "ايكون", 12m),
        ("ضاحية التربية", "ايكون", 15m),
        ("الجلزون", "ايكون", 20m),
        ("ضاحية الزراعة", "ايكون", 15m),
        ("جفنا", "ايكون", 18m),
        ("ترمسعيا", "ايكون", 70m),
        ("عجول", "ايكون", 45m),
        ("عين سينيا", "ايكون", 30m),
        ("دورا القرع", "ايكون", 30m),
        ("راس العين", "نابلس", 16m),
        ("شركة الكهرباء", "نابلس", 15m),
        ("التعاون الاوسط", "نابلس", 15m),
        ("شارع 24", "نابلس", 13m),
        ("العامرية", "نابلس", 10m),
        ("حي طيبة", "نابلس", 14m),
        ("التعاون العلوي", "نابلس", 17m),
        ("كروم عاشور", "نابلس", 18m),
        ("شارع 10", "نابلس", 17m),
        ("خلة العامود", "نابلس", 18m),
        ("مستشفى الهلال الاحمر", "نابلس", 18m),
        ("نابلس الجديدة", "نابلس", 15m),
        ("شارع الطور", "نابلس", 18m),
        ("شارع الحرش", "نابلس", 20m),
        ("اسكان الكهرباء", "نابلس", 15m),
        ("شارع كشيكة", "نابلس", 17m),
        ("شارع المعري", "نابلس", 17m),
        ("مستشفى الامل", "نابلس", 17m),
        ("الطور", "نابلس", 20m),
        ("جبل الطور", "نابلس", 25m),
        ("عراق بورين", "نابلس", 30m),
        ("شارع تل", "نابلس", 12m),
        ("شارع ابو عبيدة", "نابلس", 13m),
        ("حي النور", "نابلس", 15m),
        ("شارع المأمون", "نابلس", 12m),
        ("اول شارع تل", "نابلس", 12m),
        ("وسط شارع تل", "نابلس", 13m),
        ("اخر شارع تل", "نابلس", 15m),
        ("نابلس الجديدة فلل الفاتح", "نابلس", 17m),
        ("نابلس الجديدة RG", "نابلس", 18m),
        ("الاكاديمية", "نابلس", 10m),
        ("طلعة علاء الدين", "نابلس", 10m),
        ("المعاجين", "نابلس", 12m),
        ("شارع الجرف", "نابلس", 10m),
        ("نقابة الصيادلة", "نابلس", 10m),
        ("شركة الاتصالات", "نابلس", 10m),
        ("مفرق البدوي", "نابلس", 10m),
        ("شارع تونس", "نابلس", 10m),
        ("طلعة بليبلة", "نابلس", 10m),
        ("مستشفى رفيديا", "نابلس", 10m),
        ("الجامعة القديمة", "نابلس", 10m),
        ("المخفية", "نابلس", 10m),
        ("المستشفى التخصصي", "نابلس", 10m),
        ("اسكان المهندسين - رفيديا", "نابلس", 10m),
        ("شارع النجاح", "نابلس", 10m),
        ("ضاحية النخيل", "نابلس", 10m),
        ("اسكان البيدر", "نابلس", 10m),
        ("عين الصبيان", "نابلس", 10m),
        ("دخلة ملحيس", "نابلس", 10m),
        ("شارع كمال جنبلاط", "نابلس", 10m),
        ("شارع مريج", "نابلس", 10m),
        ("شارع يافا", "نابلس", 10m),
        ("شارع 16", "نابلس", 10m),
        ("شارع 17", "نابلس", 10m),
        ("شارع 15", "نابلس", 10m),
        ("رفيديا", "نابلس", 10m),
        ("عبد الرحيم محمود", "نابلس", 10m),
        ("المخفية العلوي", "نابلس", 10m),
        ("اسكان المهندسين المخفية", "نابلس", 10m),
        ("شارع 25", "نابلس", 10m),
        ("شارع عمان", "نابلس", 20m),
        ("اسعاد الطفولة", "نابلس", 18m),
        ("شارع جمال عبد الناصر", "نابلس", 18m),
        ("المقاطعة", "نابلس", 18m),
        ("عراق التايه", "نابلس", 20m),
        ("كلية الروضة", "نابلس", 18m),
        ("شارع المي", "نابلس", 20m),
        ("طلعة الماطورات", "نابلس", 18m),
        ("بلاطة البلد", "نابلس", 20m),
        ("عسكر البلد", "نابلس", 22m),
        ("مخيم عسكر القديم", "نابلس", 25m),
        ("المسلخ", "نابلس", 22m),
        ("عسكر الجديد", "نابلس", 28m),
        ("الضاحية", "نابلس", 25m),
        ("دوار الفارس", "نابلس", 18m),
        ("دوار الحسبة", "نابلس", 22m),
        ("اول شارع القدس", "نابلس", 22m),
        ("المساكن الشعبية", "نابلس", 27m),
        ("مخيم بلاطة", "نابلس", 20m),
        ("شارع القدس", "نابلس", 25m),
        ("اسكان روجيب", "نابلس", 27m),
        ("المنطقة الصناعية", "نابلس", 25m),
        ("روجيب", "نابلس", 30m),
        ("السوق الشرقي", "نابلس", 17m),
        ("كفر قليل", "نابلس", 30m),
        ("شارع حلاوة", "نابلس", 17m),
        ("الضاحية العليا", "نابلس", 28m),
        ("اخر المساكن", "نابلس", 30m),
        ("وسط شارع القدس", "نابلس", 25m),
        ("اخر شارع القدس", "نابلس", 27m),
        ("اول الضاحية", "نابلس", 22m),
        ("وسط الضاحية", "نابلس", 25m),
        ("المساكن المدفع", "نابلس", 30m),
        ("طلعة الزينبيه", "نابلس", 16m),
        ("شارع سعد صايل", "نابلس", 17m),
        ("جسر التيتي", "نابلس", 18m),
        ("الاسكان النمساوي", "نابلس", 16m),
        ("اسكان المهندسين - شارع عصيرة", "نابلس", 17m),
        ("مستشفى الاتحاد", "نابلس", 17m),
        ("امتداد شارع السكة", "نابلس", 15m),
        ("شارع عصيرة", "نابلس", 17m),
        ("شارع ابن رشد", "نابلس", 18m),
        ("خلة الايمان", "نابلس", 20m),
        ("مستشفى النجاح", "نابلس", 18m),
        ("شارع الارصاد", "نابلس", 18m),
        ("شارع مؤتة", "نابلس", 20m),
        ("شارع الحجة عفيفة", "نابلس", 17m),
        ("طلعة اسو", "نابلس", 18m),
        ("شارع الرشيد", "نابلس", 18m),
        ("فطاير", "نابلس", 18m),
        ("جبل فطاير", "نابلس", 20m),
        ("شارع بيجر", "نابلس", 19m),
        ("شارع ابو بكر", "نابلس", 20m),
        ("شارع المنجرة", "نابلس", 20m),
        ("طلعة عماد الدين", "نابلس", 19m),
        ("سما نابلس", "نابلس", 20m),
        ("عصيرة الشمالية", "نابلس", 27m),
        ("طلعة زبلح", "نابلس", 18m),
        ("بليبوس الاوسط", "نابلس", 17m),
        ("المعاجين - القدس المفتوحة", "نابلس", 14m),
        ("اول خلة الايمان", "نابلس", 20m),
        ("وسط خلة الايمان", "نابلس", 21m),
        ("اخر خلة الايمان", "نابلس", 22m),
        ("واد التفاح", "نابلس", 10m),
        ("مفرق زواتا الاول", "نابلس", 10m),
        ("بيت ايبا", "نابلس", 15m),
        ("مفرق زواتا الثاني", "نابلس", 15m),
        ("جبل عجرم", "نابلس", 17m),
        ("زواتا", "نابلس", 15m),
        ("مخيم العين", "نابلس", 10m),
        ("بيت ايبا المنطقة الصناعية", "نابلس", 17m),
        ("العماير", "نابلس", 13m),
        ("الجنيد", "نابلس", 10m),
        ("بيت وزن", "نابلس", 10m),
        ("طريق صرة", "نابلس", 22m),
        ("صرة", "نابلس", 30m),
        ("حي المسك", "نابلس", 18m),
        ("فلل القمر", "نابلس", 15m),
        ("دير شرف", "نابلس", 25m),
        ("منتجع مارينا", "نابلس", 15m),
        ("قرية تل", "نابلس", 27m),
        ("حي الجنيد البلد", "نابلس", 10m),
        ("دوار قوصين", "نابلس", 18m),
        ("طريق قرية تل", "نابلس", 22m),
        ("منتزه البلدية", "نابلس", 11m),
        ("منتزه العائلات", "نابلس", 11m),
        ("شارع البساتين", "نابلس", 13m),
        ("شارع المدارس", "نابلس", 18m),
        ("شارع غرناطة", "نابلس", 15m),
        ("شارع شويترة", "نابلس", 12m),
        ("شارع فيصل", "نابلس", 18m),
        ("المجمع الغربي", "نابلس", 15m),
        ("مجمع القرى الغربي", "نابلس", 15m),
        ("--", "نابلس", 10m),
        ("بيت دجن", "نابلس", 60m),
        ("سبسطية", "نابلس", 40m),
        ("قوصين", "نابلس", 23m),
        ("سالم", "نابلس", 50m),
        ("دير الحطب", "نابلس", 50m),
        ("عزموط", "نابلس", 50m),
        ("طلوزة", "نابلس", 60m),
        ("بيت فوريك", "نابلس", 60m),
        ("الباذان", "نابلس", 70m),
        ("طوباس", "نابلس", 90m),
        ("النصارية", "نابلس", 80m),
        ("طمون", "نابلس", 80m),
    ];

    /// <summary>
    /// Phone is a system channel: calls are always attributed to it, and it
    /// cannot be deleted. The rest are the app channels from A-70, and the
    /// supervisor may add more.
    /// </summary>
    public static readonly (string Name, bool IsSystem)[] Channels =
    [
        (ChannelNames.Phone, true),
        ("WhatsApp", false),
        ("Facebook", false),
        ("Instagram", false),
        ("Wheels", false),
    ];

    /// <summary>
    /// The classification types from A-40. Order and Complaint are system types
    /// because reports depend on them existing (R-05, R-14, R-17).
    /// </summary>
    public static readonly (string Name, string LabelAr, string LabelEn, string Colour, bool IsSystem)[] ClassificationTypes =
    [
        ("Order",        "طلب",       "Order",        "#16a34a", true),
        ("Cancellation", "إلغاء",     "Cancellation", "#f97316", false),
        ("Complaint",    "شكوى",      "Complaint",    "#dc2626", true),
        ("Inquiry",      "استفسار",   "Inquiry",      "#2563eb", false),
        ("WrongNumber",  "رقم خاطئ",  "Wrong number", "#6b7280", false),
        ("Other",        "أخرى",      "Other",        "#8b5cf6", false),
    ];

    /// <summary>
    /// Form version 1: the default fields from A-40, without the example
    /// complaint-reason field in the schema document — the supervisor adds
    /// custom fields themselves (S-40).
    /// </summary>
    public const string FormDefinitionV1 = """
        {
          "fields": [
            { "key": "type", "kind": "type", "required": true },
            { "key": "branch", "kind": "branch", "required": true },
            { "key": "order_value", "kind": "number",
              "label": { "ar": "قيمة الطلب", "en": "Order value" },
              "required": false,
              "showWhenType": ["Order", "Cancellation"] },
            { "key": "notes", "kind": "textarea",
              "label": { "ar": "ملاحظات", "en": "Notes" },
              "required": false },
            { "key": "follow_up", "kind": "checkbox",
              "label": { "ar": "متابعة", "en": "Follow-up required" } }
          ]
        }
        """;

    /// <summary>
    /// The first outbound form: what an agent notes about a call they placed.
    /// A call-back or a confirmation has a type and a note, not a branch and an
    /// order value; the supervisor adds whatever else the site asks (S-40).
    /// </summary>
    public const string FormDefinitionOutV1 = """
        {
          "fields": [
            { "key": "type", "kind": "type", "required": true },
            { "key": "notes", "kind": "textarea",
              "label": { "ar": "ملاحظات", "en": "Notes" },
              "required": false },
            { "key": "follow_up", "kind": "checkbox",
              "label": { "ar": "متابعة", "en": "Follow-up required" } }
          ]
        }
        """;

    /// <summary>
    /// Defaults the supervisor can change without a deployment. Blank values are
    /// site-specific and filled in during installation.
    /// </summary>
    public static readonly (string Key, string Value)[] Settings =
    [
        // How long recordings are kept before the retention job deletes them (A-33, S-43).
        ("recording.retention_days", "90"),
        // How far back an agent's own call log reaches (A-50). A week. Not a
        // retention rule - nothing is deleted, and the contact history and the
        // supervisor's reports are unaffected.
        ("agent.call_log_days", "7"),

        // Idle logout, so a shared laptop does not leave the previous shift signed in (A-05).
        ("agent.idle_logout_minutes", "240"),

        // How long an agent may still edit their own classification (A-42).
        ("agent.edit_window", "SameDay"),

        // "Answered within N seconds" for the service level report (R-21).
        ("sla.answer_seconds", "20"),

        // Filled in at installation, once the client's PBX person has confirmed them.

        // The PBX is the provider's Issabel, reached over the VPN, so this is its
        // address on the VPN rather than one on the restaurant LAN.
        ("pbx.host", ""),

        // The other agents' and the branches' extension numbers (S-48). A call
        // whose other party is on this list is internal: still recorded, but
        // left out of the customer-facing reports. Comma separated.
        ("reports.internal_numbers", ""),
    ];

    /// <summary>The role the first account is created with.</summary>
    public const string FirstUserRole = UserRoles.Supervisor;
}
