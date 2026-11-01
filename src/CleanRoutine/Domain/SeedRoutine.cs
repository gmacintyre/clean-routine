namespace CleanRoutine.Domain;

/// <summary>The suggested starting routine every new household gets. They edit it in the app afterwards.
/// (The original household keeps its own routine, including the baby-gear day.)</summary>
public static class SeedRoutine
{
    public static void Apply(HouseholdData data)
    {
        data.Zones = new string[7];
        data.Zones[(int)DayOfWeek.Sunday] = "Rest & plan";
        data.Zones[(int)DayOfWeek.Monday] = "Kitchen";
        data.Zones[(int)DayOfWeek.Tuesday] = "Bathrooms";
        data.Zones[(int)DayOfWeek.Wednesday] = "Dusting";
        data.Zones[(int)DayOfWeek.Thursday] = "Floors";
        data.Zones[(int)DayOfWeek.Friday] = "Bedding & towels";
        data.Zones[(int)DayOfWeek.Saturday] = "Living areas";

        data.Tasks =
        [
            Daily("Make beds"),
            Daily("Do dishes or run the dishwasher"),
            Daily("Wipe kitchen counters and stovetop"),
            Daily("Quick wipe of bathroom sink and counter"),
            Daily("10-minute tidy"),
            Daily("Start and finish one load of laundry"),
            Daily("Take out trash if full"),

            Weekly(DayOfWeek.Monday, "Wipe appliance fronts and microwave"),
            Weekly(DayOfWeek.Monday, "Clean out the fridge"),
            Weekly(DayOfWeek.Monday, "Mop the kitchen floor"),
            Weekly(DayOfWeek.Tuesday, "Scrub toilets"),
            Weekly(DayOfWeek.Tuesday, "Clean tub and shower"),
            Weekly(DayOfWeek.Tuesday, "Clean mirrors"),
            Weekly(DayOfWeek.Tuesday, "Wash bathroom floors"),
            Weekly(DayOfWeek.Wednesday, "Dust surfaces and shelves"),
            Weekly(DayOfWeek.Wednesday, "Dust electronics"),
            Weekly(DayOfWeek.Wednesday, "Dust ceiling fan blades"),
            Weekly(DayOfWeek.Thursday, "Vacuum all rooms"),
            Weekly(DayOfWeek.Thursday, "Mop hard floors"),
            Weekly(DayOfWeek.Friday, "Change sheets"),
            Weekly(DayOfWeek.Friday, "Wash towels"),
            Weekly(DayOfWeek.Saturday, "Wipe door handles, light switches and remotes"),
            Weekly(DayOfWeek.Saturday, "Tidy and wipe the entryway"),
            Weekly(DayOfWeek.Saturday, "Shake out rugs and fluff cushions"),
            Weekly(DayOfWeek.Saturday, "Water plants"),
            Weekly(DayOfWeek.Sunday, "Plan meals for the week"),
            Weekly(DayOfWeek.Sunday, "Restock cleaning supplies"),

            Monthly(1, "Wipe baseboards, door handles and light switches"),
            Monthly(2, "Clean inside the oven and descale the kettle or coffee maker"),
            Monthly(3, "Vacuum couch cushions and under furniture"),
            Monthly(3, "Wash shower curtain liner and bath mats"),
            Monthly(4, "Clean dishwasher filter and washing machine seal"),

            Seasonal(1, "Wash windows and screens"),
            Seasonal(2, "Deep clean the fridge and freezer"),
            Seasonal(2, "Flip or rotate mattresses"),
            Seasonal(3, "Replace HVAC filters and test smoke detectors"),
            Seasonal(4, "Declutter closets and donate what you don't use"),
        ];
        data.Seeded = true;
    }

    static CleaningTask Daily(string title) => new() { Title = title, Frequency = Frequency.Daily };

    static CleaningTask Weekly(DayOfWeek day, string title) =>
        new() { Title = title, Frequency = Frequency.Weekly, Weekday = day };

    // Monthly and seasonal jobs land on a Saturday by default; change the day in the app.
    static CleaningTask Monthly(int week, string title) =>
        new() { Title = title, Frequency = Frequency.Monthly, Weekday = DayOfWeek.Saturday, WeekOfMonth = week };

    static CleaningTask Seasonal(int week, string title) =>
        new() { Title = title, Frequency = Frequency.Seasonal, Weekday = DayOfWeek.Saturday, WeekOfMonth = week };
}
