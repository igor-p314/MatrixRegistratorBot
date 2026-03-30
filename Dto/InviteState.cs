using System.Collections.Generic;

namespace MatrixRegistratorBot.Dto;

public sealed record InviteState
{
    public IReadOnlyCollection<RoomEvent> Events { get; set; } = [];
}
