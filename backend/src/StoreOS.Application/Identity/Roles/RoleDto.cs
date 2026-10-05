namespace StoreOS.Application.Identity.Roles;

public record RoleDto(Guid Id, string Name, IReadOnlyList<string> Permissions);
