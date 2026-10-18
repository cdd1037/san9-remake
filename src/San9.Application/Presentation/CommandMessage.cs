using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed record CommandSpeaker(string Name, string? ForceName);
public sealed record CommandMessage(ProducedMessage Message, int? SpeakerId, string? SoundRole)
{
    public CommandSpeaker? Speaker { get; init; }
    public MapSpeechMessage? MapSpeech { get; init; }
    public string? IllustrationRole { get; init; }
    public int? IllustrationPersonId { get; init; }
    public bool IllustrationDeceased { get; init; }
    public string? BackgroundMusicRole { get; init; }
    public MapCell? FocusLocation { get; init; }
    public bool WaitForMapSpeech { get; init; }
    public string? MovieRole { get; init; }
}
