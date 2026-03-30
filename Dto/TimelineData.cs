using System.Collections.Generic;

namespace MatrixRegistratorBot.Dto;

public sealed record TimelineData
{
    public IReadOnlyCollection<RoomEvent> Events { get; set; } = [];
}