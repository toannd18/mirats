using aspire_react.Server.Application.Users.DTOs;
using MediatR;

namespace aspire_react.Server.Application.Users.Queries;

public record GetUserByIdQuery(Guid Id) : IRequest<UserDto?>;