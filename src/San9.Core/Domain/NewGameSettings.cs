using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public enum GameDifficulty { Beginner, Advanced }
public enum GameMode { Historical, Fictional }

/// <summary>Confirmed new-game choices. Resource additions are applied once before first-xun preparation.</summary>
public sealed record NewGameSettings
{
    public bool MixedTroopsEnabled { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? NaturalDeathsEnabled { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? EquipmentDevelopmentEnabled { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? FormationLearningEnabled { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? PersonalPeerageEnabled { get; init; }
    [JsonIgnore] public bool FormationKnowledgeEnabled => MixedTroopsEnabled || FormationLearningEnabled == true;
    [JsonIgnore] public bool FormationTrainingEnabled => FormationKnowledgeEnabled && FormationLearningEnabled != false;
    [JsonRequired] public GameDifficulty Difficulty { get; init; }
    [JsonRequired] public GameMode Mode { get; init; }
    [JsonRequired] public int MoneyBonus { get; init; }
    [JsonRequired] public int FoodBonus { get; init; }
    [JsonRequired] public int DomesticBonus { get; init; }
    [JsonRequired] public int SoldierBonus { get; init; }
    [JsonRequired] public int MoraleBonus { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Difficulty) || !Enum.IsDefined(Mode) || MoneyBonus is < 0 or > 50000 ||
            FoodBonus is < 0 or > 500000 || DomesticBonus is < 0 or > 1000 ||
            SoldierBonus is < 0 or > 100000 || MoraleBonus is < 0 or > 100)
            throw new ArgumentException("Invalid new-game settings.");
    }
}
