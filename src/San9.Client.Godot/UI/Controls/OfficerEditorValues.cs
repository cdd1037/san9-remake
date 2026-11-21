using San9.Content.Authoring;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Values shared by both forms; identities, relationships and persistence stay with each workspace.</summary>
internal sealed record OfficerEditorValues
{
    public required string Surname { get; init; }
    public required string GivenName { get; init; }
    public required string CourtesyName { get; init; }
    public required string Biography { get; init; }
    public required string? PortraitKey { get; init; }
    public required int FlagGlyph { get; init; }
    public required OfficerAbilities Abilities { get; init; }
    public required bool IsFemale { get; init; }
    public required int BirthYear { get; init; }
    public required int? AgeAtScenarioStart { get; init; }
    public required int AppearanceYear { get; init; }
    public required int LifespanTier { get; init; }
    public required int Personality { get; init; }
    public required int Righteousness { get; init; }
    public required int Ambition { get; init; }
    public required int Affinity { get; init; }
    public required int SuccessionPriority { get; init; }
    public required int ForceFounderPriority { get; init; }
    public required int TacticLinkBonus { get; init; }
    public required DuelAdvantages DuelAdvantages { get; init; }
    public required bool PrefersConsolidationLeadership { get; init; }
    public required bool AutomaticSurrenderAllowed { get; init; }
    public required bool HasRecruitmentPoliticsBonus { get; init; }
    public required OfficerTrainingFields Training { get; init; }

    public static OfficerEditorValues From(OfficerDefaults p) => new()
    {
        Surname = p.Surname,
        GivenName = p.GivenName,
        CourtesyName = p.CourtesyName ?? "",
        Biography = p.Biography ?? "",
        PortraitKey = p.PortraitKey,
        FlagGlyph = p.FlagGlyph,
        Abilities = new(p.War, p.Intelligence, p.Politics, p.Leadership),
        IsFemale = p.IsFemale,
        BirthYear = p.BirthYear,
        AgeAtScenarioStart = p.AgeAtScenarioStart,
        AppearanceYear = p.AppearanceYear,
        LifespanTier = p.LifespanTier,
        Personality = p.Personality,
        Righteousness = p.Righteousness,
        Ambition = p.Ambition,
        Affinity = p.Affinity,
        SuccessionPriority = p.SuccessionPriority,
        ForceFounderPriority = p.ForceFounderPriority,
        TacticLinkBonus = p.TacticLinkBonus,
        DuelAdvantages = p.DuelAdvantages,
        PrefersConsolidationLeadership = p.PrefersConsolidationLeadership,
        AutomaticSurrenderAllowed = p.AutomaticSurrenderAllowed,
        HasRecruitmentPoliticsBonus = p.HasRecruitmentPoliticsBonus,
        Training = new(p.TacticProficiencies, p.MasteredCategories, p.LearnedTactics),
    };

    public OfficerDefaults Apply(OfficerDefaults p) => p with
    {
        Surname = Surname,
        Name = Surname == p.Surname && GivenName == p.GivenName ? p.Name : Surname + GivenName,
        CourtesyName = OptionalText(CourtesyName, p.CourtesyName),
        Biography = OptionalText(Biography, p.Biography),
        PortraitKey = PortraitKey,
        FlagGlyph = FlagGlyph,
        War = Abilities.War, Intelligence = Abilities.Intelligence, Politics = Abilities.Politics, Leadership = Abilities.Leadership,
        IsFemale = IsFemale,
        BirthYear = BirthYear,
        AgeAtScenarioStart = AgeAtScenarioStart,
        AppearanceYear = AppearanceYear,
        LifespanTier = LifespanTier,
        Personality = Personality,
        Righteousness = Righteousness,
        Ambition = Ambition,
        Affinity = Affinity,
        SuccessionPriority = SuccessionPriority,
        ForceFounderPriority = ForceFounderPriority,
        TacticLinkBonus = TacticLinkBonus,
        DuelAdvantages = DuelAdvantages,
        PrefersConsolidationLeadership = PrefersConsolidationLeadership,
        AutomaticSurrenderAllowed = AutomaticSurrenderAllowed,
        HasRecruitmentPoliticsBonus = HasRecruitmentPoliticsBonus,
        TacticProficiencies = Training.Proficiencies, MasteredCategories = Training.MasteredCategories, LearnedTactics = Training.LearnedTactics,
    };
    private static string? OptionalText(string value, string? original) => value == (original ?? "")
        ? original : string.IsNullOrWhiteSpace(value) ? null : value;
}
