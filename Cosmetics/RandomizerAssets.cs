namespace BotRandomizer;

internal static class RandomizerAssets
{
    internal const byte TerroristTeam = 2;
    internal const byte CounterTerroristTeam = 3;

    internal static readonly KnifeDefinition[] Knives =
    [
        new("weapon_bayonet", 500, "weapons/models/knife/bayonet/weapon_bayonet.vmdl"),
        new("weapon_knife_karambit", 507, "weapons/models/knife/karambit/weapon_knife_karambit.vmdl"),
        new("weapon_knife_m9_bayonet", 508, "weapons/models/knife/m9_bayonet/weapon_knife_m9_bayonet.vmdl"),
        new("weapon_knife_butterfly", 515, "weapons/models/knife/butterfly/weapon_knife_butterfly.vmdl")
    ];

    internal static readonly IReadOnlyDictionary<string, ushort> KnifeDefIndexByName =
        new Dictionary<string, ushort>
        {
            ["weapon_bayonet"] = 500,
            ["weapon_knife_css"] = 503,
            ["weapon_knife_flip"] = 505,
            ["weapon_knife_gut"] = 506,
            ["weapon_knife_karambit"] = 507,
            ["weapon_knife_m9_bayonet"] = 508,
            ["weapon_knife_tactical"] = 509,
            ["weapon_knife_falchion"] = 512,
            ["weapon_knife_survival_bowie"] = 514,
            ["weapon_knife_butterfly"] = 515,
            ["weapon_knife_push"] = 516,
            ["weapon_knife_cord"] = 517,
            ["weapon_knife_canis"] = 518,
            ["weapon_knife_ursus"] = 519,
            ["weapon_knife_gypsy_jackknife"] = 520,
            ["weapon_knife_outdoor"] = 521,
            ["weapon_knife_stiletto"] = 522,
            ["weapon_knife_widowmaker"] = 523,
            ["weapon_knife_skeleton"] = 525,
            ["weapon_knife_kukri"] = 526
        };

    internal static readonly string[] CounterTerroristModels =
    [
        "agents\\models\\ctm_diver\\ctm_diver_varianta.vmdl",
        "agents\\models\\ctm_diver\\ctm_diver_variantb.vmdl",
        "agents\\models\\ctm_diver\\ctm_diver_variantc.vmdl",
        "agents\\models\\ctm_fbi\\ctm_fbi.vmdl",
        "agents\\models\\ctm_fbi\\ctm_fbi_varianta.vmdl",
        "agents\\models\\ctm_fbi\\ctm_fbi_variantb.vmdl",
        "agents\\models\\ctm_fbi\\ctm_fbi_variantc.vmdl",
        "agents\\models\\ctm_fbi\\ctm_fbi_variantd.vmdl",
        "agents\\models\\ctm_fbi\\ctm_fbi_variante.vmdl",
        "agents\\models\\ctm_fbi\\ctm_fbi_variantf.vmdl",
        "agents\\models\\ctm_fbi\\ctm_fbi_variantg.vmdl",
        "agents\\models\\ctm_fbi\\ctm_fbi_varianth.vmdl",
        "agents\\models\\ctm_gendarmerie\\ctm_gendarmerie_varianta.vmdl",
        "agents\\models\\ctm_gendarmerie\\ctm_gendarmerie_variantb.vmdl",
        "agents\\models\\ctm_gendarmerie\\ctm_gendarmerie_variantc.vmdl",
        "agents\\models\\ctm_gendarmerie\\ctm_gendarmerie_variantd.vmdl",
        "agents\\models\\ctm_gendarmerie\\ctm_gendarmerie_variante.vmdl",
        "agents\\models\\ctm_sas\\ctm_sas.vmdl",
        "agents\\models\\ctm_sas\\ctm_sas_variantf.vmdl",
        "agents\\models\\ctm_sas\\ctm_sas_variantg.vmdl",
        "agents\\models\\ctm_st6\\ctm_st6_variante.vmdl",
        "agents\\models\\ctm_st6\\ctm_st6_variantg.vmdl",
        "agents\\models\\ctm_st6\\ctm_st6_varianti.vmdl",
        "agents\\models\\ctm_st6\\ctm_st6_variantj.vmdl",
        "agents\\models\\ctm_st6\\ctm_st6_variantk.vmdl",
        "agents\\models\\ctm_st6\\ctm_st6_variantl.vmdl",
        "agents\\models\\ctm_st6\\ctm_st6_variantm.vmdl",
        "agents\\models\\ctm_st6\\ctm_st6_variantn.vmdl",
        "agents\\models\\ctm_swat\\ctm_swat_variante.vmdl",
        "agents\\models\\ctm_swat\\ctm_swat_variantf.vmdl",
        "agents\\models\\ctm_swat\\ctm_swat_variantg.vmdl",
        "agents\\models\\ctm_swat\\ctm_swat_varianth.vmdl",
        "agents\\models\\ctm_swat\\ctm_swat_varianti.vmdl",
        "agents\\models\\ctm_swat\\ctm_swat_variantj.vmdl",
        "agents\\models\\ctm_swat\\ctm_swat_variantk.vmdl"
    ];

    internal static readonly string[] TerroristModels =
    [
        "agents\\models\\tm_balkan\\tm_balkan_variantf.vmdl",
        "agents\\models\\tm_balkan\\tm_balkan_variantg.vmdl",
        "agents\\models\\tm_balkan\\tm_balkan_varianth.vmdl",
        "agents\\models\\tm_balkan\\tm_balkan_varianti.vmdl",
        "agents\\models\\tm_balkan\\tm_balkan_variantj.vmdl",
        "agents\\models\\tm_balkan\\tm_balkan_variantk.vmdl",
        "agents\\models\\tm_balkan\\tm_balkan_variantl.vmdl",
        "agents\\models\\tm_jungle_raider\\tm_jungle_raider_varianta.vmdl",
        "agents\\models\\tm_jungle_raider\\tm_jungle_raider_variantb.vmdl",
        "agents\\models\\tm_jungle_raider\\tm_jungle_raider_variantb2.vmdl",
        "agents\\models\\tm_jungle_raider\\tm_jungle_raider_variantc.vmdl",
        "agents\\models\\tm_jungle_raider\\tm_jungle_raider_variantd.vmdl",
        "agents\\models\\tm_jungle_raider\\tm_jungle_raider_variante.vmdl",
        "agents\\models\\tm_jungle_raider\\tm_jungle_raider_variantf.vmdl",
        "agents\\models\\tm_jungle_raider\\tm_jungle_raider_variantf2.vmdl",
        "agents\\models\\tm_leet\\tm_leet_varianta.vmdl",
        "agents\\models\\tm_leet\\tm_leet_variantb.vmdl",
        "agents\\models\\tm_leet\\tm_leet_variantc.vmdl",
        "agents\\models\\tm_leet\\tm_leet_variantd.vmdl",
        "agents\\models\\tm_leet\\tm_leet_variante.vmdl",
        "agents\\models\\tm_leet\\tm_leet_variantf.vmdl",
        "agents\\models\\tm_leet\\tm_leet_variantg.vmdl",
        "agents\\models\\tm_leet\\tm_leet_varianth.vmdl",
        "agents\\models\\tm_leet\\tm_leet_varianti.vmdl",
        "agents\\models\\tm_leet\\tm_leet_variantj.vmdl",
        "agents\\models\\tm_phoenix\\tm_phoenix.vmdl",
        "agents\\models\\tm_phoenix\\tm_phoenix_varianta.vmdl",
        "agents\\models\\tm_phoenix\\tm_phoenix_variantb.vmdl",
        "agents\\models\\tm_phoenix\\tm_phoenix_variantc.vmdl",
        "agents\\models\\tm_phoenix\\tm_phoenix_variantd.vmdl",
        "agents\\models\\tm_phoenix\\tm_phoenix_variantf.vmdl",
        "agents\\models\\tm_phoenix\\tm_phoenix_variantg.vmdl",
        "agents\\models\\tm_phoenix\\tm_phoenix_varianth.vmdl",
        "agents\\models\\tm_phoenix\\tm_phoenix_varianti.vmdl",
        "agents\\models\\tm_professional\\tm_professional_varf.vmdl",
        "agents\\models\\tm_professional\\tm_professional_varf1.vmdl",
        "agents\\models\\tm_professional\\tm_professional_varf2.vmdl",
        "agents\\models\\tm_professional\\tm_professional_varf3.vmdl",
        "agents\\models\\tm_professional\\tm_professional_varf4.vmdl",
        "agents\\models\\tm_professional\\tm_professional_varf5.vmdl",
        "agents\\models\\tm_professional\\tm_professional_varg.vmdl",
        "agents\\models\\tm_professional\\tm_professional_varh.vmdl",
        "agents\\models\\tm_professional\\tm_professional_vari.vmdl",
        "agents\\models\\tm_professional\\tm_professional_varj.vmdl"
    ];
}
