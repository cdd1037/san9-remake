using System.Text.Json.Serialization;

namespace San9.Core.Domain;

/// <summary>Host options for future automatic recruitment and support between corps.</summary>
public sealed record AutomaticCorpsOptions
{
    public static AutomaticCorpsOptions Default { get; } = new();

    [JsonRequired] public bool DomesticDelegationRecruitment { get; init; }
    [JsonRequired] public bool CrossCorpsTransport { get; init; } = true;
    [JsonRequired] public bool CrossCorpsRelief { get; init; } = true;
}
