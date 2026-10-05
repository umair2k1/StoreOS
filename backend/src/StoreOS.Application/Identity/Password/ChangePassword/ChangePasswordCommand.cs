using MediatR;

namespace StoreOS.Application.Identity.Password.ChangePassword;

public record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest;
