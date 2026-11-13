using System.Collections.Immutable;

namespace CRMLite;

/// <summary>Fixed demo data. The clock is pinned to Tuesday 20 October 2026, 2:30 PM.</summary>
public static partial class Data
{
    public static DateTime Now => new(2026, 10, 20, 14, 30, 0);

    public static readonly ImmutableArray<Member> Team =
    [
        new("maya", "Maya Brooks", "maya@harbor.app", 0x4f6bedu),
        new("daniel", "Daniel Okafor", "daniel@harbor.app", 0x2f9e7au),
        new("sara", "Sara Lindqvist", "sara@harbor.app", 0xd9734au),
        new("leo", "Leo Marchetti", "leo@harbor.app", 0x8b6fdfu),
        new("nina", "Nina Patel", "nina@harbor.app", 0xc2577du)
    ];

    static DateTime At(int month, int day, int hour = 10, int minute = 0) => new(2026, month, day, hour, minute, 0);

    static DateOnly On(int year, int month, int day) => new(year, month, day);

    public static readonly ImmutableArray<Company> Companies =
    [
        new("northwind", "Northwind Labs", "northwind.dev", "Developer tools", "Seattle, WA",
            "Build and release platform for mobile teams. Moving usage billing for the enterprise plan off an in-house system before the next renewal.",
            0x1f2937u, "maya", 1480, At(10, 20, 11, 30), "Confirm audit log retention", true, On(2026, 6, 24)),
        new("kestrel", "Kestrel Freight", "kestrelfreight.com", "Logistics", "Rotterdam, NL",
            "Freight forwarding across three regions. Needs rating per lane and per region on a single invoice.",
            0xe0702bu, "maya", 4600, At(10, 20, 7, 30), "Send order form with volume tiers", true, On(2026, 5, 11)),
        new("lumen", "Lumen Health", "lumenhealth.io", "Healthcare", "Boston, MA",
            "Clinic management software. Wants seat billing for clinics with prepaid credits for telehealth minutes.",
            0x0e9488u, "daniel", 910, At(10, 19, 16), "Legal redlines due Friday", true, On(2026, 7, 2)),
        new("quarry", "Quarry", "quarry.app", "Data infrastructure", "Denver, CO",
            "Managed data pipelines. Evaluating usage to invoice for query volume.",
            0x334155u, "sara", 520, At(10, 19, 11), "Usage demo for finance ops", true, On(2026, 8, 3)),
        new("pinecrest", "Pinecrest Bank", "pinecrest.bank", "Banking", "Charlotte, NC",
            "Regional bank modernizing treasury services with a public API.",
            0x15803du, "nina", 190, At(10, 19, 9), "Kickoff with the platform team", false, On(2026, 4, 14)),
        new("tandem", "Tandem", "tandem.so", "Collaboration", "Austin, TX",
            "Shared workspaces for remote design reviews.",
            0x4f46e5u, "nina", 130, At(10, 18, 15), "Quarterly business review", false, On(2026, 3, 9)),
        new("orbital", "Orbital Analytics", "orbital.ai", "Analytics", "New York, NY",
            "Product analytics warehouse. Pricing by scanned rows with committed spend.",
            0x7c3aedu, "maya", 5900, At(10, 18, 10), "Architecture review on Thursday", true, On(2026, 7, 21)),
        new("brightwave", "Brightwave Media", "brightwave.tv", "Media", "Los Angeles, CA",
            "Streaming network selling self-serve ad inventory to small advertisers.",
            0x16a34au, "sara", 6800, At(10, 18, 9), "Answer procurement questions", false, On(2026, 6, 2)),
        new("harborline", "Harborline Travel", "harborline.com", "Travel", "Lisbon, PT",
            "Booking platform for independent travel agents, paid by commission.",
            0xe11d48u, "leo", 7200, At(10, 17, 14), "Share the integration guide", false, On(2026, 8, 18)),
        new("sprout", "Sprout Learning", "sprout.edu", "Education", "Toronto, ON",
            "Classroom software sold to school districts by seat.",
            0x65a30du, "leo", 760, At(10, 17, 11), "Pricing call with the district office", false, On(2026, 7, 30)),
        new("cobalt", "Cobalt Robotics", "cobaltrobotics.io", "Manufacturing", "Pittsburgh, PA",
            "Warehouse robot fleets billed by active robot hour.",
            0x2563ebu, "daniel", 1700, At(10, 16, 15), "Demo for the operations team", false, On(2026, 8, 25)),
        new("fernway", "Fernway Foods", "fernway.com", "Food & beverage", "Portland, OR",
            "Grocery delivery with subscriptions across four brands.",
            0xca8a04u, "maya", 1550, At(10, 16, 10), "Intro to the finance systems lead", true, On(2026, 9, 1)),
        new("glacier", "Glacier Cloud", "glaciercloud.net", "Cloud hosting", "Dublin, IE",
            "Object storage with a new cold archive tier.",
            0x0284c7u, "sara", 2900, At(10, 14, 12), "Scope the archive tier add-on", false, On(2026, 8, 7)),
        new("atlas", "Atlas Payments", "atlaspay.com", "Fintech", "London, UK",
            "Payment orchestration for marketplaces.",
            0x111827u, "sara", 8700, At(10, 13, 16), "Workshop on settlement rules", false, On(2026, 9, 3)),
        new("echo", "Echo Studios", "echostudios.gg", "Gaming", "Montreal, QC",
            "Multiplayer game studio selling creator credits.",
            0x9333eau, "leo", 640, At(10, 12, 15), "Confirm the creator credits use case", false, On(2026, 9, 10)),
        new("meadow", "Meadow Insurance", "meadow.insure", "Insurance", "Hartford, CT",
            "Small business insurance sold through brokers.",
            0x059669u, "nina", 85, At(10, 12, 10), "Onboarding check-in", false, On(2026, 5, 19)),
        new("ironclad", "Ironclad Security", "ironclad.sh", "Security", "Washington, DC",
            "Code scanning priced by repository and scan.",
            0x475569u, "daniel", 420, At(10, 11, 13), "Send the security questionnaire", false, On(2026, 9, 14)),
        new("juniper", "Juniper Retail", "juniper.shop", "Retail", "Chicago, IL",
            "Department store group launching a partner marketplace.",
            0x0f766eu, "daniel", 8300, At(10, 8, 11), "Find a champion in e-commerce", false, On(2026, 9, 22)),
        new("vantage", "Vantage Energy", "vantage.energy", "Energy", "Houston, TX",
            "Commercial solar operator piloting metered billing.",
            0xf59e0bu, "maya", 360, At(10, 7, 9), "Book a discovery call", false, On(2026, 9, 28)),
        new("riverstone", "Riverstone Legal", "riverstone.law", "Legal services", "Philadelphia, PA",
            "Law firm billing clients per matter.",
            0x78716cu, "nina", 48, At(10, 5, 16), "Send the onboarding recap", false, On(2026, 4, 2)),
        new("mosaic", "Mosaic Design", "mosaic.design", "Design agency", "Brooklyn, NY",
            "Brand studio reselling software seats to clients.",
            0xbe185du, "daniel", 590, At(10, 2, 11), "Follow up after the webinar", false, On(2026, 9, 30)),
        new("summit", "Summit Outdoor", "summitoutdoor.co", "Retail", "Boulder, CO",
            "Outdoor gear retailer with a points based loyalty program.",
            0xea580cu, "daniel", 970, At(9, 28, 15), "Book a discovery call", false, On(2026, 9, 18)),
        new("tidewater", "Tidewater Labs", "tidewater.bio", "Biotech", "San Diego, CA",
            "Sequencing lab selling instrument time to research teams.",
            0x4f46e5u, "leo", 210, At(9, 21, 10), "Revisit after their migration", false, On(2026, 8, 12))
    ];

    public static readonly ImmutableArray<Deal> Deals =
    [
        new("d-juniper", "juniper", "Partner billing", 392_000, Stage.Lead, On(2027, 2, 7), "daniel"),
        new("d-vantage", "vantage", "Metering pilot", 97_000, Stage.Lead, On(2027, 1, 12), "maya"),
        new("d-summit", "summit", "Loyalty credits", 81_000, Stage.Lead, On(2027, 1, 26), "daniel"),
        new("d-mosaic", "mosaic", "Agency seats", 68_000, Stage.Lead, On(2026, 12, 29), "daniel"),
        new("d-tidewater", "tidewater", "Instrument time billing", 59_000, Stage.Lead, On(2027, 2, 27), "leo"),
        new("d-atlas", "atlas", "Settlement invoicing", 322_000, Stage.Qualified, On(2027, 2, 17), "sara"),
        new("d-glacier", "glacier", "Archive tier add-on", 198_000, Stage.Qualified, On(2026, 12, 25), "sara"),
        new("d-fernway", "fernway", "Consolidated billing", 158_000, Stage.Qualified, On(2027, 1, 2), "maya"),
        new("d-echo", "echo", "Creator credits", 88_000, Stage.Qualified, On(2026, 12, 18), "leo"),
        new("d-ironclad", "ironclad", "Scan based pricing", 71_000, Stage.Qualified, On(2026, 12, 11), "daniel"),
        new("d-harborline", "harborline", "Agent commission invoicing", 244_000, Stage.Demo, On(2026, 12, 9), "leo"),
        new("d-orbital", "orbital", "Row metering", 228_000, Stage.Demo, On(2026, 12, 2), "maya"),
        new("d-cobalt", "cobalt", "Robot hour billing", 126_000, Stage.Demo, On(2026, 12, 6), "daniel"),
        new("d-quarry", "quarry", "Usage to invoice", 138_000, Stage.Demo, On(2026, 11, 25), "sara"),
        new("d-brightwave", "brightwave", "Self serve ad billing", 276_000, Stage.Proposal, On(2026, 11, 10), "sara"),
        new("d-northwind", "northwind", "Enterprise billing migration", 212_000, Stage.Proposal, On(2026, 11, 12), "maya"),
        new("d-sprout", "sprout", "District seat billing", 109_000, Stage.Proposal, On(2026, 11, 5), "leo"),
        new("d-kestrel", "kestrel", "Three region rating", 356_000, Stage.Negotiation, On(2026, 10, 23), "maya"),
        new("d-lumen", "lumen", "Clinic seats and credits", 164_000, Stage.Negotiation, On(2026, 10, 29), "daniel"),
        new("d-pinecrest", "pinecrest", "Treasury API plan", 42_000, Stage.Won, On(2026, 9, 30), "nina"),
        new("d-tandem", "tandem", "Team plan upgrade", 54_000, Stage.Won, On(2026, 10, 1), "nina"),
        new("d-meadow", "meadow", "Broker portal", 22_000, Stage.Won, On(2026, 9, 15), "nina"),
        new("d-riverstone", "riverstone", "Matter billing", 16_000, Stage.Won, On(2026, 8, 28), "nina")
    ];

    public static readonly ImmutableArray<string> Favorites = ["northwind", "kestrel", "lumen"];

    /// <summary>The model CRMLite starts with.</summary>
    public static CrmModel Initial(bool dark = false) =>
        new(Companies, Deals, Contacts, Activities, Inbox, Favorites, Now, dark);
}
