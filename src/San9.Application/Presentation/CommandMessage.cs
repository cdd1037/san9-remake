using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed record CommandSpeaker(string Name, string? ForceName);
public sealed record CommandMessage(ProducedMessage Message, int? SpeakerId, string? SoundRole)
{
    public CommandSpeaker? Speaker { get; init; }
    /// <summary>A staged meeting speech, with actor gesture and meeting dialogue placement; local advice remains ordinary dialogue.</summary>
    public DiplomacyMeeting? Diplomacy { get; init; }
    public bool DiplomacyFarewell { get; init; }
    public MapSpeechMessage? MapSpeech { get; init; }
    public string? IllustrationRole { get; init; }
    public int? IllustrationPersonId { get; init; }
    public bool IllustrationDeceased { get; init; }
    public string? BackgroundMusicRole { get; init; }
    public bool BackgroundMusicLoop { get; init; }
    public MapCell? FocusLocation { get; init; }
    public bool WaitForMapSpeech { get; init; }
    public string? MovieRole { get; init; }
}
