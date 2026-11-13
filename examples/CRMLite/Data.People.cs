using System.Collections.Immutable;

namespace CRMLite;

public static partial class Data
{
    public static readonly ImmutableArray<Contact> Contacts =
    [
        new("p-hana", "northwind", "Hana Kobayashi", "VP engineering", "hana@northwind.dev"),
        new("p-marco", "northwind", "Marco Silva", "Security lead", "marco@northwind.dev"),
        new("p-ruth", "northwind", "Ruth Adeyemi", "Head of finance", "ruth@northwind.dev"),
        new("p-jonas", "kestrel", "Jonas Vermeer", "Director of finance", "jonas@kestrelfreight.com"),
        new("p-ilse", "kestrel", "Ilse de Groot", "Billing operations", "ilse@kestrelfreight.com"),
        new("p-pieter", "kestrel", "Pieter Jansen", "CTO", "pieter@kestrelfreight.com"),
        new("p-grace", "lumen", "Grace Whitfield", "COO", "grace@lumenhealth.io"),
        new("p-samuel", "lumen", "Samuel Ortiz", "Revenue operations", "samuel@lumenhealth.io"),
        new("p-aiden", "quarry", "Aiden Clarke", "Head of data", "aiden@quarry.app"),
        new("p-olivia", "pinecrest", "Olivia Grant", "Platform lead", "olivia@pinecrest.bank"),
        new("p-felix", "tandem", "Felix Wagner", "Founder", "felix@tandem.so"),
        new("p-anika", "orbital", "Anika Rao", "VP analytics", "anika@orbital.ai"),
        new("p-carmen", "brightwave", "Carmen Diaz", "Ad operations lead", "carmen@brightwave.tv"),
        new("p-tomas", "harborline", "Tomas Ferreira", "Partner engineering", "tomas@harborline.com"),
        new("p-emily", "sprout", "Emily Chen", "Director of IT", "emily@sprout.edu"),
        new("p-victor", "cobalt", "Victor Hale", "Operations manager", "victor@cobaltrobotics.io"),
        new("p-laura", "fernway", "Laura Becker", "Finance systems lead", "laura@fernway.com"),
        new("p-ciaran", "glacier", "Ciaran Walsh", "Product manager", "ciaran@glaciercloud.net"),
        new("p-sophie", "atlas", "Sophie Lambert", "Payments lead", "sophie@atlaspay.com"),
        new("p-mateo", "echo", "Mateo Ruiz", "Community lead", "mateo@echostudios.gg"),
        new("p-hannah", "meadow", "Hannah Cole", "Broker success", "hannah@meadow.insure"),
        new("p-ethan", "ironclad", "Ethan Price", "CISO", "ethan@ironclad.sh"),
        new("p-zoe", "juniper", "Zoe Harper", "E-commerce director", "zoe@juniper.shop"),
        new("p-ryan", "vantage", "Ryan Cho", "Finance manager", "ryan@vantage.energy"),
        new("p-margaret", "riverstone", "Margaret Lowe", "Managing partner", "margaret@riverstone.law"),
        new("p-jules", "mosaic", "Jules Moreau", "Studio director", "jules@mosaic.design"),
        new("p-ben", "summit", "Ben Carter", "Head of loyalty", "ben@summitoutdoor.co"),
        new("p-alicia", "tidewater", "Alicia Moreno", "Lab operations", "alicia@tidewater.bio")
    ];

    public static readonly ImmutableArray<InboxThread> Inbox =
    [
        new("t-hana", "northwind", "Hana Kobayashi", "Re: SSO rollout for the platform team",
            "Marco approved SAML and SCIM. One open item on audit log retention for EU tenants.", At(10, 20, 13, 20), true),
        new("t-jonas", "kestrel", "Jonas Vermeer", "Order form questions",
            "Can tiers apply per region, or only across the whole contract?", At(10, 20, 7, 30), true),
        new("t-grace", "lumen", "Grace Whitfield", "Redlines attached",
            "Our counsel marked up sections 4 and 9. Happy to walk through them Thursday.", At(10, 19, 16, 10), true),
        new("t-laura", "fernway", "Laura Becker", "Intro: finance systems",
            "Thanks for the intro. Could we look at consolidated invoices across our four brands?", At(10, 16, 10, 5), true)
    ];
}
