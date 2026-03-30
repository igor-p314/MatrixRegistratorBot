namespace MatrixRegistratorBot.Dto;

using System.Net;

internal record CreateResult
{
    public HttpStatusCode StatusCode { get; set; }
}
