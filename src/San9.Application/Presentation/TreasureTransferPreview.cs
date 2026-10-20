using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed record AttributeChange(string Title, int Before, int After)
{
    public string Text => After == Before ? Before.ToString() : $"{Before}{(After > Before ? "+" : "−")}{Math.Abs(After - Before)}";
}
public sealed record TreasureTransferPreview(int RulerId, int? TargetId, IReadOnlyList<AttributeChange> TargetAbilities,
    IReadOnlyList<AttributeChange> RulerAbilities, AttributeChange? Loyalty)
{
    public static TreasureTransferPreview Read(AppointmentDraft draft)
    {
        var view = draft.View;
        bool grant = draft.Kind == "bestow";
        int ruler = view.World.Forces.Single(f => f.Id == view.RequirePlayerForce()).LeaderId!.Value;
        var item = view.World.Treasures.SingleOrDefault(t => t.Id == draft.TreasureId);
        int? target = grant ? draft.Members.Cast<int?>().SingleOrDefault() : item?.HolderId;
        if (item is null) return new(ruler, target, [], [], null);
        var definition = view.Information.Treasures.Single(t => t.Id == item.Id);
        var rules = view.Rules.Abilities;
        // Preview a transfer over immutable holdings; use the same ability rules as submission.
        var holdings = view.World.Treasures.Select(t => t.Id == item.Id ? t with { HolderId = grant ? target : ruler } : t).ToArray();
        AttributeChange[] Abilities(int id)
        {
            var person = view.World.Officers.Single(p => p.Id == id);
            var before = rules.Evaluate(person, view.World.Treasures);
            var after = rules.Evaluate(person, holdings);
            var bonus = definition.AbilityBonuses;
            return new[] { ("统率",before.Leadership,after.Leadership,bonus.Leadership), ("武力",before.War,after.War,bonus.War),
                ("智力",before.Intelligence,after.Intelligence,bonus.Intelligence), ("政治",before.Politics,after.Politics,bonus.Politics) }
                .Where(a => a.Item4 != 0).Select(a => new AttributeChange(a.Item1,a.Item2,a.Item3)).ToArray();
        }
        AttributeChange? loyalty = null;
        if (target is int id)
        {
            var person = view.World.Officers.Single(p => p.Id == id);
            if (person.IsServing)
            {
                int previous = person.LoyaltyTo(view.RequirePlayerForce());
                int next = grant ? (int)Math.Min(255L, (long)previous + definition.LoyaltyBonus)
                    : (int)Math.Max(0L, previous - 3L * definition.LoyaltyBonus / 2);
                loyalty = new("忠诚", Math.Min(100,previous), Math.Min(100,next));
            }
        }
        return new(ruler,target,target is int selected ? Abilities(selected) : [],Abilities(ruler),loyalty);
    }
}
