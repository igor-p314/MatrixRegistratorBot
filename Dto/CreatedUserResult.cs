using System.Net;

namespace MatrixRegistratorBot.Dto;

internal sealed record CreatedUserResult : CreateResult
{
    public string? Id { get; set; }

    public string? UserName { get; set; }
}
